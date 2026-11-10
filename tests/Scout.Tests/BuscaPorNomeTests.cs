using Scout.Application.Consultas;

namespace Scout.Tests;

public class BuscaPorNomeTests
{
    [Fact]
    public void Primeiro_nome_completo_acha_o_cadastro_pela_inicial()
    {
        // "Kauã Pascini" empatava com todo Kauã do banco; a inicial do cadastro e o sobrenome desempatam
        var pascini = BuscaPorNome.PontuarAtleta("Kauã Pascini", "K. Pascini", null);
        var outroKaua = BuscaPorNome.PontuarAtleta("Kauã Pascini", "Kauã", "Kauã Pereira da Rocha");
        Assert.True(pascini > outroKaua);
        Assert.True(BuscaPorNome.PontuarAtleta("Tomás Cuello", "T. Cuello", "Tomás Cuello") > BuscaPorNome.PontuarAtleta("Tomás Cuello", "Tomás Pérez", null));
    }

    [Fact]
    public void Nome_sem_relacao_nao_pontua()
    {
        Assert.Equal(0, BuscaPorNome.PontuarAtleta("Hulk", "K. Pascini", null));
    }
}
