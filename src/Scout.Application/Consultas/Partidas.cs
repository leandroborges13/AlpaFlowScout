using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

public record ResumoPartida(int Id, DateTime Data, string? Rodada, int CompeticaoId, string Competicao, int Temporada, string Status,
    int MandanteId, string Mandante, string? EscudoMandante, int VisitanteId, string Visitante, string? EscudoVisitante,
    int? GolsMandante, int? GolsVisitante);

public record JogadorNaPartida(int? Id, string Nome, string? Funcao);

public record LadoDaPartida(int ClubeId, string Time, string? Esquema, int? TreinadorId, string? Treinador,
    IReadOnlyList<JogadorNaPartida> Titulares, IReadOnlyList<JogadorNaPartida> Reservas);

/// <summary>Gol, cartão, substituição ou VAR. `Outro` = quem deu a assistência ou quem entrou.</summary>
public record EventoDaPartida(int Minuto, int? Acrescimo, int ClubeId, string Tipo, string? Jogador, string? Outro, string? Detalhe,
    int? JogadorId = null, int? OutroId = null);

/// <summary>Escalações completas e eventos de uma partida, como o provedor informa (buscados sob demanda).</summary>
public record DetalhesDoProvedor(IReadOnlyList<LadoDaPartida> Lados, IReadOnlyList<EventoDaPartida> Eventos);

public interface IDetalhesDaPartida
{
    Task<DetalhesDoProvedor?> Obter(int partidaId, bool encerrada, CancellationToken ct);
}

/// <summary>
/// Um aspecto do jogo: quem levou vantagem e como cada time foi em relação aos próprios jogos na temporada
/// ("finalizou mais que em 85% dos jogos dele"). Os números brutos do provedor não aparecem.
/// </summary>
public record AspectoDaPartida(string Aspecto, string? Vantagem, double? PercentilMandante, double? PercentilVisitante);

public record AnalisePartida(ResumoPartida Partida, LadoDaPartida Mandante, LadoDaPartida Visitante,
    IReadOnlyList<EventoDaPartida> Eventos, IReadOnlyList<AspectoDaPartida> Aspectos, IReadOnlyList<string> Observacoes);

// ---------- Procurar partidas ----------

/// <summary>"Galo x Santos no sábado": partidas de um time, com o adversário e o período opcionais, da mais recente para a mais antiga.</summary>
public record ProcurarPartidas(string Time, string? Adversario = null, DateOnly? De = null, DateOnly? Ate = null, int Limite = 8) : IQuery<IReadOnlyList<ResumoPartida>>;

