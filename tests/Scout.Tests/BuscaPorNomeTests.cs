using Scout.Application.Consultas;

namespace Scout.Tests;

public class BuscaPorNomeTests
{
    [Theory]
    [InlineData("Atlético MG", "Atletico-MG")]
    [InlineData("atletico mineiro", "Atletico-MG")]
    [InlineData("Eduardo Domingues o Barba", "Dominguez Eduardo")]
    [InlineData("São Paulo", "Sao Paulo")]
    [InlineData("Gremio", "Grêmio")]
    public void Acha_nomes_sem_acento_hifen_ou_com_erro_de_digitacao(string busca, string nome) =>
        Assert.True(BuscaPorNome.Pontuar(busca, nome) > 0);

    [Fact]
    public void So_uma_sigla_em_comum_nao_basta()
    {
        Assert.Equal(0, BuscaPorNome.Pontuar("Atlético MG", "America-MG"));
        Assert.Equal(0, BuscaPorNome.Pontuar("Santos", "Atletico-MG"));
    }

    [Fact]
    public void O_mais_parecido_vem_primeiro()
    {
        var nomes = new[] { "Eduardo Barros", "Dominguez Eduardo", "Atletico-GO" };

        Assert.Equal("Dominguez Eduardo", BuscaPorNome.Filtrar(nomes, "Eduardo Domingues", n => n).First());
        Assert.Equal("Atletico-MG", BuscaPorNome.Filtrar(new[] { "Athletico-PR", "Atletico-GO", "Atletico-MG", "America-MG" }, "Atlético MG", n => n).First());
    }
}
