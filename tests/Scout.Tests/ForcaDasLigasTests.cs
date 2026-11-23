using Scout.Application.Analise;

namespace Scout.Tests;

public class ForcaDasLigasTests
{
    [Fact]
    public void Estimar_recupera_a_diferenca_entre_ligas_e_o_mando()
    {
        // Liga 2 é 0,5 gol/jogo mais fraca que a referência (1); mando vale 0,4. Jogos nos dois sentidos, sem ruído.
        var jogos = new List<(int, int, double)>();
        for (int i = 0; i < 200; i++)
        {
            jogos.Add((1, 2, 0.4 + 0.5));
            jogos.Add((2, 1, 0.4 - 0.5));
        }
        var r = ForcaDasLigas.Estimar(jogos, 1, prior: 0.01);
        Assert.Equal(0, r[1].Gols);
        Assert.Equal(-0.5, r[2].Gols, 2);
        Assert.Equal(400, r[2].Jogos);
    }

    [Fact]
    public void Estimar_puxa_para_zero_quem_tem_poucos_jogos()
    {
        var jogos = new List<(int, int, double)> { (1, 3, 2), (3, 1, -1) }; // dois jogos só: liga 3 parece muito fraca
        var r = ForcaDasLigas.Estimar(jogos, 1, prior: 4);
        Assert.InRange(r[3].Gols, -0.5, 0); // o prior segura a estimativa
    }

    [Fact]
    public void Converter_mantem_a_posicao_sem_deslocamento_e_rebaixa_liga_mais_fraca()
    {
        var liga = Enumerable.Range(0, 101).Select(i => (double)i).ToList();      // notas 0..100
        var referencia = Enumerable.Range(0, 101).Select(i => i * 0.8).ToList();   // escala 0..80
        Assert.Equal(64, NotaNaReferencia.Converter(80, liga, referencia, 0)!.Value, 0); // 80º percentil vira o 80º da referência
        var rebaixada = NotaNaReferencia.Converter(80, liga, referencia, -0.5)!.Value;
        Assert.True(rebaixada < 64);
        Assert.True(NotaNaReferencia.Converter(90, liga, referencia, -0.5) > rebaixada); // continua em ordem
    }

    [Fact]
    public void Converter_sem_amostra_suficiente_devolve_nulo() =>
        Assert.Null(NotaNaReferencia.Converter(50, [10, 20, 30], [10, 20, 30, 40, 50, 60], 0));

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(0.01)]
    public void Normal_inversa_e_acumulada_se_desfazem(double p) =>
        Assert.Equal(p, Normal.Cdf(Normal.InvCdf(p)), 3);
}
