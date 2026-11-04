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

    [Fact]
    public void Vitoria_por_mais_de_dois_gols_e_diferenca_no_placar_do_ponto_de_vista_do_time()
    {
        // "Vitórias por mais de dois gols" = resultado vitória e diferença mínima 3
        Assert.True(ProcurarPartidasHandler.AtendeResultado(true, 3, 0, "vitoria", 3));    // 3 x 0 em casa
        Assert.True(ProcurarPartidasHandler.AtendeResultado(false, 1, 4, "Vitória", 3));   // 1 x 4 fora
        Assert.False(ProcurarPartidasHandler.AtendeResultado(true, 3, 2, "vitoria", 3));   // 5 gols no jogo, mas 1 de diferença
        Assert.False(ProcurarPartidasHandler.AtendeResultado(false, 4, 1, "vitoria", 3));  // goleada sofrida não é vitória
        Assert.True(ProcurarPartidasHandler.AtendeResultado(false, 4, 1, "derrota", 3));
        Assert.True(ProcurarPartidasHandler.AtendeResultado(true, 2, 2, "empate", null));
        Assert.False(ProcurarPartidasHandler.AtendeResultado(true, null, null, "vitoria", null)); // jogo sem placar
    }

    [Fact]
    public void Roteiro_do_pos_jogo_recebe_o_placar_gol_a_gol_com_acrescimo_e_o_proximo_jogo_de_cada_time()
    {
        var data = new DateTime(2026, 10, 11, 21, 0, 0, DateTimeKind.Utc);
        ResumoPartida Jogo(int id, DateTime d, int casa, string nomeCasa, int fora, string nomeFora) =>
            new(id, d, "Regular Season - 30", 71, "Serie A", 2026, "FT", casa, nomeCasa, null, fora, nomeFora, null, 2, 1);
        var lado = (int id, string nome) => new LadoDaPartida(id, nome, "4-3-3", null, null, [], []);
        var a = new AnalisePartida(Jogo(1, data, 10, "Time Casa", 20, "Time Fora"), lado(10, "Time Casa"), lado(20, "Time Fora"),
            [
                // fora de ordem de propósito; o gol contra do visitante conta para o mandante
                new EventoDaPartida(90, 7, 10, "Gol", "Atleta C", null, null),
                new EventoDaPartida(88, null, 20, "Gol", "Atleta A", null, null),
                new EventoDaPartida(90, 1, 20, "Gol contra", "Atleta B", null, null),
            ], [], [],
            JogosSeguintes: [Jogo(2, data.AddDays(3), 10, "Time Casa", 30, "Outro"), Jogo(3, data.AddDays(7), 40, "Mais Um", 20, "Time Fora")]);

        var dados = JsonDocument.Parse(Scout.Application.Chat.RoteiristaPosJogo.Dados(a)).RootElement;

        var gols = dados.GetProperty("Gols").EnumerateArray().Select(g => $"{g.GetProperty("Minuto").GetString()} {g.GetProperty("PlacarDepois").GetString()}");
        Assert.Equal(["88 0 x 1", "90+1 1 x 1", "90+7 2 x 1"], gols);
        var proximo = dados.GetProperty("ProximoJogo");
        Assert.StartsWith("Time Casa x Outro", proximo.GetProperty("Time Casa").GetString());
        Assert.StartsWith("Mais Um x Time Fora", proximo.GetProperty("Time Fora").GetString());
        Assert.Contains("domingo", dados.GetProperty("Data").GetString()); // 11/10/2026 foi domingo
    }

    [Fact]
    public void Times_do_pos_jogo_automatico_vem_da_configuracao()
    {
        Assert.Equal([1062, 118], Scout.Infrastructure.Provedor.AcompanhamentoPosJogo.Times(" 1062, 118 ,x,"));
        Assert.Empty(Scout.Infrastructure.Provedor.AcompanhamentoPosJogo.Times(null));
    }

    [Fact]
    public void Jogo_da_PitchAPI_e_achado_pelo_horario_e_pelo_nome_sem_acento()
    {
        var nosso = new ResumoPartida(1, new DateTime(2026, 10, 29, 22, 30, 0, DateTimeKind.Utc), null, 71, "Serie A", 2026, "FT",
            1062, "Atletico-MG", null, 135, "Cruzeiro", null, 0, 1);
        var jogos = JsonDocument.Parse("""
            [{"id":"m_outro","time_utc":"2026-10-29T22:30:00Z","home_team":{"id":"t_x","name":"Flamengo"},"away_team":{"id":"t_y","name":"Palmeiras"}},
             {"id":"m_antes","time_utc":"2026-10-25T22:30:00Z","home_team":{"id":"t_3K8ET0","name":"Atlético-MG"},"away_team":{"id":"t_z","name":"Vasco"}},
             {"id":"m_29YTpa","time_utc":"2026-10-29T22:30:00Z","home_team":{"id":"t_3K8ET0","name":"Atlético-MG"},"away_team":{"id":"t_3npCjp","name":"Cruzeiro"}}]
            """).RootElement;

        var achado = Scout.Infrastructure.Provedor.PitchApi.Achar(jogos, nosso);

        Assert.Equal(("m_29YTpa", "t_3K8ET0", "t_3npCjp"), achado);
        Assert.Equal("atleticomg", Scout.Infrastructure.Provedor.PitchApi.Nome("Atlético-MG"));
        Assert.Null(Scout.Infrastructure.Provedor.PitchApi.Achar(jogos, nosso with { Data = nosso.Data.AddDays(1) }));
        // como a API devolve: 19:30 no horário de Brasília (sem fuso) é o mesmo jogo das 22:30 UTC
        var deBrasilia = nosso with { Data = new DateTime(2026, 10, 29, 19, 30, 0, DateTimeKind.Unspecified) };
        Assert.Equal("m_29YTpa", Scout.Infrastructure.Provedor.PitchApi.Achar(jogos, deBrasilia)?.Id);
        // e o jogo do outro time no mesmo horário não confunde: só serve se o nome bater
        Assert.Null(Scout.Infrastructure.Provedor.PitchApi.Achar(jogos, deBrasilia with { Mandante = "Bahia", Visitante = "Vitoria" }));
    }

    [Fact]
    public void Percentil_do_aspecto_vai_para_o_roteiro_ja_como_frase()
    {
        Assert.Equal("melhor que em 79% dos próprios jogos da temporada", Scout.Application.Chat.RoteiristaPosJogo.Leitura(79));
        Assert.Equal("pior que em 89% dos próprios jogos da temporada", Scout.Application.Chat.RoteiristaPosJogo.Leitura(11));
        Assert.Null(Scout.Application.Chat.RoteiristaPosJogo.Leitura(null));
    }

    [Fact]
    public void Frase_do_mapa_entra_na_fala_quando_a_IA_esquece()
    {
        var p = new ResumoPartida(1, DateTime.UtcNow, null, 71, "Serie A", 2026, "FT", 10, "Galo", null, 20, "Cruzeiro", null, 0, 1);
        FinalizacaoNoMapa F(int clube, bool area, bool alvo) => new(clube, "X", 10, 90, 34, 0.1, alvo, false, area, null);
        var mapa = new MapaDaPartida([F(10, true, true), F(10, false, false), F(20, true, false)], [], "PitchAPI");
        var frase = Scout.Application.Chat.RoteiristaPosJogo.FraseDoMapa(p, mapa);
        Assert.Equal("O Galo finalizou 2 vezes, 1 de dentro da área e 1 no alvo; o Cruzeiro, 1 vez, 1 da área e 0 no alvo. Olha no mapa.", frase);

        var semFrase = "TÍTULO: t\n\nFALA: Perdemos. Me segue que no próximo jogo tem mais.\n\nCARTÕES:\nplacar";
        var com = Scout.Application.Chat.RoteiristaPosJogo.ComFraseDoMapa(semFrase, frase);
        Assert.Contains("Perdemos. O Galo finalizou 2 vezes", com);
        Assert.EndsWith("Olha no mapa. Me segue que no próximo jogo tem mais.\n\nCARTÕES:\nplacar", com);
        // se a IA já citou, não repete
        var jaTem = "FALA: O Galo finalizou 2 vezes e perdeu. Me segue que no próximo jogo tem mais.\n\nCARTÕES:";
        Assert.Equal(jaTem, Scout.Application.Chat.RoteiristaPosJogo.ComFraseDoMapa(jaTem, frase));
    }
}
