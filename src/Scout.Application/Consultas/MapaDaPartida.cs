namespace Scout.Application.Consultas;

/// <summary>
/// Finalização no campo de 105x68 metros, sempre atacando para a direita (x = 105 é o gol atacado), como a fonte entrega.
/// `Xg`: chance de gol da finalização (0 a 1), da fonte do mapa (não é o xG do provedor principal).
/// </summary>
public record FinalizacaoNoMapa(int ClubeId, string Jogador, int Minuto, double X, double Y, double Xg, bool NoAlvo, bool Gol,
    bool DentroDaArea, string? Situacao);

/// <summary>
/// Onde o time teve a bola: grade de 16 x 12 células com o número de ações em cada uma, os dois times no mesmo campo
/// (mandante atacando para a direita). Célula [x, y, ações], com x de 0 (gol do mandante) a 15.
/// </summary>
public record CalorDoTime(int ClubeId, int Acoes, IReadOnlyList<int[]> Celulas);

/// <summary>Mapa de finalizações e de calor de uma partida, de uma fonte externa (hoje a PitchAPI: só ligas e só pós-jogo).</summary>
public record MapaDaPartida(IReadOnlyList<FinalizacaoNoMapa> Finalizacoes, IReadOnlyList<CalorDoTime> Calor, string Fonte);

public interface IMapaDaPartida
{
    /// <summary>Nulo quando a fonte não está configurada, não cobre a competição ou ainda não tem o jogo.</summary>
    Task<MapaDaPartida?> Obter(ResumoPartida partida, CancellationToken ct);
}
