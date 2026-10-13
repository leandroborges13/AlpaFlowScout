using System.Text.Json;
using Scout.Application.Consultas;
using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class PartidasTests
{
    static List<JsonElement> Json(string texto) => JsonDocument.Parse(texto).RootElement.EnumerateArray().ToList();

    [Fact]
    public void Le_escalacao_eventos_e_acerta_quem_saiu_na_substituicao()
    {
        var escalacoes = Json("""
            [{"team":{"id":1,"name":"Time Teste"},"coach":{"id":9,"name":"Treinador Teste"},"formation":"4-4-2",
              "startXI":[{"player":{"id":10,"name":"Atleta Teste A","grid":"2:1"}},{"player":{"id":11,"name":"Atleta Teste B","grid":"4:1"}}],
              "substitutes":[{"player":{"id":20,"name":"Atleta Teste C","grid":null}}]}]
            """);
        // O provedor manda "player" = quem entrou e "assist" = quem saiu nesta substituição
        var eventos = Json("""
            [{"time":{"elapsed":60,"extra":null},"team":{"id":1},"player":{"id":20,"name":"Atleta Teste C"},"assist":{"id":11,"name":"Atleta Teste B"},"type":"subst","detail":"Substitution 1"},
             {"time":{"elapsed":12,"extra":null},"team":{"id":1},"player":{"id":10,"name":"Atleta Teste A"},"assist":{"id":11,"name":"Atleta Teste B"},"type":"Goal","detail":"Normal Goal"},
             {"time":{"elapsed":45,"extra":2},"team":{"id":1},"player":{"id":10,"name":"Atleta Teste A"},"assist":{"id":null,"name":null},"type":"Card","detail":"Yellow Card"}]
            """);

        var d = DetalhesDaPartida.Ler(escalacoes, eventos);

        var lado = Assert.Single(d.Lados);
        Assert.Equal("4-4-2", lado.Esquema);
        Assert.Equal("Lateral esquerdo", lado.Titulares[0].Funcao);
        Assert.Equal("Treinador Teste", lado.Treinador);

        Assert.Equal(["Gol", "Cartão amarelo", "Substituição"], d.Eventos.Select(e => e.Tipo)); // em ordem de minuto
        Assert.Equal("Atleta Teste B", d.Eventos[0].Outro); // assistência
        Assert.Equal(2, d.Eventos[1].Acrescimo);
        var troca = d.Eventos[2];
        Assert.Equal("Atleta Teste B", troca.Jogador); // saiu quem estava em campo
        Assert.Equal("Atleta Teste C", troca.Outro);
    }

    [Theory]
    [InlineData("Galo", "Atletico MG")]
    [InlineData("o Peixe", "Santos")]
    [InlineData("Santos", "Santos")]
    public void Traduz_apelidos_de_torcida(string busca, string esperado) =>
        Assert.Equal(esperado, BuscaPorNome.TraduzirApelido(busca));
}
