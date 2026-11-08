using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class ColetaBzzoiroTests
{
    [Theory]
    [InlineData("Atletico-MG", "Atlético Mineiro", "Atlético-MG", true)]
    [InlineData("Gremio", "Grêmio", "Grêmio", true)]
    [InlineData("Atletico-MG", "Atlético Goianiense", "Atlético-GO", false)]
    [InlineData("Atletico Paranaense", "Atlético Mineiro", "Atlético-MG", false)]
    public void Liga_o_clube_pelo_nome_ou_pelo_nome_curto(string nosso, string nome, string curto, bool esperado) =>
        Assert.Equal(esperado, ColetaBzzoiro.MesmoClube(nosso, nome, curto));

    [Fact]
    public void Primeiro_de_janeiro_nao_vale_para_a_regra_dos_18()
    {
        Assert.False(ColetaBzzoiro.Confiavel(new DateOnly(2007, 1, 1)));
        Assert.True(ColetaBzzoiro.Confiavel(new DateOnly(2008, 4, 5)));
    }
}
