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

    [Fact]
    public void Cobrancas_da_disputa_de_penaltis_nao_viram_gols_do_jogo()
    {
        var eventos = Json("""
            [{"time":{"elapsed":120,"extra":null},"team":{"id":1},"player":{"id":10,"name":"Atleta Teste A"},"assist":{"id":null,"name":null},"type":"Goal","detail":"Penalty","comments":"Penalty Shootout"},
             {"time":{"elapsed":120,"extra":null},"team":{"id":2},"player":{"id":20,"name":"Atleta Teste B"},"assist":{"id":null,"name":null},"type":"Goal","detail":"Missed Penalty","comments":"Penalty Shootout"},
             {"time":{"elapsed":80,"extra":null},"team":{"id":1},"player":{"id":10,"name":"Atleta Teste A"},"assist":{"id":null,"name":null},"type":"Goal","detail":"Penalty","comments":null}]
            """);

        var d = DetalhesDaPartida.Ler([], eventos);

        Assert.Equal(["Gol de pênalti", "Pênalti convertido na disputa", "Pênalti perdido na disputa"], d.Eventos.Select(e => e.Tipo));
    }

    [Fact]
    public void Treinador_com_id_zero_e_tratado_como_nao_informado()
    {
        var escalacoes = Json("""[{"team":{"id":1,"name":"Time Teste"},"coach":{"id":0,"name":null},"formation":"4-3-3","startXI":[],"substitutes":[]}]""");
        var lado = Assert.Single(DetalhesDaPartida.Ler(escalacoes, []).Lados);
        Assert.Null(lado.TreinadorId);
        Assert.Null(lado.Treinador);
    }

    [Theory]
    [InlineData("Galo", "Atletico MG")]
    [InlineData("o Peixe", "Santos")]
    [InlineData("Santos", "Santos")]
    public void Traduz_apelidos_de_torcida(string busca, string esperado) =>
        Assert.Equal(esperado, BuscaPorNome.TraduzirApelido(busca));

    [Fact]
    public void Le_numeros_dos_atletas_descarta_quem_nao_entrou_e_le_passes_certos()
    {
        var times = Json("""
            [{"team":{"id":1},"players":[
              {"player":{"id":10,"name":"Atleta Teste A"},"statistics":[{"games":{"minutes":90,"position":"M"},"shots":{"total":2,"on":1},
                "goals":{"total":1,"assists":null,"conceded":0,"saves":null},"passes":{"total":40,"key":3,"accuracy":"34"},
                "tackles":{"total":2,"blocks":null,"interceptions":1},"duels":{"total":10,"won":6},"dribbles":{"attempts":3,"success":2},
                "fouls":{"drawn":1,"committed":2},"cards":{"yellow":1,"red":0}}]},
              {"player":{"id":11,"name":"Atleta Teste B"},"statistics":[{"games":{"minutes":null,"position":"D"}}]},
              {"player":{"id":12,"name":"Atleta Teste C"},"statistics":[{"games":{"minutes":20,"position":"F"},"passes":{"total":5,"key":0,"accuracy":"80%"}}]}]}]
            """);

        var lista = DetalhesDaPartida.LerJogadores(times);

        Assert.Equal([10, 12], lista.Select(j => j.AtletaId)); // quem ficou no banco (sem minutos) sai
        var a = lista[0];
        Assert.Equal(1, a.ClubeId);
        Assert.Equal(90, a.Minutos);
        Assert.Equal(34, a.PassesCertos); // "accuracy" em /fixtures/players é a quantidade de passes certos
        Assert.Equal(0, a.Assistencias); // null vira 0
        Assert.Equal(6, a.DuelosGanhos);
        Assert.Equal(4, lista[1].PassesCertos); // 80 > 5 passes: era porcentagem
    }

    [Fact]
    public void Titular_recebe_o_lugar_no_desenho_tatico()
    {
        var escalacoes = Json("""
            [{"team":{"id":1,"name":"Time Teste"},"formation":"4-3-3",
              "startXI":[{"player":{"id":1,"name":"Goleiro Teste","grid":"1:1"}},{"player":{"id":9,"name":"Atacante Teste","grid":"4:2"}}],
              "substitutes":[{"player":{"id":20,"name":"Reserva Teste","grid":null}}]}]
            """);
        var lado = Assert.Single(DetalhesDaPartida.Ler(escalacoes, []).Lados);
        Assert.Equal((50.0, 5.0), (lado.Titulares[0].X, lado.Titulares[0].Y));
        Assert.Equal(50.0, lado.Titulares[1].X); // centroavante no meio
        Assert.Null(lado.Reservas[0].X);
    }

    [Fact]
    public void Percentil_do_jogo_fica_entre_1_e_99_e_pesa_menos_em_jogo_curto()
    {
        var temporada = Enumerable.Range(0, 40).Select(k => 1.0 + k * 0.05).ToList(); // médias por 90 de 1,0 a 2,95
        double? P(double valor, int minutos) => AnalisarPartidaHandler.PercentilDoJogo("desarmes90", temporada, valor,
            new Scout.Application.Analise.LinhaAnalise { Minutos = minutos });

        Assert.InRange(P(50, 90)!.Value, 98, 99);   // muito acima de todos: teto em 99, nunca acima de 100
        Assert.InRange(P(0, 90)!.Value, 1, 30);
        Assert.True(P(4, 90) > P(2, 90));           // mais é melhor
        // O mesmo "por 90" num jogo de 20 minutos é menos confiável: fica mais perto do meio
        Assert.True(P(6, 20) < P(6, 90));
        Assert.Null(AnalisarPartidaHandler.PercentilDoJogo("desarmes90", temporada, null, new Scout.Application.Analise.LinhaAnalise { Minutos = 90 }));
    }

    [Fact]
    public void Confronto_direto_conta_do_lado_do_mandante_mesmo_quando_ele_jogou_fora()
    {
        Scout.Domain.Partida J(int m, int v, int gm, int gv, string status = "FT") =>
            new() { MandanteId = m, VisitanteId = v, GolsMandante = gm, GolsVisitante = gv, Status = status, Temporada = 2025 };
        // Flamengo (1) x Galo (2): 1 vence em casa, 1 vence fora, empate, derrota e 0 x 0 decidido nos pênaltis
        var anteriores = new List<Scout.Domain.Partida> { J(1, 2, 2, 0), J(2, 1, 0, 1), J(1, 2, 1, 1), J(2, 1, 3, 1), J(2, 1, 0, 0, "PEN") };

        var c = AnalisarPartidaHandler.Contar(1, anteriores, []);

        Assert.Equal((2, 2, 1), (c.VitoriasMandante, c.Empates, c.VitoriasVisitante));
        Assert.Equal(2025, c.DesdeAno);
    }
}
