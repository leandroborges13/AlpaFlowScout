using System.Text.Json;
using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class TreinadoresTests
{
    static PassagemNaTemporada Passagem(int id, string nome, int v, int e, int d, DateTime inicio, DateTime fim) =>
        new(id, nome, null, new LinhaTime { ClubeId = 1, Nome = "Time Teste", Jogos = v + e + d, Vitorias = v, Empates = e, Derrotas = d }, inicio, fim, false, []);

    [Fact]
    public void Compara_com_o_antecessor_no_mesmo_clube()
    {
        var antes = Passagem(1, "Treinador Teste A", v: 2, e: 2, d: 6, new(2026, 2, 1), new(2026, 5, 1)); // 8 de 30 = 27%
        var depois = Passagem(2, "Treinador Teste B", v: 6, e: 2, d: 2, new(2026, 5, 8), new(2026, 9, 1)); // 20 de 30 = 67%

        var texto = ResumirTreinador.Comparacao(depois, [antes, depois]);

        Assert.NotNull(texto);
        Assert.Contains("Treinador Teste A", texto);
        Assert.Contains("melhorou 40 pontos percentuais", texto);
        Assert.Null(ResumirTreinador.Comparacao(antes, [antes, depois])); // o primeiro não tem antecessor
    }

    [Fact]
    public void Mesmo_treinador_com_dois_cadastros_vira_um_so()
    {
        var nomes = new Dictionary<int, string> { [1] = "Souza Alex", [2] = "Alex", [3] = "R. Teste", [4] = "Rafael Teste", [5] = "Outro Nome" };
        var jogos = new Dictionary<int, int> { [1] = 31, [2] = 1, [3] = 2, [4] = 10, [5] = 5 };

        var u = CatalogoTimes.UnificarTreinadores(jogos, id => nomes[id]);

        Assert.Equal(1, u[2]);
        Assert.Equal(4, u[3]);
        Assert.Equal(5, u[5]);
        Assert.Equal(1, u[1]);
    }

    [Fact]
    public void Le_a_carreira_do_provedor_da_mais_recente_para_a_mais_antiga()
    {
        var json = JsonDocument.Parse("""
            {"id":1,"name":"Treinador Teste","age":48,"nationality":"Brazil",
             "career":[{"team":{"id":10,"name":"Clube Antigo","logo":null},"start":"2019-01-01","end":"2021-06-30"},
                       {"team":{"id":11,"name":"Clube Atual","logo":"x.png"},"start":"2024-03-10","end":null}]}
            """).RootElement;

        var c = CarreiraDoTreinador.Ler(json);

        Assert.Equal(48, c.Idade);
        Assert.Equal("Clube Atual", c.Passagens[0].Clube);
        Assert.Null(c.Passagens[0].Fim);
        Assert.Equal(new DateOnly(2021, 6, 30), c.Passagens[1].Fim);
    }
}
