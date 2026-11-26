using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

/// <summary>A temporada no formato da PitchAPI: ano civil ("2026") ou dividida ("2026/2027", calendário europeu e México).</summary>
public class PitchApiTests
{
    [Theory]
    [InlineData(71, 2026, "2026")]          // Série A: ano civil
    [InlineData(128, 2026, "2026")]         // Argentina
    [InlineData(262, 2026, "2026/2027")]    // México: Apertura 2026 + Clausura 2027 (antes dava 0 jogos)
    [InlineData(94, 2025, "2025/2026")]     // Portugal
    public void Temporada_no_formato_da_PitchAPI(int competicao, int temporada, string esperado) =>
        Assert.Equal(esperado, PitchApi.Temporada(competicao, temporada));
}
