using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

public record ResumoPartida(int Id, DateTime Data, string? Rodada, int CompeticaoId, string Competicao, int Temporada, string Status,
    int MandanteId, string Mandante, string? EscudoMandante, int VisitanteId, string Visitante, string? EscudoVisitante,
    int? GolsMandante, int? GolsVisitante);

/// <summary>`X`/`Y` (0–100): lugar no desenho tático, como em <see cref="FuncaoTatica.Coordenadas"/>; só para titulares.</summary>
public record JogadorNaPartida(int? Id, string Nome, string? Funcao, double? X = null, double? Y = null);

/// <summary>
/// Números de um atleta numa partida, como o provedor informa (/fixtures/players). Uso interno: viram
/// percentis e a nossa nota do jogo, nunca aparecem crus.
/// </summary>
public record EstatisticaDoJogador(int AtletaId, int ClubeId, int Minutos, string? Posicao,
    int Gols, int Assistencias, int GolsSofridos, int Defesas, int Finalizacoes, int FinalizacoesNoAlvo,
    int Passes, int PassesDecisivos, int? PassesCertos, int Desarmes, int Bloqueios, int Interceptacoes,
    int Duelos, int DuelosGanhos, int DriblesTentados, int DriblesCertos, int FaltasCometidas, int FaltasSofridas,
    int Amarelos, int Vermelhos);

/// <summary>Um indicador do atleta no jogo: percentil do rendimento por 90 min contra o setor na temporada.</summary>
public record IndicadorNoJogo(string Chave, string Nome, double? Percentil, bool DoSetor);

/// <summary>
/// Como o atleta foi no jogo: a nossa nota (média dos percentis dos indicadores do setor) e os fatos públicos
/// (minutos, gols, assistências, cartões). `Nota` nula quando jogou pouco para medir.
/// </summary>
public record DesempenhoNoJogo(int AtletaId, string Nome, int ClubeId, string? Funcao, string Setor, int Minutos, double? Nota,
    int Gols, int Assistencias, int Amarelos, int Vermelhos, IReadOnlyList<IndicadorNoJogo> Indicadores);

public record LadoDaPartida(int ClubeId, string Time, string? Esquema, int? TreinadorId, string? Treinador,
    IReadOnlyList<JogadorNaPartida> Titulares, IReadOnlyList<JogadorNaPartida> Reservas);

/// <summary>Gol, cartão, substituição ou VAR. `Outro` = quem deu a assistência ou quem entrou.</summary>
public record EventoDaPartida(int Minuto, int? Acrescimo, int ClubeId, string Tipo, string? Jogador, string? Outro, string? Detalhe,
    int? JogadorId = null, int? OutroId = null);

/// <summary>Escalações completas e eventos de uma partida, como o provedor informa (buscados sob demanda).</summary>
public record DetalhesDoProvedor(IReadOnlyList<LadoDaPartida> Lados, IReadOnlyList<EventoDaPartida> Eventos,
    IReadOnlyList<EstatisticaDoJogador>? Jogadores = null);

public interface IDetalhesDaPartida
{
    Task<DetalhesDoProvedor?> Obter(int partidaId, bool encerrada, CancellationToken ct);
}

/// <summary>
/// Um aspecto do jogo: quem levou vantagem e como cada time foi em relação aos próprios jogos na temporada
/// ("finalizou mais que em 85% dos jogos dele"). Os números brutos do provedor não aparecem.
/// </summary>
public record AspectoDaPartida(string Aspecto, string? Vantagem, double? PercentilMandante, double? PercentilVisitante);

/// <summary>
/// Confronto direto antes deste jogo, só com os jogos que estão na base (todas as competições importadas).
/// Vitórias contadas pelo placar do jogo: decisão por pênaltis entra como empate.
/// </summary>
public record ConfrontoDireto(int VitoriasMandante, int Empates, int VitoriasVisitante, int? DesdeAno, IReadOnlyList<ResumoPartida> Ultimos);

/// <summary>`Desfecho`: "Decidido nos pênaltis: 4 x 3" ou "Decidido na prorrogação"; nulo no tempo normal.</summary>
public record AnalisePartida(ResumoPartida Partida, LadoDaPartida Mandante, LadoDaPartida Visitante,
    IReadOnlyList<EventoDaPartida> Eventos, IReadOnlyList<AspectoDaPartida> Aspectos, IReadOnlyList<string> Observacoes, string? Desfecho = null,
    IReadOnlyList<DesempenhoNoJogo>? Desempenhos = null, ConfrontoDireto? Confronto = null, IReadOnlyList<ResumoPartida>? JogosSeguintes = null);

