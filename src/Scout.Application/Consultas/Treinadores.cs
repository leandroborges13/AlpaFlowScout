using Scout.Application.Analise;

namespace Scout.Application.Consultas;

/// <summary>
/// Um treinador num clube e temporada: resultados e esquemas (fatos públicos) e o estilo do time sob o comando
/// dele, em percentil entre as passagens de treinadores da mesma competição.
/// </summary>
public record ResumoTreinador(
    int Id, string Nome, string? Foto, int ClubeId, string Clube, string? Escudo,
    int CompeticaoId, string Competicao, int Temporada, bool NoCargo, DateTime Inicio, DateTime Fim,
    int Jogos, int Vitorias, int Empates, int Derrotas, int? Aproveitamento,
    string? EsquemaPreferido, string? Estilo, IReadOnlyList<Destaque> Dimensoes, int? TitularesAte23);

public record OutroTreinadorNoClube(int Id, string Nome, DateTime Inicio, DateTime Fim, int Jogos, int? Aproveitamento);

public record PassagemDetalhada(ResumoTreinador Passagem, IReadOnlyList<EsquemaDoTime> Esquemas, IReadOnlyList<PercentilIndicador> Indicadores,
    EstiloTime? Estilo, double? IdadeMediaTitulares, double? PercentilJovens, IReadOnlyList<OutroTreinadorNoClube> NoMesmoClube, string? Comparacao);

public record DetalheTreinador(ResumoTreinador Resumo, IReadOnlyList<PassagemDetalhada> Passagens, CarreiraTreinador? Carreira, IReadOnlyList<string> Observacoes);

public static class ResumirTreinador
{
    public static ResumoTreinador De(PassagemNaTemporada p) => new(
        p.TreinadorId, p.Nome, p.Foto, p.Linha.ClubeId, p.Linha.Nome, p.Linha.Escudo,
        p.Linha.CompeticaoId, p.Linha.Competicao, p.Linha.Temporada, p.NoCargo, p.Inicio, p.Fim,
        p.Linha.Jogos, p.Linha.Vitorias, p.Linha.Empates, p.Linha.Derrotas,
        p.Linha.Aproveitamento is { } a ? (int)Math.Round(a) : null,
        p.Linha.EsquemasPorJogo.GroupBy(e => e.Esquema).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
        ResumirTime.Estilo(p.Linha)?.Rotulo, ResumirTime.Dimensoes(p.Linha),
        // Com poucos jogos a porcentagem engana (1 jogo com 6 jovens vira "55%")
        p.Linha.Jogos >= PassagemNaTemporada.JogosMinimos && p.Linha.TitularesAte23 is { } j ? (int)Math.Round(j) : null);

    /// <summary>"Assumiu depois de Fulano (38% em 12 jogos). Com ele, 62% em 15 jogos."</summary>
    public static string? Comparacao(PassagemNaTemporada p, IReadOnlyList<PassagemNaTemporada> doClube)
    {
        var antes = doClube.Where(o => o.TreinadorId != p.TreinadorId && o.Fim <= p.Inicio).OrderByDescending(o => o.Fim).FirstOrDefault();
        if (antes is null || p.Linha.Jogos < PassagemNaTemporada.JogosMinimos || p.Linha.Aproveitamento is not { } com || antes.Linha.Aproveitamento is not { } sem) return null;
        var diferenca = (int)Math.Round(com - sem);
        var verbo = diferenca switch { >= 5 => "melhorou", <= -5 => "piorou", _ => "manteve" };
        return $"Assumiu depois de {antes.Nome} ({Math.Round(sem)}% de aproveitamento em {antes.Linha.Jogos} jogos). " +
               $"Com ele, {Math.Round(com)}% em {p.Linha.Jogos} jogos: o time {verbo} {(verbo == "manteve" ? "o desempenho" : $"{Math.Abs(diferenca)} pontos percentuais")}.";
    }
}

// ---------- Treinadores ----------

public record ListarTreinadores(int? CompeticaoId = null, string? Nome = null, int? Temporada = null, bool SoNoCargo = true) : IQuery<IReadOnlyList<ResumoTreinador>>;

