using Microsoft.EntityFrameworkCore;

namespace Scout.Application.Consultas;

/// <summary>
/// Se o atleta ainda está no clube. `NoElenco` falso quando:
/// - jogou por outro clube depois do último jogo pelo clube (`SaiuPara`: o clube novo); ou
/// - não aparece num jogo do clube há mais de <see cref="ElencoAtual.DiasSemJogar"/> dias, contados até o último jogo do clube
///   (saída, empréstimo ou lesão longa: o provedor não diz qual).
/// Sem nenhum jogo registrado pelo clube (liga importada sem escalação), não há como saber: fica no elenco.
/// `Situacao`: a frase pronta para a tela e o chat ("saiu: joga pelo Fluminense desde 09/2026").
/// </summary>
public record VinculoAtual(int AtletaId, bool NoElenco, DateTime? UltimoJogoPeloClube, string? SaiuPara, string? Situacao);

public static class ElencoAtual
{
    public const int DiasSemJogar = 60;

    /// <summary>
    /// Para cada atleta, o vínculo com os clubes dele (`clubes`: os ids que contam como "o clube", ex.: os de uma linha da análise).
    /// Jogo = titular na escalação ou minutos em campo na estatística por partida (quem entrou do banco).
    /// </summary>
    public static async Task<Dictionary<int, VinculoAtual>> Avaliar(IScoutDb db, IReadOnlyDictionary<int, int[]> clubes, CancellationToken ct)
    {
        var ids = clubes.Keys.ToList();
        if (ids.Count == 0) return [];
        var titular = await (from e in db.Escalacoes.AsNoTracking()
                             join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                             where ids.Contains(e.AtletaId)
                             select new { e.AtletaId, e.ClubeId, p.Data }).ToListAsync(ct);
        var emCampo = await (from d in db.DesempenhosAvancados.AsNoTracking()
                             join p in db.Partidas.AsNoTracking() on d.PartidaId equals p.Id
                             where d.AtletaId != null && ids.Contains(d.AtletaId.Value) && d.Minutos > 0
                             select new { AtletaId = d.AtletaId!.Value, d.ClubeId, p.Data }).ToListAsync(ct);
        var jogos = titular.Concat(emCampo).ToLookup(j => j.AtletaId);

        // Último jogo de cada clube com escalação: a régua dos 60 dias (no fim da temporada não marca o elenco inteiro)
        var dosClubes = clubes.Values.SelectMany(c => c).Distinct().ToList();
        var ultimoDoClube = (await (from e in db.Escalacoes.AsNoTracking()
                                    join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                                    where dosClubes.Contains(e.ClubeId)
                                    group p by e.ClubeId into g
                                    select new { ClubeId = g.Key, Data = g.Max(p => p.Data) }).ToListAsync(ct))
            .ToDictionary(x => x.ClubeId, x => x.Data);
        var outros = jogos.SelectMany(g => g).Select(j => j.ClubeId).Distinct().ToList();
        var nomes = await db.Clubes.AsNoTracking().Where(c => outros.Contains(c.Id) || dosClubes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, ct);

        var r = clubes.ToDictionary(c => c.Key, c => Decidir(c.Key, c.Value, jogos[c.Key].Select(j => (j.ClubeId, j.Data)),
            c.Value.Select(id => ultimoDoClube.TryGetValue(id, out var d) ? d : (DateTime?)null).Max(),
            id => nomes.GetValueOrDefault(id, "outro clube")));

        // "Fora dos jogos" é ambíguo (saída, empréstimo ou lesão): confirma pelas transferências já guardadas do provedor (sem
        // consultar de novo; quem passou pelo perfil ou pelo comando trajetorias já tem)
        var duvida = r.Values.Where(v => !v.NoElenco && v.SaiuPara is null && v.UltimoJogoPeloClube is not null).ToList();
        if (duvida.Count > 0)
        {
            var guardadas = await db.RespostasGuardadas(duvida.Select(v => ChaveTransferencias(v.AtletaId)).ToList(), ct);
            foreach (var v in duvida)
                if (guardadas.TryGetValue(ChaveTransferencias(v.AtletaId), out var json))
                {
                    var nomesDoClube = clubes[v.AtletaId].Select(id => nomes.GetValueOrDefault(id, "")).Where(n => n != "").ToList();
                    r[v.AtletaId] = ConfirmarPelaTransferencia(v, LerTransferencias(json), nomesDoClube);
                }
        }
        return r;
    }