// ---------- Procurar partidas ----------

/// <summary>"Galo x Santos no sábado": partidas de um time, com o adversário e o período opcionais, da mais recente para a mais antiga.</summary>
public record ProcurarPartidas(string Time, string? Adversario = null, DateOnly? De = null, DateOnly? Ate = null, int Limite = 8, int? CompeticaoId = null) : IQuery<IReadOnlyList<ResumoPartida>>;

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
        if (q.CompeticaoId is { } competicao) consulta = consulta.Where(p => p.CompeticaoId == competicao);
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

public class AnalisarPartidaHandler(IScoutDb db, IDetalhesDaPartida detalhes, MotorDeAnalise motor) : IQueryHandler<AnalisarPartida, AnalisePartida?>
{
    record Medida(string Aspecto, Func<EstatisticaPartida, double?> Valor, bool Inverso = false, double Margem = 0.1);

    // Margem: diferença mínima (relativa) para dizer que um time levou vantagem
    static readonly Medida[] Medidas =
    [
        new("Posse de bola", e => e.Posse, Margem: 0.08),
        new("Volume de finalizações", e => e.Finalizacoes),
        new("Chances criadas (xG)", e => e.Xg, Margem: 0.15),
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
            .Concat(provedor?.Eventos.SelectMany(e => new[] { e.JogadorId, e.OutroId }) ?? [])
            .Concat((provedor?.Jogadores ?? []).Select(j => (int?)j.AtletaId)).OfType<int>().Distinct().ToList();
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

        string? desfecho = p.Status switch
        {
            "PEN" => $"Decidido nos pênaltis: {resumo.Mandante} {Convertidos(p.MandanteId)} x {Convertidos(p.VisitanteId)} {resumo.Visitante}",
            "AET" => "Decidido na prorrogação",
            _ => null,
        };
        int Convertidos(int clubeId) => eventos.Count(e => e.ClubeId == clubeId && e.Tipo == "Pênalti convertido na disputa");
        if (p.Status == "PEN") observacoes.Add("Placar do jogo (tempo normal e prorrogação); a disputa de pênaltis aparece à parte.");
        var mandante = Lado(p.MandanteId, resumo.Mandante);
        var visitante = Lado(p.VisitanteId, resumo.Visitante);
        // Nota do jogo só para quem tem data de nascimento e 18 anos ou mais (mesma regra das análises individuais)
        bool PodeAnalisar(int id) => nascimentos.TryGetValue(id, out var n) && new Atleta { Nascimento = n }.IdadeEm(dataJogo) >= Atleta.IdadeMinimaAnalise;
        var desempenhos = await Desempenhos(p, provedor?.Jogadores ?? [], [mandante, visitante], PodeAnalisar, ct);
        if (desempenhos.Count > 0)
            observacoes.Add($"Nota do jogo (0 a 100) = média dos percentis do atleta nos indicadores do setor, com o rendimento do jogo levado a 90 minutos e comparado ao dos atletas do mesmo setor na temporada. Gol ou assistência de defensor e meio-campista também entra na nota. Só recebe nota quem jogou {MinutosParaNota} minutos ou mais.");
        var (confronto, seguintes) = await ConfrontoESeguintes(p, ct);
        return new AnalisePartida(resumo, mandante, visitante, eventos, aspectos, observacoes, desfecho, desempenhos, confronto, seguintes);
    }

    /// <summary>Jogos anteriores entre os dois times e o jogo seguinte de cada um depois desta partida.</summary>
    async Task<(ConfrontoDireto?, List<ResumoPartida>)> ConfrontoESeguintes(Partida p, CancellationToken ct)
    {
        var (a, b) = (p.MandanteId, p.VisitanteId);
        var anteriores = (await db.Partidas.AsNoTracking()
                .Where(x => x.Id != p.Id && x.Data < p.Data && x.GolsMandante != null
                            && ((x.MandanteId == a && x.VisitanteId == b) || (x.MandanteId == b && x.VisitanteId == a)))
                .OrderByDescending(x => x.Data).ToListAsync(ct))
            .Where(x => x.Encerrada).ToList();
        var seguintes = new List<Partida>();
        foreach (var clube in new[] { a, b })
            if (await db.Partidas.AsNoTracking().Where(x => x.Data > p.Data && (x.MandanteId == clube || x.VisitanteId == clube))
                    .OrderBy(x => x.Data).FirstOrDefaultAsync(ct) is { } prox && seguintes.All(s => s.Id != prox.Id))
                seguintes.Add(prox);
        var resumoSeguintes = await ProcurarPartidasHandler.Resumir(db, seguintes, ct);
        if (anteriores.Count == 0) return (null, resumoSeguintes);
        return (Contar(a, anteriores, await ProcurarPartidasHandler.Resumir(db, anteriores.Take(5).ToList(), ct)), resumoSeguintes);
    }

