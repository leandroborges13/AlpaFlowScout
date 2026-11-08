using Scout.Application.Consultas;

namespace Scout.Tests;

public class PalpitesTests
{
    [Fact]
    public void Times_iguais_em_casa_o_mandante_e_favorito_e_as_chances_somam_100()
    {
        var c = PalpitesHandler.Calcular(1600, 1600);
        Assert.InRange(c.Mandante + c.Empate + c.Visitante, 99.8, 100.2);
        Assert.True(c.Mandante > c.Visitante);
        Assert.InRange(c.Empate, 20, 32);
        Assert.Equal("1 x 0", $"{c.PlacarMandante} x {c.PlacarVisitante}"); // com mando, 1 x 0 (12,9%) passa o 1 x 1 (12,6%)
    }

    [Fact]
    public void Em_campo_neutro_times_iguais_tem_a_mesma_chance()
    {
        var c = PalpitesHandler.Calcular(1600, 1600, neutro: true);
        Assert.Equal(c.Mandante, c.Visitante);
    }

    [Fact]
    public void Time_bem_mais_forte_fora_ainda_e_favorito()
    {
        var c = PalpitesHandler.Calcular(1500, 1750);
        Assert.Equal("visitante", PalpitesHandler.Favorito(c.Mandante, c.Empate, c.Visitante));
        Assert.True(c.PlacarVisitante > c.PlacarMandante);
    }
}
