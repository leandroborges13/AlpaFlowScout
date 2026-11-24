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

        return clubes.ToDictionary(c => c.Key, c => Decidir(c.Key, c.Value, jogos[c.Key].Select(j => (j.ClubeId, j.Data)),
            c.Value.Select(id => ultimoDoClube.TryGetValue(id, out var d) ? d : (DateTime?)null).Max(),
            id => nomes.GetValueOrDefault(id, "outro clube")));
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
