namespace Scout.Domain;

/// <summary>
/// Palpite do modelo AlpaFlow para um jogo (backlog item 4): chances de vitória do mandante, empate e vitória do visitante e o
/// placar mais provável, pela força tipo Elo dos dois times. Pode ser refeito até o início do jogo; depois fica congelado,
/// para o "acertou ou errou?" valer. Um por partida.
/// </summary>
public class PalpiteDaPartida
{
    public int PartidaId { get; set; }
    public DateTime FeitoEm { get; set; }
    public double ForcaMandante { get; set; }
    public double ForcaVisitante { get; set; }
    /// <summary>Chances em % (somam 100).</summary>
    public double ChanceMandante { get; set; }
    public double ChanceEmpate { get; set; }
    public double ChanceVisitante { get; set; }
    public int PlacarMandante { get; set; }
    public int PlacarVisitante { get; set; }
    public double ChancePlacar { get; set; }
}
