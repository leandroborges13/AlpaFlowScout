using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

/// <summary>
/// O que a plataforma mostra de um time: fatos públicos (resultados, gols, esquemas) e as nossas
/// métricas (percentis entre os times da competição). Nunca as médias brutas do provedor.
/// </summary>
public record ResumoTime(
    int Id, string Nome, string? Escudo, int CompeticaoId, string Competicao, int Temporada,
    int Posicao, int Jogos, int Vitorias, int Empates, int Derrotas, int GolsPro, int GolsContra, int Pontos, int? Aproveitamento,
    string? EsquemaMaisUsado, string? Estilo, IReadOnlyList<Destaque> Dimensoes, IReadOnlyList<string> Carencias);

public record EsquemaDoTime(string Esquema, int Jogos, int EmCasa, int Fora, int? Aproveitamento);

public record EstiloTime(string Rotulo, double? PercentilPosse, string Explicacao);

public record ElencoTime(double? IdadeMedia, int? PercentualMinutosAte23, int AtletasUsados, string? Rodizio, string Explicacao);

public record TitularNaFuncao(int Id, string Nome, int Jogos, double? Nota);

/// <summary>"Lateral esquerdo: titulares com nota 41, abaixo de 80% dos times da Série B" → carência.</summary>
public record NecessidadeNaFuncao(string Funcao, string Setor, int Jogos, int PorJogo, double? NotaTitulares, double? Percentil, bool Carencia,
    IReadOnlyList<TitularNaFuncao> Titulares);

public record DetalheTime(ResumoTime Resumo, IReadOnlyList<EsquemaDoTime> Esquemas, IReadOnlyList<Destaque> Dimensoes,
    IReadOnlyList<PercentilIndicador> Indicadores, EstiloTime? Estilo, ElencoTime Elenco,
    IReadOnlyList<NecessidadeNaFuncao> Funcoes, IReadOnlyList<ResumoAtleta> Principais, IReadOnlyList<ResumoTreinador> Treinadores,
    IReadOnlyList<string> Observacoes);

public static class ResumirTime
{
    /// <summary>Abaixo deste percentil, a função é uma carência do time.</summary>
    public const double LimiteCarencia = 35;

    public static ResumoTime De(LinhaTime t) => new(
        t.ClubeId, t.Nome, t.Escudo, t.CompeticaoId, t.Competicao, t.Temporada,
        t.Posicao, t.Jogos, t.Vitorias, t.Empates, t.Derrotas, t.GolsPro, t.GolsContra, t.Pontos,
        t.Aproveitamento is { } a ? (int)Math.Round(a) : null,
        t.EsquemasPorJogo.GroupBy(e => e.Esquema).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
        Estilo(t)?.Rotulo, Dimensoes(t),
        Necessidades(t).Where(n => n.Carencia).OrderBy(n => n.Percentil).Take(2).Select(n => n.Funcao).ToList());

    public static List<Destaque> Dimensoes(LinhaTime t) => CatalogoTimes.Dimensoes
        .Select(d => (d, nota: CatalogoTimes.NotaDaDimensao(t, d)))
        .Where(x => x.nota is not null)
        .Select(x => new Destaque(x.d.Nome, Math.Round(x.nota!.Value)))
        .ToList();

    public static EstiloTime? Estilo(LinhaTime t)
    {
        if (t.Percentis.GetValueOrDefault("posse") is not { } posse) return null;
        var rotulo = posse >= 65 ? "Propositivo" : posse <= 35 ? "Reativo" : "Equilibrado na posse";
        var ataque = CatalogoTimes.NotaDaDimensao(t, CatalogoTimes.Dimensoes[1]);
        var defesa = CatalogoTimes.NotaDaDimensao(t, CatalogoTimes.Dimensoes[2]);
        var pendulo = ataque - defesa;
        if (pendulo >= 20) rotulo += ", mais forte atacando";
        else if (pendulo <= -20) rotulo += ", mais forte defendendo";
        var explicacao = posse >= 65 ? "Gosta de ter a bola e construir com passes."
            : posse <= 35 ? "Joga mais sem a bola, apostando em transição e bola longa."
            : "Divide a posse com os adversários.";
        return new EstiloTime(rotulo, Math.Round(posse),
            $"{explicacao} Posse de bola maior que a de {Math.Round(posse)}% dos times da competição.");
    }

