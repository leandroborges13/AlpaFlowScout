using Scout.Application.Chat;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Tests;

/// <summary>"Comentar o jogo" (item 19): os pontos saem com os números da base, do ponto de vista do Galo.</summary>
public class ComentarTests
{
    const int Galo = PontosParaComentar.TimeDoCanal, Rival = 9001;

    static AnalisePartida Jogo()
    {
        var p = new ResumoPartida(1, new DateTime(2026, 11, 25, 23, 0, 0), "Regular Season - 36", 71, "Serie A", 2026, "FT",
            Galo, "Atletico-MG", null, Rival, "Clube Fictício", null, 1, 1);
        var galo = new LadoDaPartida(Galo, "Atletico-MG", "4-2-3-1", null, null, [new(10, "Atleta Galo Um", "Ponta direita", 80, 70)], []);
        // o lateral esquerdo do rival (X = 25: esquerda dele) dá o passe do gol: cruzamento pela direita da defesa do Galo
        var rival = new LadoDaPartida(Rival, "Clube Fictício", "4-2-3-1", null, null,
            [new(20, "Atleta Rival Lateral", "Lateral esquerdo", 25, 18), new(21, "Atleta Rival Atacante", "Atacante", 50, 84)], []);
        var eventos = new List<EventoDaPartida>
        {
            new(66, null, Galo, "Cartão vermelho", "Atleta Galo Dois", null, null),
            new(70, null, Rival, "Gol", "Atleta Rival Atacante", "Atleta Rival Lateral", null, 21, 20),
            new(90, 2, Galo, "Gol", "Atleta Galo Um", null, null, 10),
        };
        var desempenhos = new List<DesempenhoNoJogo>
        {
            new(10, "Atleta Galo Um", Galo, "Ponta direita", "Ataque", 90, 82, 1, 0, 0, 0, []),
            new(11, "Atleta Galo Três", Galo, "Volante", "Meio", 90, 60, 0, 0, 0, 0, []),
            new(20, "Atleta Rival Lateral", Rival, "Lateral esquerdo", "Defesa", 90, 90, 0, 1, 0, 0, []),
        };
        return new AnalisePartida(p, galo, rival, eventos, [], [], Desempenhos: desempenhos);
    }

    static MapaDaPartida Mapa() => new(
    [
        new(Galo, "A. Galo Um", 10, 90, 50, 0.1, true, false, true, null),
        new(Galo, "A. Galo Um", 50, 90, 50, 0.1, true, false, true, null),
        new(Galo, "Atleta Galo Três", 60, 80, 50, 0.05, true, false, false, null),
        new(Galo, "Atleta Galo Três", 61, 80, 50, 0.05, false, false, false, null),
        new(Rival, "Atleta Rival Atacante", 70, 90, 50, 0.3, true, true, true, null),
    ], [], "teste");

    [Fact]
    public void Pontos_do_jogo_do_ponto_de_vista_do_Galo()
    {
        var palpite = new Palpite(Jogo().Partida, 56, 25, 19, "mandante", "1 x 0", 14, DateTime.UtcNow, true, "empate", false, false);
        var pontos = PontosParaComentar.Montar(Jogo(), Galo, Mapa(), null, null, palpite).ToDictionary(p => p.Tema, p => p.Frase);

        Assert.Contains("Galo 1 x 1 Clube Fictício", pontos["placar"]);
        Assert.Contains("O Galo ficou com um a menos desde os 66'", pontos["placar"]);
        Assert.Contains("Atleta Galo Um 90+2'", pontos["placar"]);
        Assert.Equal("O Galo finalizou 4 vezes (2 de dentro da área) e acertou o alvo 3; o Clube Fictício, 1 finalização e 1 no alvo.", pontos["finalizações"]);
        // o melhor é o do Galo (o rival com nota maior não entra) e o nome do mapa ("A. Galo Um") liga pelo sobrenome
        Assert.Equal("O melhor do Galo pela IA foi Atleta Galo Um, nota 82: 1 gol, 2 dos 3 chutes no alvo do Galo.", pontos["destaque"]);
        Assert.Contains("pela direita da defesa do Galo", pontos["gols sofridos"]);
        Assert.Equal("O modelo errou: deu Galo (56%), com 1 x 0 como placar mais provável.", pontos["modelo"]);
    }

    [Theory]
    [InlineData(25.0, "direita")]
    [InlineData(80.0, "esquerda")]
    [InlineData(50.0, null)]
    [InlineData(null, null)]
    public void Lado_da_defesa_e_o_espelho_do_lado_de_quem_passou(double? x, string? esperado) =>
        Assert.Equal(esperado, PontosParaComentar.LadoDaDefesa(x));

    [Fact]
    public void Jogador_do_mapa_liga_pelo_sobrenome_sem_acento()
    {
        Assert.True(PontosParaComentar.MesmoJogador("T. Cuello", "Tomás Cuello"));
        Assert.True(PontosParaComentar.MesmoJogador("Kaio César", "Kaio Cesar"));
        Assert.False(PontosParaComentar.MesmoJogador("T. Cuello", "Bernard"));
    }
}
