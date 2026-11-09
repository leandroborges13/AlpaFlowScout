using Microsoft.EntityFrameworkCore;

namespace Scout.Application.Consultas;

// ---------- Sequências e marcas (backlog item 3: gancho de título, "o Galo não perde há 7 jogos") ----------

/// <summary>Um jogo citado numa sequência, do ponto de vista de quem lê: "Galo 3 x 0 Grêmio", data de Brasília.</summary>
public record JogoDaSequencia(int Id, DateOnly Data, string Jogo, string Competicao, bool EmCasa, string Quando);

/// <summary>
/// Uma sequência. Na atual: `Jogos` seguidos até o último jogo, `Desde` = o primeiro deles e `QuebradaEm` = o jogo anterior,
/// que interrompeu a sequência de antes (ex.: a última derrota, para "invicto"). No recorde: de `Desde` a `Ate`.
/// </summary>
public record Sequencia(string Tipo, string Descricao, int Jogos, JogoDaSequencia? Desde, JogoDaSequencia? Ate, JogoDaSequencia? QuebradaEm);

public record Goleada(int Saldo, JogoDaSequencia Jogo);

/// <summary>`Atuais`: a contagem atual de cada tipo (0 = não está valendo; o `QuebradaEm` é o último jogo, que a interrompeu); `EmCasa`/`ForaDeCasa`: as atuais só com jogos naquele mando;
/// `Recordes`: a maior de cada tipo no período.</summary>
public record ResultadoSequencias(string Time, string Periodo, int Jogos, IReadOnlyList<Sequencia> Atuais, IReadOnlyList<Sequencia> EmCasa,
    IReadOnlyList<Sequencia> ForaDeCasa, IReadOnlyList<Sequencia> Recordes, Goleada? MaiorVitoria, Goleada? MaiorDerrota);

public record ObterSequencias(string Time, DateOnly? De = null, DateOnly? Ate = null, int? CompeticaoId = null, int? Temporada = null, string? Treinador = null)
    : IQuery<ResultadoSequencias?>;

/// <summary>Um jogo encerrado do ponto de vista do time (gols pró e contra); a disputa de pênaltis não muda o resultado (empate).</summary>
public record JogoDoTimeNaSequencia(int Id, DateTime Data, string Time, string Adversario, bool EmCasa, int GolsPro, int GolsContra, string Competicao);

public class ObterSequenciasHandler(IScoutDb db) : IQueryHandler<ObterSequencias, ResultadoSequencias?>
{
    static readonly (string Tipo, string Descricao, Func<JogoDoTimeNaSequencia, bool> Vale)[] Tipos =
    [
        ("invicto", "jogos sem perder", j => j.GolsPro >= j.GolsContra),
        ("vitorias", "vitórias seguidas", j => j.GolsPro > j.GolsContra),
        ("sem_vencer", "jogos sem vencer", j => j.GolsPro <= j.GolsContra),
        ("derrotas", "derrotas seguidas", j => j.GolsPro < j.GolsContra),
        ("marcando", "jogos seguidos marcando gol", j => j.GolsPro > 0),
        ("sem_sofrer", "jogos seguidos sem sofrer gol", j => j.GolsContra == 0),
        ("sem_marcar", "jogos seguidos sem marcar", j => j.GolsPro == 0),
        ("sofrendo", "jogos seguidos sofrendo gol", j => j.GolsContra > 0),
    ];

    public async Task<ResultadoSequencias?> Handle(ObterSequencias q, CancellationToken ct)
    {
        var clubes = await db.Clubes.AsNoTracking().ToListAsync(ct);
        var pontuados = clubes.Select(c => (c, pontos: BuscaPorNome.Pontuar(BuscaPorNome.TraduzirApelido(q.Time), c.Nome))).Where(x => x.pontos > 0).ToList();
        if (pontuados.Count == 0) return null;
        var melhor = pontuados.Max(x => x.pontos);
        var ids = pontuados.Where(x => x.pontos == melhor).Select(x => x.c.Id).ToList();
        var nomes = clubes.ToDictionary(c => c.Id, c => c.Nome);

        // Período: o pedido; sem nada, a temporada (do ano pedido ou do atual)
        var temporada = q.Temporada ?? DateTime.UtcNow.Year;
        var de = q.De ?? new DateOnly(temporada, 1, 1);
        var ate = q.Ate ?? (q.Temporada is null ? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) : new DateOnly(temporada, 12, 31));
        var inicio = TimeZoneInfo.ConvertTimeToUtc(de.ToDateTime(TimeOnly.MinValue), ProcurarPartidasHandler.Brasilia);
        var fim = TimeZoneInfo.ConvertTimeToUtc(ate.AddDays(1).ToDateTime(TimeOnly.MinValue), ProcurarPartidasHandler.Brasilia);

