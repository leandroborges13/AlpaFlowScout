namespace Scout.Application.Analise;

/// <summary>Um número medido por atleta. `Inverso` = quanto menor, melhor.</summary>
public record Indicador(string Chave, string Nome, Func<LinhaAnalise, double?> Valor, bool Inverso = false);

/// <summary>Uma característica que o usuário pede ("jogo aéreo") e os indicadores que a medem.</summary>
public record Atributo(string Chave, string Nome, string Descricao, string[] Indicadores, string? Observacao = null);

public static class Catalogo
{
    static double? Por90(int valor, LinhaAnalise l) => l.Minutos > 0 ? valor * 90.0 / l.Minutos : null;
    static double? Taxa(int parte, int total, int minimo) => total >= minimo ? parte * 100.0 / total : null;

    /// <summary>
    /// Ajuste à posse (PAdj): com 65% de posse, o adversário tem a bola só 35% do tempo e o time defende menos vezes.
    /// Os números defensivos são levados para uma posse de 50% (×50/35), senão defensores de times dominantes parecem fracos.
    /// </summary>
    public static double AjustePosse(LinhaAnalise l) => l.PosseDoTime is { } p && p is > 20 and < 80 ? 50 / (100 - p) : 1;
    static double? Defensivo90(int valor, LinhaAnalise l) => Por90(valor, l) * AjustePosse(l);

    public static readonly Indicador[] Indicadores =
    [
        new("gols90", "Gols por 90 min", l => Por90(l.Gols, l)),
        new("assistencias90", "Assistências por 90 min", l => Por90(l.Assistencias, l)),
        new("finalizacoesNoAlvo90", "Finalizações no alvo por 90 min", l => Por90(l.FinalizacoesNoAlvo, l)),
        new("aproveitamentoFinalizacoes", "% de finalizações no alvo", l => Taxa(l.FinalizacoesNoAlvo, l.Finalizacoes, 10)),
        new("passes90", "Passes por 90 min", l => Por90(l.Passes, l)),
        new("precisaoPasses", "Precisão de passe", l => l.PrecisaoPasses),
        new("passesDecisivos90", "Passes decisivos por 90 min", l => Por90(l.PassesDecisivos, l)),
        new("desarmes90", "Desarmes por 90 min (ajustado à posse)", l => Defensivo90(l.Desarmes, l)),
        new("interceptacoes90", "Interceptações por 90 min (ajustado à posse)", l => Defensivo90(l.Interceptacoes, l)),
        new("bloqueios90", "Bloqueios por 90 min (ajustado à posse)", l => Defensivo90(l.Bloqueios, l)),
        new("duelos90", "Duelos disputados por 90 min", l => Por90(l.Duelos, l)),
        new("duelosGanhos90", "Duelos ganhos por 90 min", l => Por90(l.DuelosGanhos, l)),
        new("aproveitamentoDuelos", "% de duelos ganhos", l => Taxa(l.DuelosGanhos, l.Duelos, 20)),
        new("dribles90", "Dribles certos por 90 min", l => Por90(l.DriblesCertos, l)),
        new("aproveitamentoDribles", "% de dribles certos", l => Taxa(l.DriblesCertos, l.DriblesTentados, 10)),
        new("faltasSofridas90", "Faltas sofridas por 90 min", l => Por90(l.FaltasSofridas, l)),
        new("faltasCometidas90", "Faltas cometidas por 90 min", l => Por90(l.FaltasCometidas, l), Inverso: true),
        new("cartoes90", "Cartões por 90 min", l => Por90(l.Amarelos + 2 * l.Vermelhos, l), Inverso: true),
        new("defesas90", "Defesas por 90 min", l => Por90(l.Defesas, l)),
        new("golsSofridos90", "Gols sofridos por 90 min", l => Por90(l.GolsSofridos, l), Inverso: true),
        new("altura", "Altura", l => l.AlturaCm),
        new("participacaoMinutos", "% dos minutos do clube", l => l.ParticipacaoMinutos * 100),
    ];

    public static readonly Dictionary<string, Indicador> PorChave = Indicadores.ToDictionary(i => i.Chave);

    public static readonly Atributo[] Atributos =
    [
        new("jogo_aereo", "Jogo aéreo", "Altura e força nos duelos",
            ["altura", "duelosGanhos90", "aproveitamentoDuelos"],
            "Aproximação: o provedor atual não separa os duelos aéreos dos duelos no chão."),
        new("saida_de_bola", "Saída de bola / construção", "Volume e qualidade do passe",
            ["passes90", "precisaoPasses", "passesDecisivos90"],
            "Aproximação: sem dados de passe progressivo no provedor atual."),
        new("marcacao", "Marcação", "Recuperação de bola", ["desarmes90", "interceptacoes90", "bloqueios90"]),
        new("intensidade", "Intensidade / físico", "Quanto disputa e quanto joga",
            ["duelos90", "duelosGanhos90", "participacaoMinutos"],
            "Aproximação: dados físicos (velocidade, distância) exigem rastreamento, que o provedor atual não tem."),
        new("drible", "Drible", "Ganhar no um contra um", ["dribles90", "aproveitamentoDribles", "faltasSofridas90"]),
        new("criacao", "Criação", "Gerar chances para os outros", ["passesDecisivos90", "assistencias90"]),
        new("finalizacao", "Finalização", "Fazer gol e finalizar bem", ["gols90", "finalizacoesNoAlvo90", "aproveitamentoFinalizacoes"]),
        new("disciplina", "Disciplina", "Poucas faltas e cartões", ["faltasCometidas90", "cartoes90"]),
        new("regularidade", "Regularidade", "Peça fixa no time", ["participacaoMinutos"]),
        new("goleiro", "Goleiro", "Defesas e gols sofridos", ["defesas90", "golsSofridos90"]),
    ];

    public static readonly Dictionary<string, Atributo> AtributoPorChave = Atributos.ToDictionary(a => a.Chave);

    /// <summary>Indicadores usados na nota geral de cada setor.</summary>
    public static string[] IndicadoresDoSetor(Domain.Setor setor) => setor switch
    {
        Domain.Setor.Goleiro => ["defesas90", "golsSofridos90"],
        Domain.Setor.Defesa => ["desarmes90", "interceptacoes90", "duelosGanhos90", "aproveitamentoDuelos", "precisaoPasses", "faltasCometidas90"],
        Domain.Setor.Meio => ["passesDecisivos90", "precisaoPasses", "desarmes90", "interceptacoes90", "dribles90", "duelosGanhos90"],
        _ => ["gols90", "assistencias90", "finalizacoesNoAlvo90", "passesDecisivos90", "dribles90", "faltasSofridas90"],
    };
}
