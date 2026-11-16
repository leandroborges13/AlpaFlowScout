using System.Text.Json;
using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class PlacarAoVivoTests
{
    // Formato do /fixtures?id= e do /fixtures/statistics da API-Football (trecho)
    const string Jogo = """
        { "fixture": { "id": 1, "status": { "short": "HT", "elapsed": 45 } },
          "teams": { "home": { "id": 7848, "name": "Mirassol" }, "away": { "id": 1062, "name": "Atletico-MG" } },
          "goals": { "home": 0, "away": 1 } }
        """;
    const string Numeros = """
        [ { "team": { "id": 7848 }, "statistics": [ { "type": "Ball Possession", "value": "46%" }, { "type": "Total Shots", "value": 5 },
              { "type": "Shots on Goal", "value": 1 }, { "type": "Corner Kicks", "value": null }, { "type": "expected_goals", "value": "0.41" } ] },
          { "team": { "id": 1062 }, "statistics": [ { "type": "Ball Possession", "value": "54%" }, { "type": "Total Shots", "value": 8 },
              { "type": "Shots on Goal", "value": 3 }, { "type": "Corner Kicks", "value": 4 } ] } ]
        """;

    [Fact]
    public void Le_o_placar_e_os_numeros_de_cada_time_no_intervalo()
    {
        var p = PlacarAoVivoDoProvedor.Ler(1, JsonDocument.Parse(Jogo).RootElement, JsonDocument.Parse(Numeros).RootElement.EnumerateArray().ToList());
        Assert.True(p.Intervalo);
        Assert.False(p.Encerrado);
        Assert.Equal(45, p.Minuto);
        Assert.Equal(("Mirassol", 0, 46, 5, 1), (p.Mandante.Time, p.Mandante.Gols, p.Mandante.Posse, p.Mandante.Finalizacoes, p.Mandante.NoAlvo));
        Assert.Null(p.Mandante.Escanteios);          // sem valor = não informado
        Assert.Equal(0.41, p.Mandante.Xg);
        Assert.Equal(("Atletico-MG", 1, 54, 8, 3, 4), (p.Visitante.Time, p.Visitante.Gols, p.Visitante.Posse, p.Visitante.Finalizacoes, p.Visitante.NoAlvo, p.Visitante.Escanteios));
        Assert.Null(p.Visitante.Xg);
    }

    [Fact]
    public void Sem_numeros_ainda_o_placar_sai_assim_mesmo()
    {
        var p = PlacarAoVivoDoProvedor.Ler(1, JsonDocument.Parse(Jogo).RootElement, []);
        Assert.Equal(1, p.Visitante.Gols);
        Assert.Null(p.Visitante.Posse);
    }
}