    /// <summary>V/E/D do ponto de vista do mandante deste jogo (sem banco, para testar).</summary>
    public static ConfrontoDireto Contar(int mandanteId, List<Partida> anteriores, IReadOnlyList<ResumoPartida> ultimos)
    {
        int GolsDe(Partida x, bool doMandante) => (x.MandanteId == mandanteId) == doMandante ? x.GolsMandante!.Value : x.GolsVisitante!.Value;
        var (v, e, d) = (0, 0, 0);
        foreach (var x in anteriores)
        {
            var (pro, contra) = (GolsDe(x, true), GolsDe(x, false));
            if (pro > contra) v++; else if (pro == contra) e++; else d++;
        }
        return new ConfrontoDireto(v, e, d, anteriores.Count == 0 ? null : anteriores.Min(x => x.Temporada), ultimos);
    }

    public const int MinutosParaNota = 30;

    static readonly Dictionary<string, Setor> SetorDaPosicao = new() { ["G"] = Setor.Goleiro, ["D"] = Setor.Defesa, ["M"] = Setor.Meio, ["F"] = Setor.Ataque };

    /// <summary>
    /// Rendimento de cada atleta no jogo: os números do provedor viram indicadores por 90 min (os defensivos ajustados à
    /// posse do time naquele jogo) e cada um vira percentil contra os atletas do mesmo setor na temporada.
    /// </summary>
    async Task<List<DesempenhoNoJogo>> Desempenhos(Partida p, IReadOnlyList<EstatisticaDoJogador> jogadores, LadoDaPartida[] lados,
        Func<int, bool> podeAnalisar, CancellationToken ct)
    {
        if (jogadores.Count == 0) return [];
        var temporada = await motor.Analisar(p.CompeticaoId, p.Temporada, ct);
        var linhas = temporada?.Linhas ?? [];
        var daTemporada = linhas.ToDictionary(l => l.AtletaId);
        var porSetor = linhas.GroupBy(l => l.Setor).ToDictionary(g => g.Key, g => g.ToList());
        var posse = await db.EstatisticasPartida.AsNoTracking().Where(e => e.PartidaId == p.Id && e.Posse != null)
            .ToDictionaryAsync(e => e.ClubeId, e => (double)e.Posse!.Value, ct);
        var noJogo = lados.SelectMany(l => l.Titulares.Concat(l.Reservas)).Where(j => j.Id is not null)
            .GroupBy(j => j.Id!.Value).ToDictionary(g => g.Key, g => g.First());
        var indicadores = Catalogo.Indicadores.Where(i => i.Chave is not "altura" and not "participacaoMinutos").ToList();

        var lista = new List<DesempenhoNoJogo>();
        foreach (var j in jogadores)
        {
            if (!podeAnalisar(j.AtletaId)) continue;
            daTemporada.TryGetValue(j.AtletaId, out var naTemporada);
            Setor? setor = naTemporada?.Setor ?? (SetorDaPosicao.TryGetValue(j.Posicao ?? "", out var s) ? s : null);
            if (setor is null) continue;
            var linha = new LinhaAnalise
            {
                AtletaId = j.AtletaId, Setor = setor.Value, Minutos = j.Minutos,
                Gols = j.Gols, Assistencias = j.Assistencias, GolsSofridos = j.GolsSofridos, Defesas = j.Defesas,
                Finalizacoes = j.Finalizacoes, FinalizacoesNoAlvo = j.FinalizacoesNoAlvo, Passes = j.Passes, PassesDecisivos = j.PassesDecisivos,
                PrecisaoPasses = j.PassesCertos is { } c && j.Passes >= 10 ? c * 100.0 / j.Passes : null,
                Desarmes = j.Desarmes, Bloqueios = j.Bloqueios, Interceptacoes = j.Interceptacoes,
                Duelos = j.Duelos, DuelosGanhos = j.DuelosGanhos, DriblesTentados = j.DriblesTentados, DriblesCertos = j.DriblesCertos,
                FaltasCometidas = j.FaltasCometidas, FaltasSofridas = j.FaltasSofridas, Amarelos = j.Amarelos, Vermelhos = j.Vermelhos,
                PosseDoTime = posse.TryGetValue(j.ClubeId, out var ps) ? ps : null,
            };
            var doSetor = Catalogo.IndicadoresDoSetor(setor.Value).ToHashSet();
            // Defensor ou meio-campista que fez gol ou deu assistência: a participação no gol entra na nota do jogo
            // (só soma quando acontece; não pesa contra quem não marcou)
            if (setor is Setor.Defesa or Setor.Meio)
            {
                if (j.Gols > 0) doSetor.Add("gols90");
                if (j.Assistencias > 0) doSetor.Add("assistencias90");
            }
            var grupo = porSetor.GetValueOrDefault(setor.Value) ?? [];
            var lidos = new List<IndicadorNoJogo>();
            foreach (var ind in indicadores)
            {
                var valores = grupo.Select(l => l.Valores.GetValueOrDefault(ind.Chave)).OfType<double>().ToList();
                // Com menos de 10 atletas no setor o percentil não diz nada
                var pct = valores.Count < 10 ? null : PercentilDoJogo(ind.Chave, valores, ind.Valor(linha), linha);
                if (pct is null) continue;
                lidos.Add(new IndicadorNoJogo(ind.Chave, ind.Nome, Math.Round(ind.Inverso ? 100 - pct.Value : pct.Value), doSetor.Contains(ind.Chave)));
            }
            double? nota = j.Minutos >= MinutosParaNota ? MotorDeAnalise.Media(lidos.Where(i => i.DoSetor).Select(i => i.Percentil)) : null;
            noJogo.TryGetValue(j.AtletaId, out var naEscalacao);
            lista.Add(new DesempenhoNoJogo(j.AtletaId, naEscalacao?.Nome ?? naTemporada?.Nome ?? "?", j.ClubeId,
                naEscalacao?.Funcao ?? naTemporada?.Funcao, setor.Value.ToString(), j.Minutos, nota is null ? null : Math.Round(nota.Value),
                j.Gols, j.Assistencias, j.Amarelos, j.Vermelhos, lidos.OrderByDescending(i => i.DoSetor).ToList()));
        }
        return lista.OrderByDescending(d => d.Nota ?? -1).ToList();
    }

