using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Estilo do time na liga (métricas de time da PitchAPI, ranqueadas contra os outros times da competição) ----------

/// <summary>
/// Uma métrica do time por jogo, com a posição no ranking da liga (1 = quem mais tem) e a média da liga.
/// `MaisEhMais`: false quando o número menor é o "mais" da frase (PPDA: menor = mais pressão).
/// `Frase`: pronta para vídeo, contando a posição a partir da ponta mais perto ("o 2º que mais perde a bola no próprio campo").
/// </summary>
public record MetricaNaLiga(string Chave, string Nome, double Valor, int Posicao, int Total, double MediaDaLiga, bool MaisEhMais, string Frase);

public record EstiloNaLiga(int ClubeId, string Time, int CompeticaoId, string Competicao, int Temporada, int Jogos,
    IReadOnlyList<MetricaNaLiga> Metricas, string Fonte);

/// <summary>Sem competição/temporada: a liga mais recente do time que tem métricas de time.</summary>
public record ObterEstiloNaLiga(int ClubeId, int? CompeticaoId = null, int? Temporada = null) : IQuery<EstiloNaLiga?>;

public class ObterEstiloNaLigaHandler(IScoutDb db) : IQueryHandler<ObterEstiloNaLiga, EstiloNaLiga?>
{
    /// <summary>Times com menos jogos que isso ficam fora do ranking (amostra curta distorce a média).</summary>
    public const int JogosMinimos = 5;

    /// <summary>Chave, nome, como ler a métrica de uma linha (com o adversário do jogo), se maior = "mais" e o texto do "mais"/"menos".</summary>
    record Def(string Chave, string Nome, Func<EstiloNoJogo, EstiloNoJogo?, double?> Ler, bool MaisEhMais, string Mais, string Menos);

    static readonly Def[] Defs =
    [
        new("perdasNoProprioCampo", "Bolas perdidas no próprio campo", (_, rival) => rival?.RecuperacoesAltas, true,
            "que mais perde a bola no próprio campo", "que menos perde a bola no próprio campo"),
        new("recuperacoesAltas", "Bolas recuperadas no campo de ataque", (e, _) => e.RecuperacoesAltas, true,
            "que mais rouba a bola no campo de ataque", "que menos rouba a bola no campo de ataque"),
        new("ppda", "Pressão (PPDA: passes do rival por ação defensiva)", (e, _) => e.Ppda, false,
            "que mais pressiona", "que menos pressiona"),
        new("recuperacoesEm5s", "Recupera a bola em até 5 s depois de perder", (e, _) => e.RecuperacoesEm5s, true,
            "que mais recupera a bola logo depois de perder", "que menos recupera a bola logo depois de perder"),
        new("alturaDefesa", "Altura média da defesa (m)", (e, _) => e.AlturaDefesa, true,
            "que defende mais adiantado", "que defende mais recuado"),
        new("fieldTilt", "Domínio territorial (field tilt, %)", (e, _) => e.FieldTilt, true,
            "que mais joga no campo de ataque", "que menos joga no campo de ataque"),
        new("ataquesDiretos", "Ataques diretos", (e, _) => e.AtaquesDiretos, true,
            "que mais ataca direto", "que menos ataca direto"),
        new("ataquesConstruidos", "Ataques construídos (troca de passes)", (e, _) => e.AtaquesConstruidos, true,
            "que mais constrói as jogadas com passes", "que menos constrói as jogadas com passes"),
        new("passesProgressivos", "Passes progressivos", (e, _) => e.PassesProgressivos, true,
            "que mais passa a bola pra frente", "que menos passa a bola pra frente"),
        new("entradasArea", "Entradas na área", (e, _) => e.EntradasArea, true,
            "que mais chega na área", "que menos chega na área"),
    ];