public class ProcurarPartidasHandler(IScoutDb db) : IQueryHandler<ProcurarPartidas, IReadOnlyList<ResumoPartida>>
{
    /// <summary>O banco guarda UTC; datas de jogo são lidas e mostradas no horário de Brasília (jogo das 21h30 de sábado é sábado).</summary>
    public static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    static DateTime InicioDoDiaEmUtc(DateOnly dia) => TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(TimeOnly.MinValue), Brasilia);

    public async Task<IReadOnlyList<ResumoPartida>> Handle(ProcurarPartidas q, CancellationToken ct)
    {
        var clubes = await db.Clubes.AsNoTracking().ToListAsync(ct);
        // Só os clubes com a melhor pontuação: "Atlético MG" não pode trazer o Atlético-GO junto
        List<int> Ids(string busca)
        {
            var pontuados = clubes.Select(c => (c.Id, pontos: BuscaPorNome.Pontuar(BuscaPorNome.TraduzirApelido(busca), c.Nome))).Where(x => x.pontos > 0).ToList();
            var melhor = pontuados.Select(x => x.pontos).DefaultIfEmpty(0).Max();
            return pontuados.Where(x => x.pontos == melhor).Select(x => x.Id).ToList();
        }
        var times = Ids(q.Time);
        if (times.Count == 0) return [];
        var adversarios = q.Adversario is null ? null : Ids(q.Adversario);

        var consulta = db.Partidas.AsNoTracking().Where(p => times.Contains(p.MandanteId) || times.Contains(p.VisitanteId));
        if (adversarios is not null) consulta = consulta.Where(p => adversarios.Contains(p.MandanteId) || adversarios.Contains(p.VisitanteId));
        if (q.De is { } de) { var inicio = InicioDoDiaEmUtc(de); consulta = consulta.Where(p => p.Data >= inicio); }
        if (q.Ate is { } ate) { var fim = InicioDoDiaEmUtc(ate.AddDays(1)); consulta = consulta.Where(p => p.Data < fim); }
        var partidas = await consulta.Where(p => p.Data <= DateTime.UtcNow.AddDays(1)).OrderByDescending(p => p.Data).Take(Math.Clamp(q.Limite, 1, 20)).ToListAsync(ct);
        return await Resumir(db, partidas, ct);
    }

    public static async Task<List<ResumoPartida>> Resumir(IScoutDb db, List<Partida> partidas, CancellationToken ct)
    {
        var ids = partidas.SelectMany(p => new[] { p.MandanteId, p.VisitanteId }).Distinct().ToList();
        var clubes = await db.Clubes.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var competicoes = await db.Competicoes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        return partidas.Select(p => new ResumoPartida(p.Id, TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(p.Data, DateTimeKind.Utc), Brasilia), p.Rodada, p.CompeticaoId, competicoes.GetValueOrDefault(p.CompeticaoId, ""), p.Temporada, p.Status,
            p.MandanteId, clubes.GetValueOrDefault(p.MandanteId)?.Nome ?? "?", clubes.GetValueOrDefault(p.MandanteId)?.Escudo,
            p.VisitanteId, clubes.GetValueOrDefault(p.VisitanteId)?.Nome ?? "?", clubes.GetValueOrDefault(p.VisitanteId)?.Escudo,
            p.GolsMandante, p.GolsVisitante)).ToList();
    }
}

// ---------- Analisar uma partida ----------

public record AnalisarPartida(int PartidaId) : IQuery<AnalisePartida?>;

public class AnalisarPartidaHandler(IScoutDb db, IDetalhesDaPartida detalhes) : IQueryHandler<AnalisarPartida, AnalisePartida?>
{
    record Medida(string Aspecto, Func<EstatisticaPartida, double?> Valor, bool Inverso = false, double Margem = 0.1);

    // Margem: diferença mínima (relativa) para dizer que um time levou vantagem
    static readonly Medida[] Medidas =
    [
        new("Posse de bola", e => e.Posse, Margem: 0.08),
        new("Volume de finalizações", e => e.Finalizacoes),
        new("Finalizações no alvo", e => e.FinalizacoesNoAlvo),
        new("Finalizações de dentro da área", e => e.FinalizacoesNaArea),
        new("Escanteios", e => e.Escanteios),
        new("Precisão de passe", e => e.Passes > 0 && e.PassesCertos is not null ? e.PassesCertos * 100.0 / e.Passes : null, Margem: 0.03),
        new("Faltas cometidas", e => e.Faltas, Inverso: true),
    ];

    public async Task<AnalisePartida?> Handle(AnalisarPartida q, CancellationToken ct)
    {
        var p = await db.Partidas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == q.PartidaId, ct);
        if (p is null) return null;
        var resumo = (await ProcurarPartidasHandler.Resumir(db, [p], ct))[0];
        var observacoes = new List<string>();

        var provedor = await detalhes.Obter(p.Id, p.Encerrada, ct);
        if (provedor is null) observacoes.Add("Escalações e eventos indisponíveis no provedor para este jogo.");