    public static List<NecessidadeNaFuncao> Necessidades(LinhaTime t) => t.Funcoes
        .Select(f => new NecessidadeNaFuncao(f.Funcao, Resumir.NomeSetor(f.Setor), f.Jogos, f.PorJogo,
            f.NotaTitulares is { } n ? Math.Round(n) : null,
            f.PercentilNaCompeticao is { } p ? Math.Round(p) : null,
            f.PercentilNaCompeticao <= LimiteCarencia,
            f.Atletas.Take(4).Select(a => new TitularNaFuncao(a.AtletaId, a.Nome, a.Jogos, a.Nota is { } x ? Math.Round(x) : null)).ToList()))
        .ToList();
}

// ---------- Times de uma competição ----------

public record ListarTimes(int? CompeticaoId = null, int? Temporada = null, string? Nome = null) : IQuery<IReadOnlyList<ResumoTime>>;

public class ListarTimesHandler(IScoutDb db, AnaliseDeTimes analise) : IQueryHandler<ListarTimes, IReadOnlyList<ResumoTime>>
{
    public async Task<IReadOnlyList<ResumoTime>> Handle(ListarTimes q, CancellationToken ct)
    {
        var times = await CarregarTimes(db, analise, q.Temporada, q.CompeticaoId is { } c ? [c] : null, ct);
        var ordenados = q.Nome is null
            ? times.OrderBy(t => t.CompeticaoId).ThenBy(t => t.Posicao)
            : BuscaPorNome.Filtrar(times, BuscaPorNome.TraduzirApelido(q.Nome), t => t.Nome); // "Atlético MG" acha "Atletico-MG"
        return ordenados
            .Select(ResumirTime.De)
            .ToList();
    }

    /// <summary>Times das competições pedidas (ou de todas), na temporada mais recente com dados de cada uma.</summary>
    public static async Task<List<LinhaTime>> CarregarTimes(IScoutDb db, AnaliseDeTimes analise, int? temporada, int[]? competicoes, CancellationToken ct) =>
        (await CarregarAnalises(db, analise, temporada, competicoes, ct)).SelectMany(a => a.Times).ToList();

    public static async Task<List<AnaliseTimesTemporada>> CarregarAnalises(IScoutDb db, AnaliseDeTimes analise, int? temporada, int[]? competicoes, CancellationToken ct)
    {
        var analises = new List<AnaliseTimesTemporada>();
        foreach (var (competicao, ano) in await BuscarPorPerfilHandler.CompeticoesAlvo(db, temporada, competicoes, ct))
            if (await analise.Analisar(competicao, ano, ct) is { } r) analises.Add(r);
        return analises;
    }
}

// ---------- Detalhe do time ----------

public record ObterTime(int ClubeId, int? CompeticaoId = null, int? Temporada = null) : IQuery<DetalheTime?>;

