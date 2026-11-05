using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Prancheta tática (montar a escalação para gravar vídeo) ----------

/// <summary>Uma vaga do esquema: a grade do provedor ("linha:coluna"), o nome da função e o ponto no campo (0–100, como em
/// <see cref="FuncaoTatica.Coordenadas"/>). `AtletaId`/`Nome`: quem a escalação provável põe ali (nulo se ninguém serve).</summary>
public record VagaNaPrancheta(string Grade, string Funcao, double X, double Y, int? AtletaId, string? Nome);

/// <summary>Atleta do elenco para a prancheta. `Titularidades`: na temporada; `Recentes`: nos últimos jogos do time.</summary>
public record AtletaDaPrancheta(int Id, string Nome, string? Funcao, string Setor, int Titularidades, int Recentes);

public record EsquemaRecente(string Esquema, int Jogos);

public record Prancheta(int ClubeId, string Time, int Temporada, string Esquema, IReadOnlyList<EsquemaRecente> Esquemas,
    IReadOnlyList<VagaNaPrancheta> Vagas, IReadOnlyList<AtletaDaPrancheta> Elenco, string Explicacao);

/// <summary>Sem esquema, o mais usado nos últimos jogos do time.</summary>
public record ObterPrancheta(int ClubeId, string? Esquema = null) : IQuery<Prancheta?>;

public class ObterPranchetaHandler(IScoutDb db) : IQueryHandler<ObterPrancheta, Prancheta?>
{
    public const int JogosRecentes = 10;

    /// <summary>Esquemas que a prancheta oferece além dos que o time usou.</summary>
    public static readonly string[] EsquemasComuns = ["4-3-3", "4-2-3-1", "4-4-2", "4-1-4-1", "4-3-1-2", "3-5-2", "3-4-3", "3-4-2-1", "5-3-2", "4-5-1"];

    /// <summary>Uma titularidade: em que função e se foi nos últimos jogos (pesa mais).</summary>
    public record Titularidade(int AtletaId, string Funcao, Setor Setor, Lado Lado, bool Recente);

    public async Task<Prancheta?> Handle(ObterPrancheta q, CancellationToken ct)
    {
        var clube = await db.Clubes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ClubeId, ct);
        if (clube is null) return null;
        var jogos = await db.Partidas.AsNoTracking()
            .Where(p => (p.MandanteId == q.ClubeId || p.VisitanteId == q.ClubeId) && db.Escalacoes.Any(e => e.PartidaId == p.Id && e.ClubeId == q.ClubeId))
            .OrderByDescending(p => p.Data).Select(p => new { p.Id, p.Temporada }).Take(80).ToListAsync(ct);
        if (jogos.Count == 0) return null;
        var temporada = jogos[0].Temporada;
        var daTemporada = jogos.Where(j => j.Temporada == temporada).Select(j => j.Id).ToList();
        var recentes = daTemporada.Take(JogosRecentes).ToHashSet();

        var escalacoes = await db.Escalacoes.AsNoTracking().Where(e => e.ClubeId == q.ClubeId && daTemporada.Contains(e.PartidaId))
            .Select(e => new { e.PartidaId, e.AtletaId, e.Esquema, e.Funcao, e.Setor, e.Lado }).ToListAsync(ct);