    public async Task<EstiloNaLiga?> Handle(ObterEstiloNaLiga q, CancellationToken ct)
    {
        var clube = await db.Clubes.FirstOrDefaultAsync(c => c.Id == q.ClubeId, ct);
        if (clube is null) return null;

        // as linhas do time com a competição de cada jogo; sem competição pedida, a liga mais recente com dados
        var doTime = db.EstilosNoJogo.Where(e => e.ClubeId == q.ClubeId)
            .Join(db.Partidas, e => e.PartidaId, p => p.Id, (e, p) => new { p.CompeticaoId, p.Temporada });
        if (q.CompeticaoId is { } c0) doTime = doTime.Where(x => x.CompeticaoId == c0);
        if (q.Temporada is { } t0) doTime = doTime.Where(x => x.Temporada == t0);
        var alvo = await doTime.GroupBy(x => new { x.CompeticaoId, x.Temporada })
            .Select(g => new { g.Key.CompeticaoId, g.Key.Temporada, Jogos = g.Count() })
            .OrderByDescending(x => x.Temporada).ThenByDescending(x => x.Jogos).FirstOrDefaultAsync(ct);
        if (alvo is null) return null;

        var linhas = await db.EstilosNoJogo
            .Join(db.Partidas.Where(p => p.CompeticaoId == alvo.CompeticaoId && p.Temporada == alvo.Temporada), e => e.PartidaId, p => p.Id, (e, p) => e)
            .ToListAsync(ct);
        var competicao = await db.Competicoes.Where(c => c.Id == alvo.CompeticaoId).Select(c => c.Nome).FirstOrDefaultAsync(ct) ?? "";
        var metricas = Calcular(linhas, q.ClubeId);
        if (metricas is null) return null;
        return new EstiloNaLiga(clube.Id, clube.Nome, alvo.CompeticaoId, competicao, alvo.Temporada, alvo.Jogos, metricas, "PitchAPI");
    }

    /// <summary>
    /// Médias por jogo de cada time e o ranking. Nulo se o time não tem jogos suficientes.
    /// Público para os testes e para o roteiro do pós-jogo.
    /// </summary>
    public static IReadOnlyList<MetricaNaLiga>? Calcular(IReadOnlyList<EstiloNoJogo> linhas, int clubeId)
    {
        var porJogo = linhas.GroupBy(l => l.PartidaId).ToDictionary(g => g.Key, g => g.ToList());
        EstiloNoJogo? Rival(EstiloNoJogo e) => porJogo[e.PartidaId].FirstOrDefault(o => o.ClubeId != e.ClubeId);
        var porTime = linhas.GroupBy(l => l.ClubeId).Where(g => g.Count() >= JogosMinimos).ToDictionary(g => g.Key, g => g.ToList());
        if (!porTime.ContainsKey(clubeId)) return null;

        var r = new List<MetricaNaLiga>();
        foreach (var d in Defs)
        {
            var medias = porTime.ToDictionary(kv => kv.Key, kv =>
            {
                var v = kv.Value.Select(e => d.Ler(e, Rival(e))).Where(x => x is not null).Select(x => x!.Value).ToList();
                return v.Count == 0 ? (double?)null : v.Average();
            }).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
            if (!medias.TryGetValue(clubeId, out var valor)) continue;
            // posição: 1 = quem tem "mais" da métrica (no PPDA, o menor número)
            var ordem = (d.MaisEhMais ? medias.OrderByDescending(kv => kv.Value) : medias.OrderBy(kv => kv.Value)).Select(kv => kv.Key).ToList();
            var pos = ordem.IndexOf(clubeId) + 1;
            var total = ordem.Count;
            var frase = pos <= (total + 1) / 2 ? $"o {Ordinal(pos)} {d.Mais}" : $"o {Ordinal(total - pos + 1)} {d.Menos}";
            r.Add(new MetricaNaLiga(d.Chave, d.Nome, Math.Round(valor, 2), pos, total, Math.Round(medias.Values.Average(), 2), d.MaisEhMais, frase));
        }
        return r;
    }

    /// <summary>1 → "time", 2 → "2º time": "o time que mais..." soa melhor que "o 1º que mais...".</summary>
    static string Ordinal(int n) => n == 1 ? "time" : $"{n}º time";
}
