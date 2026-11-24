using Scout.Infrastructure.Provedor;
using Xunit;

namespace Scout.Tests;

public class LigarClubesTests
{
    static readonly Dictionary<int, (string Nome, string? Curto)> Argentina = new()
    {
        [1] = ("Independiente Rivadavia", "Ind. Rivadavia"), [2] = ("Independiente", null), [3] = ("Talleres", null),
        [4] = ("Belgrano", null), [5] = ("Gimnasia y Esgrima La Plata", "Gimnasia LP"), [6] = ("Gimnasia de Mendoza", "Gimnasia M"),
        [7] = ("Estudiantes de La Plata", "Estudiantes"),
    };

    [Theory]
    [InlineData("Independ. Rivadavia", 1)]
    [InlineData("Talleres Cordoba", 3)]
    [InlineData("Belgrano Cordoba", 4)]
    [InlineData("Estudiantes L.P.", 7)]
    public void Liga_nome_abreviado_ou_com_cidade(string nosso, int esperado) =>
        Assert.Equal(esperado, ColetaBzzoiro.MelhorPar(nosso, Argentina));

    [Fact]
    public void Empate_nao_liga()
    {
        // "Gimnasia M." casa com os dois Gimnasia pela palavra "gimnasia": na dúvida, não liga
        var times = new Dictionary<int, (string Nome, string? Curto)> { [5] = ("Gimnasia y Esgrima La Plata", null), [6] = ("Gimnasia de Mendoza", null) };
        Assert.Null(ColetaBzzoiro.MelhorPar("Gimnasia", times));
    }

    [Theory]
    [InlineData("Bayern München", "Bayern Munich")]
    [InlineData("Al-Hilal Saudi FC", "Al Hilal")]
    [InlineData("Los Angeles Galaxy", "LA Galaxy")]
    [InlineData("Chapecoense-sc", "Chapecoense")]
    public void Liga_variacoes_de_nome(string nosso, string deLa) =>
        Assert.Equal(9, ColetaBzzoiro.MelhorPar(nosso, new Dictionary<int, (string, string?)> { [9] = (deLa, null), [8] = ("Outro Time", null) }));
}