public class ObterTimeHandler(IScoutDb db, AnaliseDeTimes analise, MotorDeAnalise motor) : IQueryHandler<ObterTime, DetalheTime?>
{
    public async Task<DetalheTime?> Handle(ObterTime q, CancellationToken ct)
    {
        var times = await ListarTimesHandler.CarregarTimes(db, analise, q.Temporada, q.CompeticaoId is { } c ? [c] : null, ct);
        var t = times.Where(x => x.ClubeId == q.ClubeId).OrderByDescending(x => x.Temporada).ThenByDescending(x => x.Jogos).FirstOrDefault();
        if (t is null) return null;
        var daCompeticao = times.Where(x => x.CompeticaoId == t.CompeticaoId && x.Temporada == t.Temporada).ToList();
        var treinadores = (await analise.Analisar(t.CompeticaoId, t.Temporada, ct))?.Treinadores
            .Where(p => p.Linha.ClubeId == t.ClubeId).OrderBy(p => p.Inicio).Select(ResumirTreinador.De).ToList() ?? [];

        var esquemas = t.EsquemasPorJogo.GroupBy(e => e.Esquema)
            .Select(g => new EsquemaDoTime(g.Key, g.Count(), g.Count(e => e.EmCasa), g.Count(e => !e.EmCasa),
                (int)Math.Round(g.Sum(e => e.Pontos) * 100.0 / (g.Count() * 3))))
            .OrderByDescending(e => e.Jogos)
            .ToList();

        var indicadores = CatalogoTimes.Indicadores
            .Where(i => t.Percentis.GetValueOrDefault(i.Chave) is not null)
            .Select(i => new PercentilIndicador(i.Nome, Math.Round(t.Percentis[i.Chave]!.Value)))
            .ToList();

        var percentilOnze = MotorDeAnalise.Percentil(daCompeticao.Select(x => x.MinutosDosOnzeMaisUsados).OfType<double>().ToList(), t.MinutosDosOnzeMaisUsados);
        var rodizio = percentilOnze switch { null => null, >= 65 => "Time-base fixo", <= 35 => "Muito rodízio", _ => "Rodízio moderado" };
        var elenco = new ElencoTime(
            t.IdadeMedia is { } i ? Math.Round(i, 1) : null,
            t.MinutosAte23 is { } m ? (int)Math.Round(m) : null,
            t.AtletasUsados, rodizio,
            "Idade média e minutos para atletas até 23 anos pesados pelos minutos jogados. O rodízio compara quanto os 11 atletas mais usados concentram os minutos, em relação aos outros times.");

        var principais = (await motor.Analisar(t.CompeticaoId, t.Temporada, ct))?.Linhas
            .Where(l => l.ClubeIds.Contains(t.ClubeId))
            .OrderByDescending(l => l.Minutos)
            .Take(8)
            .Select(l => Resumir.De(l))
            .ToList() ?? [];

        var observacoes = new List<string>();
        if (t.JogosComEstatistica < CatalogoTimes.JogosMinimosComEstatistica)
            observacoes.Add("Sem estatísticas de jogo suficientes nesta competição: posse, finalizações e disciplina ficam de fora.");
        if (t.EsquemasPorJogo.Count == 0)
            observacoes.Add("Sem escalações importadas: esquemas e funções ficam de fora.");
        observacoes.Add("Percentis em relação aos outros times da mesma competição. As funções comparam a nota dos titulares de cada time na mesma função.");

        var detalheDimensoes = CatalogoTimes.Dimensoes
            .Select(d => (d, nota: CatalogoTimes.NotaDaDimensao(t, d)))
            .Where(x => x.nota is not null)
            .Select(x => new Destaque(x.d.Nome, Math.Round(x.nota!.Value), x.d.Descricao))
            .ToList();

        return new DetalheTime(ResumirTime.De(t), esquemas, detalheDimensoes, indicadores, ResumirTime.Estilo(t), elenco,
            ResumirTime.Necessidades(t), principais, treinadores, observacoes);
    }
}

// ---------- Times com carência numa função ----------

public record TimeComCarencia(ResumoTime Time, NecessidadeNaFuncao Necessidade);

public record BuscarTimesComCarencia(string Funcao, int? Temporada = null, int[]? Competicoes = null, int Limite = 8) : IQuery<IReadOnlyList<TimeComCarencia>>;

