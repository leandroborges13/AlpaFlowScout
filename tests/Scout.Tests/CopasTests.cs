using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Tests;

public class CopasTests
{
    static Partida Jogo(int dia, int competicao, int mandante, int visitante, int gm, int gv) => new()
    {
        Id = dia * 100 + mandante, CompeticaoId = competicao, Temporada = 2026, Data = new DateTime(2026, 3, 1).AddDays(dia),
        Status = "FT", MandanteId = mandante, VisitanteId = visitante, GolsMandante = gm, GolsVisitante = gv,
    };

    [Fact]
    public void Forca_comeca_pela_divisao_e_sobe_com_vitorias()
    {
        var ligas = new Dictionary<int, string> { [71] = "Serie A", [72] = "Serie B" };
        // 1 e 2 são da Série A, 3 da Série B, 4 só joga copa (estrangeiro)
        var jogos = new List<Partida> { Jogo(1, 71, 1, 2, 3, 0), Jogo(2, 72, 3, 5, 0, 0), Jogo(3, 11, 4, 1, 0, 0) };

        var f = ObterCopasDoTimeHandler.Forca(jogos, ligas);

        Assert.True(f[1] > 1600, "vitória por 3 a 0 aumenta a força");
        Assert.True(f[2] < 1600, "derrota diminui");
        Assert.InRange(f[3], 1495, 1505); // empate em casa contra igual quase não mexe
        Assert.True(f[4] > 1550, "estrangeiro começa em 1550 e o empate fora contra time mais forte sobe");
    }

    [Fact]
    public void Forca_soma_zero_entre_os_dois_lados()
    {
        var f = ObterCopasDoTimeHandler.Forca([Jogo(1, 71, 1, 2, 2, 1)], new Dictionary<int, string> { [71] = "Serie A" });
        Assert.Equal(3200, f[1] + f[2], 6);
    }
}
