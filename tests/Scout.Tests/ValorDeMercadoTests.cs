using Scout.Application.Consultas;
using Scout.Domain;
using Xunit;

namespace Scout.Tests;

public class ValorDeMercadoTests
{
    [Fact]
    public void Teto_e_o_percentil_90_do_setor_na_Serie_A()
    {
        // 20 atacantes de € 1 mi a € 20 mi: o 18º (90%) vale € 18 mi
        var valores = Enumerable.Range(1, 20).Select(i => (Setor.Ataque, (long?)(i * 1_000_000L)))
            .Append((Setor.Ataque, null)) // sem valor: não entra na conta
            .Concat(Enumerable.Range(1, 5).Select(i => (Setor.Goleiro, (long?)(i * 1_000_000L)))); // só 5 goleiros: sem teto
        var tetos = BuscarPorPerfilHandler.Tetos(valores);
        Assert.Equal(18_000_000, tetos[Setor.Ataque]);
        Assert.False(tetos.ContainsKey(Setor.Goleiro));
    }

    [Theory]
    [InlineData(50_000_000L, false)] // um Endrick: fora da realidade
    [InlineData(12_000_000L, true)]
    [InlineData(null, true)]         // sem valor conhecido: entra (sem o número)
    public void Cabe_no_bolso_pelo_teto_da_Serie_A(long? valor, bool cabe)
    {
        var tetos = new Dictionary<Setor, long> { [Setor.Ataque] = 15_000_000 };
        Assert.Equal(cabe, BuscarPorPerfilHandler.CabeNoBolso(valor, Setor.Ataque, null, tetos));
    }

    [Fact]
    public void Teto_pedido_vale_junto_com_o_da_Serie_A()
    {
        var tetos = new Dictionary<Setor, long> { [Setor.Ataque] = 15_000_000 };
        Assert.False(BuscarPorPerfilHandler.CabeNoBolso(8_000_000, Setor.Ataque, 5_000_000, tetos));
        Assert.True(BuscarPorPerfilHandler.CabeNoBolso(4_000_000, Setor.Ataque, 5_000_000, tetos));
    }

    [Fact]
    public void Nota_de_centroavante_pesa_o_gol()
    {
        // O "Passerini": cria muito (assistências no topo) mas faz pouco gol; o artilheiro faz gol e cria pouco
        var criador = new Scout.Application.Analise.LinhaAnalise();
        criador.Percentis["gols90"] = 40; criador.Percentis["finalizacoesNoAlvo90"] = 60; criador.Percentis["assistencias90"] = 97;
        var artilheiro = new Scout.Application.Analise.LinhaAnalise();
        artilheiro.Percentis["gols90"] = 95; artilheiro.Percentis["finalizacoesNoAlvo90"] = 85; artilheiro.Percentis["assistencias90"] = 40;
        Assert.True(BuscarPorPerfilHandler.NotaDeCentroavante(artilheiro) > BuscarPorPerfilHandler.NotaDeCentroavante(criador));
        Assert.Equal(0.6 * 95 + 0.2 * 85 + 0.2 * 40, BuscarPorPerfilHandler.NotaDeCentroavante(artilheiro)!.Value, 6);
    }

    [Theory]
    [InlineData("Centroavante", Setor.Ataque)]
    [InlineData("Ponta esquerda", Setor.Ataque)]
    [InlineData("Lateral direito", Setor.Defesa)]
    [InlineData("Ala esquerdo", Setor.Defesa)]
    [InlineData("Volante", Setor.Meio)]
    [InlineData("Meio-campista pela esquerda", Setor.Meio)]
    [InlineData("Goleiro", Setor.Goleiro)]
    public void O_teto_segue_o_setor_da_funcao_pedida(string funcao, Setor setor) =>
        Assert.Equal(setor, BuscarPorPerfilHandler.SetorDaFuncao(funcao));

    [Theory]
    [InlineData(5_700_000L, "€ 5,7 mi")]
    [InlineData(620_000L, "€ 620 mil")]
    public void Valor_por_extenso(long valor, string texto) =>
        Assert.Equal(texto, BuscarPorPerfilHandler.Euros(valor));
}