        // Menor de 18 ou sem data de nascimento fica fora, como em toda a plataforma (o nome pode ser digitado na prancheta)
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var ids = escalacoes.Select(e => e.AtletaId).Distinct().ToList();
        var atletas = (await db.Atletas.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct))
            .Where(a => a.PodeSerAnalisado(hoje)).ToDictionary(a => a.Id, a => a.Nome);

        var esquemas = escalacoes.Where(e => recentes.Contains(e.PartidaId)).GroupBy(e => e.PartidaId).Select(g => g.First().Esquema)
            .GroupBy(e => e).Select(g => new EsquemaRecente(g.Key, g.Count())).OrderByDescending(e => e.Jogos).ToList();
        var esquema = Valido(q.Esquema) ? q.Esquema! : esquemas.Select(e => e.Esquema).FirstOrDefault(Valido) ?? "4-3-3";

        var historico = escalacoes.Where(e => atletas.ContainsKey(e.AtletaId))
            .Select(e => new Titularidade(e.AtletaId, e.Funcao, e.Setor, e.Lado, recentes.Contains(e.PartidaId))).ToList();
        var vagas = Escalar(Vagas(esquema), historico).Select(v => v with { Nome = v.AtletaId is { } id ? atletas[id] : null }).ToList();

        var elenco = historico.GroupBy(h => h.AtletaId).Select(g => new AtletaDaPrancheta(g.Key, atletas[g.Key],
                g.GroupBy(h => h.Funcao).OrderByDescending(f => f.Count()).First().Key,
                g.GroupBy(h => h.Setor).OrderByDescending(f => f.Count()).First().Key.ToString(), g.Count(), g.Count(h => h.Recente)))
            .OrderBy(a => Enum.Parse<Setor>(a.Setor)).ThenByDescending(a => a.Recentes).ThenByDescending(a => a.Titularidades).ToList();

        return new Prancheta(clube.Id, clube.Nome, temporada, esquema, esquemas, vagas, elenco,
            $"Escalação provável: para cada posição do {esquema}, quem mais começou jogando nela (os últimos {JogosRecentes} jogos pesam seis vezes mais), " +
            "depois quem joga numa função parecida (lateral na ala, zagueiro na linha de 3) ou no mesmo setor e lado. Só titulares com 18 anos ou mais aparecem; outros nomes podem ser digitados.");
    }

    public static bool Valido(string? esquema) =>
        !string.IsNullOrWhiteSpace(esquema) && esquema.Split('-') is { Length: >= 2 } p && p.All(x => int.TryParse(x, out var n) && n > 0) &&
        p.Sum(int.Parse) == 10;

    /// <summary>As 11 vagas do esquema, do goleiro ao ataque, com a função e o ponto no campo.</summary>
    public static List<VagaNaPrancheta> Vagas(string esquema)
    {
        var linhas = esquema.Split('-').Select(int.Parse).ToArray();
        var grades = new List<string> { "1:1" };
        for (var l = 0; l < linhas.Length; l++)
            for (var c = 1; c <= linhas[l]; c++) grades.Add($"{l + 2}:{c}");
        return grades.Select(g =>
        {
            var p = FuncaoTatica.Identificar(esquema, g)!;
            var (x, y) = FuncaoTatica.Coordenadas(esquema, g)!.Value;
            return new VagaNaPrancheta(g, p.Funcao, x, y, null, null);
        }).ToList();
    }

    /// <summary>
    /// Escalação provável: pontua cada par (vaga, atleta) pelo histórico de titularidades — mesma função vale mais que mesmo setor e
    /// lado, jogo recente vale mais que antigo — e preenche do par mais forte para o mais fraco, sem repetir vaga nem atleta.
    /// Goleiro só vai para o gol. Sem sinal nenhum para uma vaga, ela fica vazia.
    /// </summary>
    public static List<VagaNaPrancheta> Escalar(List<VagaNaPrancheta> vagas, IReadOnlyList<Titularidade> historico)
    {
        var porAtleta = historico.GroupBy(h => h.AtletaId).ToDictionary(g => g.Key, g => g.ToList());
        var pares = new List<(int Vaga, int Atleta, double Pontos)>();
        for (var i = 0; i < vagas.Count; i++)
        {
            var alvo = FuncaoTatica.Identificar(EsquemaDa(vagas), vagas[i].Grade)!;
            foreach (var (atleta, hs) in porAtleta)
            {
                var goleiro = hs.Count(h => h.Setor == Setor.Goleiro) * 2 > hs.Count;
                if (goleiro != (alvo.Setor == Setor.Goleiro)) continue;
                double pontos = 0;
                foreach (var h in hs)
                {
                    // Os últimos jogos mostram o time de agora: o goleiro que assumiu há 9 jogos passa o que jogou o primeiro turno
                    var peso = h.Recente ? 6 : 1;
                    if (h.Funcao == alvo.Funcao) pontos += 4 * peso;
                    else if (Familia(h.Funcao) == Familia(alvo.Funcao)) pontos += 3 * peso; // lateral na ala, zagueiro no meio da linha de 3
                    else if (h.Setor == alvo.Setor && h.Lado == alvo.Lado) pontos += 2 * peso;
                    else if (h.Setor == alvo.Setor) pontos += 1 * peso;
                    else pontos += 0.1 * peso; // joga em outro setor: só desempata
                }
                if (pontos > 0) pares.Add((i, atleta, pontos));
            }
        }
        var resultado = vagas.ToList();
        var usados = new HashSet<int>();
        foreach (var (vaga, atleta, _) in pares.OrderByDescending(p => p.Pontos).ThenBy(p => p.Atleta))
        {
            if (resultado[vaga].AtletaId is not null || usados.Contains(atleta)) continue;
            resultado[vaga] = resultado[vaga] with { AtletaId = atleta };
            usados.Add(atleta);
        }
        return resultado;
    }

    /// <summary>Funções que pedem o mesmo jogador, mudando só o esquema: lateral e ala do mesmo lado, os zagueiros, volante e
    /// meio-campista, meia e ponta do mesmo lado, e centroavante com a dupla de ataque.</summary>
    public static string Familia(string funcao)
    {
        var lado = funcao.EndsWith("esquerdo") || funcao.EndsWith("esquerda") ? "E" : funcao.EndsWith("direito") || funcao.EndsWith("direita") ? "D" : "C";
        if (funcao.StartsWith("Zagueiro")) return "zagueiro";
        if (funcao.StartsWith("Lateral") || funcao.StartsWith("Ala")) return "lateral" + lado;
        if (funcao is "Volante" || funcao.StartsWith("Meio-campista")) return "volante";
        // "Atacante esquerdo/direito" é a dupla de ataque (4-4-2, 3-5-2): pede centroavante, não ponta
        if (funcao is "Centroavante" || funcao.StartsWith("Atacante")) return "centroavante";
        if (funcao.StartsWith("Meia") || funcao.StartsWith("Ponta") || funcao.StartsWith("Atacante")) return lado == "C" ? "meia" : "aberto" + lado;
        return funcao;
    }

    // As vagas guardam só a grade; o esquema sai da quantidade de vagas em cada linha
    static string EsquemaDa(List<VagaNaPrancheta> vagas) =>
        string.Join('-', vagas.Where(v => v.Grade != "1:1").GroupBy(v => v.Grade.Split(':')[0]).OrderBy(g => int.Parse(g.Key)).Select(g => g.Count()));
}