        // Menores de 18 anos não aparecem pelo nome (regra de proteção da plataforma)
        var idsJogadores = (provedor?.Lados.SelectMany(l => l.Titulares.Concat(l.Reservas)).Select(j => j.Id) ?? [])
            .Concat(provedor?.Eventos.SelectMany(e => new[] { e.JogadorId, e.OutroId }) ?? []).OfType<int>().Distinct().ToList();
        var nascimentos = await db.Atletas.AsNoTracking().Where(a => idsJogadores.Contains(a.Id) && a.Nascimento != null)
            .ToDictionaryAsync(a => a.Id, a => a.Nascimento!.Value, ct);
        var dataJogo = DateOnly.FromDateTime(resumo.Data);
        bool Menor(int? id) => id is { } i && nascimentos.TryGetValue(i, out var n) && new Atleta { Nascimento = n }.IdadeEm(dataJogo) < Atleta.IdadeMinimaAnalise;
        JogadorNaPartida Proteger(JogadorNaPartida j) => Menor(j.Id) ? j with { Id = null, Nome = "Atleta sub-18" } : j;
        LadoDaPartida Lado(int clubeId, string nome) =>
            provedor?.Lados.FirstOrDefault(l => l.ClubeId == clubeId) is { } l
                ? l with { Time = nome, Titulares = l.Titulares.Select(Proteger).ToList(), Reservas = l.Reservas.Select(Proteger).ToList() }
                : new LadoDaPartida(clubeId, nome, null, null, null, [], []);
        var eventos = (provedor?.Eventos ?? []).Select(e => e with
        {
            Jogador = Menor(e.JogadorId) ? "Atleta sub-18" : e.Jogador,
            Outro = Menor(e.OutroId) ? "Atleta sub-18" : e.Outro,
        }).ToList();

        var aspectos = await Aspectos(p, resumo.Mandante, resumo.Visitante, ct);
        if (aspectos.Count == 0) observacoes.Add("Sem estatísticas deste jogo no provedor: a comparação de desempenho fica de fora.");
        else observacoes.Add("Percentil = como o time foi neste jogo em relação aos próprios jogos na temporada (100 = o melhor jogo dele naquele aspecto).");

        return new AnalisePartida(resumo, Lado(p.MandanteId, resumo.Mandante), Lado(p.VisitanteId, resumo.Visitante), eventos, aspectos, observacoes);
    }

    async Task<List<AspectoDaPartida>> Aspectos(Partida p, string mandante, string visitante, CancellationToken ct)
    {
        var doJogo = await db.EstatisticasPartida.AsNoTracking().Where(e => e.PartidaId == p.Id).ToListAsync(ct);
        var m = doJogo.FirstOrDefault(e => e.ClubeId == p.MandanteId);
        var v = doJogo.FirstOrDefault(e => e.ClubeId == p.VisitanteId);
        if (m is null || v is null) return [];

        // Jogos de cada time na mesma competição e temporada, para o percentil "em relação ao normal dele"
        var temporada = await (from e in db.EstatisticasPartida.AsNoTracking()
                               join x in db.Partidas.AsNoTracking() on e.PartidaId equals x.Id
                               where x.CompeticaoId == p.CompeticaoId && x.Temporada == p.Temporada
                                     && (e.ClubeId == p.MandanteId || e.ClubeId == p.VisitanteId)
                               select e).ToListAsync(ct);

        double? PercentilNoTime(Medida med, EstatisticaPartida e)
        {
            var valores = temporada.Where(x => x.ClubeId == e.ClubeId).Select(med.Valor).OfType<double>().ToList();
            if (valores.Count < CatalogoTimes.JogosMinimosComEstatistica) return null;
            var pct = MotorDeAnalise.Percentil(valores, med.Valor(e));
            return pct is null ? null : Math.Round(med.Inverso ? 100 - pct.Value : pct.Value);
        }

        var lista = new List<AspectoDaPartida>();
        foreach (var med in Medidas)
        {
            if (med.Valor(m) is not { } vm || med.Valor(v) is not { } vv) continue;
            var maior = Math.Max(vm, vv);
            string? vantagem = maior == 0 || Math.Abs(vm - vv) / maior < med.Margem ? "Equilibrado"
                : (vm > vv) != med.Inverso ? mandante : visitante;
            lista.Add(new AspectoDaPartida(med.Aspecto, vantagem, PercentilNoTime(med, m), PercentilNoTime(med, v)));
        }
        return lista;
    }
}