    /// <summary>A chave com que o cliente do provedor guarda as transferências do atleta (ApiFootballClient: "endpoint?parâmetros").</summary>
    public static string ChaveTransferencias(int atletaId) => $"/transfers?player={atletaId}";

    /// <summary>As transferências do JSON do /transfers da API-Football (o mesmo formato da trajetória). Público para os testes.</summary>
    public static List<Transferencia> LerTransferencias(string json)
    {
        var lista = new List<Transferencia>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.GetProperty("response").EnumerateArray())
                foreach (var t in item.GetProperty("transfers").EnumerateArray())
                {
                    string? Texto(params string[] c)
                    {
                        var e = t;
                        foreach (var p in c) if (e.ValueKind != System.Text.Json.JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
                        return e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null;
                    }
                    lista.Add(new Transferencia(DateOnly.TryParse(Texto("date"), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
                        Texto("teams", "out", "name"), Texto("teams", "in", "name"), Texto("type")));
                }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException) { /* JSON estranho: sem confirmação */ }
        return lista;
    }

    /// <summary>
    /// Troca o "fora dos jogos" pela transferência quando há uma saída do clube a partir de 30 dias antes do último jogo por ele:
    /// "emprestado ao X desde 08/2026" ou "transferido para o X em 08/2026". Sem saída registrada, diz isso (lesão ou fora dos
    /// planos), o que também é informação. A transferência é do provedor e pode atrasar alguns dias.
    /// </summary>
    public static VinculoAtual ConfirmarPelaTransferencia(VinculoAtual v, IReadOnlyList<Transferencia> transferencias, IReadOnlyList<string> nomesDoClube)
    {
        static string N(string? s) => new string((s ?? "").Normalize(System.Text.NormalizationForm.FormD)
            .Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var doClube = nomesDoClube.Select(N).ToHashSet();
        var desde = DateOnly.FromDateTime(v.UltimoJogoPeloClube!.Value).AddDays(-30);
        var saida = transferencias.Where(t => t.Data is { } d && d >= desde && doClube.Contains(N(t.De)) && !string.IsNullOrWhiteSpace(t.Para))
            .OrderByDescending(t => t.Data).FirstOrDefault();
        if (saida is null)
            return v with { Situacao = $"{v.Situacao?.Replace(" (saída, empréstimo ou lesão)", "")} (sem transferência registrada: lesão ou fora dos planos)" };
        var emprestimo = saida.Tipo?.Contains("loan", StringComparison.OrdinalIgnoreCase) == true || saida.Tipo?.Contains("empr", StringComparison.OrdinalIgnoreCase) == true;
        return v with
        {
            SaiuPara = saida.Para,
            Situacao = emprestimo ? $"emprestado ao {saida.Para} desde {saida.Data:MM/yyyy}" : $"transferido para o {saida.Para} em {saida.Data:MM/yyyy}",
        };
    }

    /// <summary>
    /// A regra, sem banco: `jogos` são os jogos do atleta (qualquer clube) e `ultimoDoTime` o último jogo do clube com escalação.
    /// </summary>
    public static VinculoAtual Decidir(int atleta, int[] doClube, IEnumerable<(int ClubeId, DateTime Data)> jogos, DateTime? ultimoDoTime,
        Func<int, string> nome)
    {
        var lista = jogos.ToList();
        var pelo = lista.Where(j => doClube.Contains(j.ClubeId)).OrderByDescending(j => j.Data).Select(j => ((int, DateTime)?)j).FirstOrDefault();
        if (pelo is not { } p) return new VinculoAtual(atleta, true, null, null, null);

        var depois = lista.Where(j => !doClube.Contains(j.ClubeId) && j.Data > p.Item2).OrderBy(j => j.Data).Select(j => ((int, DateTime)?)j).FirstOrDefault();
        if (depois is { } d)
        {
            var novo = nome(d.Item1);
            return new VinculoAtual(atleta, false, p.Item2, novo, $"saiu: joga pelo {novo} desde {d.Item2:MM/yyyy}");
        }
        if (ultimoDoTime is { } u && (u - p.Item2).TotalDays > DiasSemJogar)
            return new VinculoAtual(atleta, false, p.Item2, null,
                $"fora dos jogos do {nome(p.Item1)} desde {p.Item2:dd/MM/yyyy} (saída, empréstimo ou lesão)");
        return new VinculoAtual(atleta, true, p.Item2, null, null);
    }
}