public class ListarTreinadoresHandler(IScoutDb db, AnaliseDeTimes analise) : IQueryHandler<ListarTreinadores, IReadOnlyList<ResumoTreinador>>
{
    public async Task<IReadOnlyList<ResumoTreinador>> Handle(ListarTreinadores q, CancellationToken ct)
    {
        var analises = await ListarTimesHandler.CarregarAnalises(db, analise, q.Temporada, q.CompeticaoId is { } c ? [c] : null, ct);
        var passagens = analises.SelectMany(a => a.Treinadores);
        // Buscando pelo nome, vale qualquer passagem, da mais parecida para a menos; sem nome, só quem está no cargo
        var ordenadas = q.Nome is not null
            ? BuscaPorNome.Filtrar(passagens, q.Nome, p => p.Nome)
            : passagens.Where(p => !q.SoNoCargo || p.NoCargo).OrderBy(p => p.Linha.CompeticaoId).ThenByDescending(p => p.Linha.Aproveitamento);
        return ordenadas
            .Select(ResumirTreinador.De)
            .ToList();
    }
}

public record ObterTreinador(int TreinadorId, int? Temporada = null) : IQuery<DetalheTreinador?>;

public class ObterTreinadorHandler(IScoutDb db, AnaliseDeTimes analise, ICarreiraDoTreinador carreira) : IQueryHandler<ObterTreinador, DetalheTreinador?>
{
    public async Task<DetalheTreinador?> Handle(ObterTreinador q, CancellationToken ct)
    {
        var analises = await ListarTimesHandler.CarregarAnalises(db, analise, q.Temporada, null, ct);
        var passagens = analises.SelectMany(a => a.Treinadores.Select(p => (a, p))).Where(x => x.p.TreinadorId == q.TreinadorId)
            .OrderByDescending(x => x.p.Fim).ToList();
        if (passagens.Count == 0) return null;

        var detalhadas = passagens.Select(x =>
        {
            var (a, p) = x;
            var doClube = a.Treinadores.Where(o => o.Linha.ClubeId == p.Linha.ClubeId).OrderBy(o => o.Inicio).ToList();
            var jovens = a.Treinadores.Where(o => o.Linha.Jogos >= PassagemNaTemporada.JogosMinimos).Select(o => o.Linha.TitularesAte23).OfType<double>().ToList();
            return new PassagemDetalhada(
                ResumirTreinador.De(p),
                p.Linha.EsquemasPorJogo.GroupBy(e => e.Esquema)
                    .Select(g => new EsquemaDoTime(g.Key, g.Count(), g.Count(e => e.EmCasa), g.Count(e => !e.EmCasa),
                        (int)Math.Round(g.Sum(e => e.Pontos) * 100.0 / (g.Count() * 3))))
                    .OrderByDescending(e => e.Jogos).ToList(),
                CatalogoTimes.Indicadores.Where(i => p.Linha.Percentis.GetValueOrDefault(i.Chave) is not null)
                    .Select(i => new PercentilIndicador(i.Nome, Math.Round(p.Linha.Percentis[i.Chave]!.Value))).ToList(),
                ResumirTime.Estilo(p.Linha),
                p.Linha.IdadeMediaTitulares is { } idade ? Math.Round(idade, 1) : null,
                p.Linha.Jogos >= PassagemNaTemporada.JogosMinimos && MotorDeAnalise.Percentil(jovens, p.Linha.TitularesAte23) is { } pj ? Math.Round(pj) : null,
                doClube.Where(o => o.TreinadorId != p.TreinadorId)
                    .Select(o => new OutroTreinadorNoClube(o.TreinadorId, o.Nome, o.Inicio, o.Fim, o.Linha.Jogos,
                        o.Linha.Aproveitamento is { } ap ? (int)Math.Round(ap) : null)).ToList(),
                ResumirTreinador.Comparacao(p, doClube));
        }).ToList();

        var observacoes = new List<string>
        {
            "Percentis em relação aos outros treinadores da mesma competição e temporada, só nos jogos em que cada um comandou o time.",
        };
        if (detalhadas[0].Passagem.Jogos < PassagemNaTemporada.JogosMinimos)
            observacoes.Add($"Menos de {PassagemNaTemporada.JogosMinimos} jogos na passagem mais recente: ainda sem percentis.");

        return new DetalheTreinador(detalhadas[0].Passagem, detalhadas, await carreira.Obter(q.TreinadorId, ct), observacoes);
    }
}
