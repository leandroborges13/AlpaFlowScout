using Scout.Application.Consultas;

namespace Scout.Tests;

public class LigarNomesTests
{
    static readonly (int, string, string?)[] Elenco =
    [
        (1, "T. Cuello", "Tomás Cuello"), (2, "A. Preciado", "Ángelo Preciado"), (3, "Dudu", "Eduardo Pereira Rodrigues"),
        (4, "Vitor Hugo", "Vitor Hugo Franchescoli de Souza"), (5, "Vitão", "Vitor Eduardo da Silva"), (6, "Mamady Cissé", null),
        (7, "Samuel", "Samuel Paes de Oliveira"), (8, "Gabriel Delfim", null), (9, "Gabriel Veneno", "Gabriel Fernandes"),
    ];

    [Theory]
    [InlineData("Tomás Cuello", 1)]
    [InlineData("Ángelo Preciado", 2)]
    [InlineData("Dudu", 3)]
    [InlineData("Vitor Hugo", 4)]
    [InlineData("Vitão", 5)]
    [InlineData("Mamady Cisse", 6)]
    [InlineData("Samuel Paes", 7)]
    [InlineData("Gabriel Veneno", 9)]
    public void Liga_os_nomes_da_outra_fonte_ao_nosso_cadastro(string nome, int esperado) =>
        Assert.Equal(esperado, LigarNomes.Ligar(nome, Elenco));

    [Fact]
    public void Na_duvida_nao_liga()
    {
        Assert.Null(LigarNomes.Ligar("Gabriel", Elenco)); // Delfim ou Veneno?
        Assert.Null(LigarNomes.Ligar("Hulk", Elenco));
    }
}