    /// <summary>
    /// Percentil de um jogo contra o setor na temporada. Um jogo varia muito mais que a média de uma temporada
    /// (num jogo de 30 minutos, um desarme a mais dobra o "por 90"), então a comparação direta com as médias
    /// jogaria quase todo mundo em 0 ou 100. A distribuição de referência soma a variação entre atletas (das
    /// médias da temporada) à variação de um jogo daquela duração: Poisson nos indicadores de contagem por 90 min
    /// e binomial nas porcentagens. O percentil sai da curva normal, entre 1 e 99.
    /// </summary>
    public static double? PercentilDoJogo(string chave, IReadOnlyList<double> temporada, double? valor, LinhaAnalise jogo)
    {
        if (valor is null || temporada.Count < 2 || jogo.Minutos <= 0) return null;
        var media = temporada.Average();
        var entreAtletas = temporada.Sum(v => (v - media) * (v - media)) / (temporada.Count - 1);
        double? tentativas = chave switch
        {
            "precisaoPasses" => jogo.Passes,
            "aproveitamentoFinalizacoes" => jogo.Finalizacoes,
            "aproveitamentoDuelos" => jogo.Duelos,
            "aproveitamentoDribles" => jogo.DriblesTentados,
            _ => null,
        };
        double doJogo = tentativas is { } n
            ? (n > 0 ? Math.Clamp(media, 1, 99) * (100 - Math.Clamp(media, 1, 99)) / n : double.PositiveInfinity)
            : Math.Max(media, 0.05) * 90.0 / jogo.Minutos;
        var desvio = Math.Sqrt(entreAtletas + doJogo);
        if (desvio <= 0 || double.IsInfinity(desvio)) return null;
        return Math.Clamp(100 * Normal((valor.Value - media) / desvio), 1, 99);
    }

    /// <summary>Função de distribuição da normal padrão (aproximação de Abramowitz e Stegun, erro &lt; 1e-7).</summary>
    static double Normal(double z)
    {
        var t = 1 / (1 + 0.2316419 * Math.Abs(z));
        var d = 0.3989422804014327 * Math.Exp(-z * z / 2);
        var cauda = d * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return z >= 0 ? 1 - cauda : cauda;
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