        var consulta = db.Partidas.AsNoTracking()
            .Where(p => (ids.Contains(p.MandanteId) || ids.Contains(p.VisitanteId)) && p.Data >= inicio && p.Data < fim
                        && p.GolsMandante != null && p.GolsVisitante != null && (p.Status == "FT" || p.Status == "AET" || p.Status == "PEN"));
        if (q.CompeticaoId is { } c) consulta = consulta.Where(p => p.CompeticaoId == c);
        if (!string.IsNullOrWhiteSpace(q.Treinador))
        {
            var comEle = await ProcurarPartidasHandler.JogosDoTreinador(db, q.Treinador, ids, ct);
            consulta = consulta.Where(p => comEle.Contains(p.Id));
        }
        var partidas = await consulta.OrderBy(p => p.Data).ToListAsync(ct);
        var competicoes = await db.Competicoes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Nome, ct);

        var jogos = partidas.Select(p =>
        {
            var emCasa = ids.Contains(p.MandanteId);
            return new JogoDoTimeNaSequencia(p.Id, p.Data, nomes.GetValueOrDefault(emCasa ? p.MandanteId : p.VisitanteId, "?"),
                nomes.GetValueOrDefault(emCasa ? p.VisitanteId : p.MandanteId, "?"), emCasa,
                emCasa ? p.GolsMandante!.Value : p.GolsVisitante!.Value, emCasa ? p.GolsVisitante!.Value : p.GolsMandante!.Value,
                competicoes.GetValueOrDefault(p.CompeticaoId, ""));
        }).ToList();

        var time = nomes[ids[0]];
        var periodo = $"{de:dd/MM/yyyy} a {(q.Ate is null && q.Temporada is null ? "hoje" : ate.ToString("dd/MM/yyyy"))}"
                      + (q.CompeticaoId is { } cc ? $" ({competicoes.GetValueOrDefault(cc, "")})" : " (todas as competições)")
                      + (string.IsNullOrWhiteSpace(q.Treinador) ? "" : $", só jogos com {q.Treinador} no banco"
                         + (jogos.Count > 0 ? $" (estreia em {TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(jogos[0].Data, DateTimeKind.Utc), ProcurarPartidasHandler.Brasilia):dd/MM/yyyy})" : ""));
        return Calcular(time, periodo, jogos);
    }

    /// <summary>As contas, sem banco (para testar). `jogos` em ordem do mais antigo para o mais recente.</summary>
    public static ResultadoSequencias Calcular(string time, string periodo, List<JogoDoTimeNaSequencia> jogos)
    {
        var emCasa = jogos.Where(j => j.EmCasa).ToList();
        var fora = jogos.Where(j => !j.EmCasa).ToList();
        var vitorias = jogos.Where(j => j.GolsPro > j.GolsContra).OrderByDescending(j => j.GolsPro - j.GolsContra).ThenByDescending(j => j.GolsPro).FirstOrDefault();
        var derrotas = jogos.Where(j => j.GolsPro < j.GolsContra).OrderBy(j => j.GolsPro - j.GolsContra).ThenByDescending(j => j.GolsContra).FirstOrDefault();
        return new ResultadoSequencias(time, periodo, jogos.Count, Atuais(jogos), Atuais(emCasa), Atuais(fora),
            Tipos.Select(t => Recorde(jogos, t)).OfType<Sequencia>().ToList(),
            vitorias is null ? null : new Goleada(vitorias.GolsPro - vitorias.GolsContra, Citar(vitorias)),
            derrotas is null ? null : new Goleada(derrotas.GolsContra - derrotas.GolsPro, Citar(derrotas)));
    }

    static List<Sequencia> Atuais(List<JogoDoTimeNaSequencia> jogos)
    {
        var r = new List<Sequencia>();
        foreach (var (tipo, descricao, vale) in Tipos)
        {
            var n = 0;
            for (var i = jogos.Count - 1; i >= 0 && vale(jogos[i]); i--) n++;
            // Todas, mesmo com 0 ou 1 jogo: sem a contagem explícita, a IA "completava" a que faltava (ex.: invicto em casa)
            var antes = jogos.Count > n ? jogos[^(n + 1)] : null;
            r.Add(new Sequencia(tipo, descricao, n, n > 0 ? Citar(jogos[^n]) : null, n > 0 ? Citar(jogos[^1]) : null, antes is null ? null : Citar(antes)));
        }
        return r.OrderByDescending(s => s.Jogos).ToList();
    }

    static Sequencia? Recorde(List<JogoDoTimeNaSequencia> jogos, (string Tipo, string Descricao, Func<JogoDoTimeNaSequencia, bool> Vale) t)
    {
        int melhor = 0, fimMelhor = -1, atual = 0;
        for (var i = 0; i < jogos.Count; i++)
        {
            atual = t.Vale(jogos[i]) ? atual + 1 : 0;
            if (atual > melhor) { melhor = atual; fimMelhor = i; } // empate fica com a primeira vez
        }
        if (melhor < 2) return null;
        var inicio = fimMelhor - melhor + 1;
        return new Sequencia(t.Tipo, t.Descricao, melhor, Citar(jogos[inicio]), Citar(jogos[fimMelhor]),
            fimMelhor + 1 < jogos.Count ? Citar(jogos[fimMelhor + 1]) : null);
    }

    static JogoDaSequencia Citar(JogoDoTimeNaSequencia j)
    {
        var data = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(j.Data, DateTimeKind.Utc), ProcurarPartidasHandler.Brasilia));
        var jogo = j.EmCasa ? $"{j.Time} {j.GolsPro} x {j.GolsContra} {j.Adversario}" : $"{j.Adversario} {j.GolsContra} x {j.GolsPro} {j.Time}";
        var hoje = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ProcurarPartidasHandler.Brasilia);
        return new JogoDaSequencia(j.Id, data, jogo, j.Competicao, j.EmCasa, ProcurarPartidasHandler.Relativo(data.ToDateTime(TimeOnly.MinValue), hoje));
    }
}