public class BuscarTimesComCarenciaHandler(IScoutDb db, AnaliseDeTimes analise) : IQueryHandler<BuscarTimesComCarencia, IReadOnlyList<TimeComCarencia>>
{
    public async Task<IReadOnlyList<TimeComCarencia>> Handle(BuscarTimesComCarencia q, CancellationToken ct)
    {
        var times = await ListarTimesHandler.CarregarTimes(db, analise, q.Temporada, q.Competicoes, ct);
        return times
            .SelectMany(t => ResumirTime.Necessidades(t)
                .Where(n => n.Percentil is not null && CatalogoTimes.FuncoesEquivalentes(n.Funcao).Any(f => f.Contains(q.Funcao, StringComparison.OrdinalIgnoreCase)))
                .Select(n => (t, n)))
            .OrderBy(x => x.n.Percentil)
            .Take(Math.Clamp(q.Limite, 1, 30))
            .Select(x => new TimeComCarencia(ResumirTime.De(x.t), x.n))
            .ToList();
    }
}

// ---------- Em que times o atleta encaixa ----------

public record EncaixeEmTime(ResumoTime Time, string Funcao, double? NotaTitulares, double NotaAtleta, double Ganho,
    string? ExperienciaNoEsquema, IReadOnlyList<TitularNaFuncao> TitularesAtuais);

public record ResultadoTimesParaAtleta(ResumoAtleta Atleta, IReadOnlyList<EncaixeEmTime> Times, IReadOnlyList<string> Observacoes);

/// <summary>
/// Times que usam a função do atleta e têm ali titulares com nota menor que a dele.
/// Por padrão só na competição do atleta: as notas são relativas a cada competição.
/// </summary>
public record TimesParaAtleta(int AtletaId, int? Temporada = null, int[]? Competicoes = null, int Limite = 6) : IQuery<ResultadoTimesParaAtleta?>;

public class TimesParaAtletaHandler(IScoutDb db, AnaliseDeTimes analise, MotorDeAnalise motor) : IQueryHandler<TimesParaAtleta, ResultadoTimesParaAtleta?>
{
    public async Task<ResultadoTimesParaAtleta?> Handle(TimesParaAtleta q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, null, ct);
        var atleta = linhas.Where(l => l.AtletaId == q.AtletaId).OrderByDescending(l => l.Minutos).FirstOrDefault();
        if (atleta?.Nota is not { } notaAtleta || atleta.Funcao is null) return null;

        var competicoes = q.Competicoes is { Length: > 0 } c ? c : [atleta.CompeticaoId];
        var funcoes = CatalogoTimes.FuncoesEquivalentes(atleta.Funcao).ToHashSet();
        var times = await ListarTimesHandler.CarregarTimes(db, analise, q.Temporada, competicoes, ct);

        var encaixes = times
            .Where(t => !atleta.ClubeIds.Contains(t.ClubeId))
            .SelectMany(t => ResumirTime.Necessidades(t).Where(n => funcoes.Contains(n.Funcao) && n.NotaTitulares < notaAtleta).Select(n => (t, n)))
            .OrderByDescending(x => notaAtleta - x.n.NotaTitulares!.Value)
            .Take(Math.Clamp(q.Limite, 1, 20))
            .Select(x =>
            {
                var resumo = ResumirTime.De(x.t);
                return new EncaixeEmTime(resumo, x.n.Funcao, x.n.NotaTitulares, Math.Round(notaAtleta), Math.Round(notaAtleta - x.n.NotaTitulares!.Value),
                    Resumir.ExperienciaNoEsquema(atleta, resumo.EsquemaMaisUsado), x.n.Titulares);
            })
            .ToList();

        var observacoes = new List<string> { "Ganho = nota do atleta menos a nota média de quem começa jogando na função hoje." };
        if (competicoes.Any(id => id != atleta.CompeticaoId))
            observacoes.Add("As notas são relativas a cada competição: comparar atletas de divisões diferentes é só uma indicação.");
        return new ResultadoTimesParaAtleta(Resumir.De(atleta), encaixes, observacoes);
    }
}
