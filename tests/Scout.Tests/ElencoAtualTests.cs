using Scout.Application.Consultas;
using Xunit;

namespace Scout.Tests;

public class ElencoAtualTests
{
    const int Galo = 1062, Flu = 124;
    static string Nome(int id) => id == Galo ? "Atletico-MG" : id == Flu ? "Fluminense" : "outro";
    static DateTime D(int mes, int dia) => new(2026, mes, dia);

    [Fact]
    public void Quem_jogou_por_outro_clube_depois_saiu()
    {
        // O caso do Hulk: último jogo pelo Galo em agosto, depois pelo Fluminense
        var v = ElencoAtual.Decidir(1, [Galo], [(Galo, D(8, 10)), (Flu, D(9, 14)), (Flu, D(10, 2))], D(11, 21), Nome);
        Assert.False(v.NoElenco);
        Assert.Equal("Fluminense", v.SaiuPara);
        Assert.Equal("saiu: joga pelo Fluminense desde 09/2026", v.Situacao);
    }

    [Fact]
    public void Sem_jogar_ha_mais_de_60_dias_fica_fora()
    {
        var v = ElencoAtual.Decidir(2, [Galo], [(Galo, D(9, 15))], D(11, 21), Nome);
        Assert.False(v.NoElenco);
        Assert.Null(v.SaiuPara);
        Assert.Contains("desde 15/09/2026", v.Situacao);
    }

    [Fact]
    public void Jogou_recentemente_segue_no_elenco()
    {
        var v = ElencoAtual.Decidir(3, [Galo], [(Galo, D(10, 1)), (Galo, D(11, 21))], D(11, 21), Nome);
        Assert.True(v.NoElenco);
        Assert.Null(v.Situacao);
    }

    [Fact]
    public void A_regua_e_o_ultimo_jogo_do_clube_nao_o_dia_de_hoje()
    {
        // Fim de temporada: o clube parou em junho e o atleta jogou o último jogo; não pode virar "fora do elenco"
        var v = ElencoAtual.Decidir(4, [Galo], [(Galo, D(6, 1))], D(6, 1), Nome);
        Assert.True(v.NoElenco);
    }

    [Fact]
    public void Jogo_por_outro_clube_antes_nao_conta()
    {
        // Chegou do Fluminense no meio do ano e segue jogando pelo Galo
        var v = ElencoAtual.Decidir(5, [Galo], [(Flu, D(3, 1)), (Galo, D(11, 20))], D(11, 21), Nome);
        Assert.True(v.NoElenco);
    }

    [Fact]
    public void Sem_jogo_registrado_pelo_clube_nao_se_sabe_e_fica()
    {
        // Liga importada sem escalação: não dá para dizer que saiu
        var v = ElencoAtual.Decidir(6, [Galo], [], D(11, 21), Nome);
        Assert.True(v.NoElenco);
    }

    [Fact]
    public void Mais_de_um_clube_na_linha_conta_como_o_mesmo()
    {
        // Linha da análise com dois clubes (trocou dentro da liga): jogar pelo segundo não é "saiu"
        var v = ElencoAtual.Decidir(7, [Flu, Galo], [(Flu, D(5, 1)), (Galo, D(11, 20))], D(11, 21), Nome);
        Assert.True(v.NoElenco);
    }
}
