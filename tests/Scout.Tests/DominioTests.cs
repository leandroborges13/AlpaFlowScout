using Scout.Domain;

namespace Scout.Tests;

public class AtletaTests
{
    static readonly DateOnly Referencia = new(2026, 10, 9);

    [Theory]
    [InlineData("2008-10-09", 18)]
    [InlineData("2008-10-10", 17)]
    [InlineData("2000-02-29", 26)]
    public void Idade_considera_se_o_aniversario_ja_passou(string nascimento, int esperada) =>
        Assert.Equal(esperada, new Atleta { Nascimento = DateOnly.Parse(nascimento) }.IdadeEm(Referencia));

    [Fact]
    public void Menor_de_idade_e_atleta_sem_nascimento_nao_sao_analisados()
    {
        Assert.False(new Atleta { Nascimento = new DateOnly(2008, 10, 10) }.PodeSerAnalisado(Referencia));
        Assert.False(new Atleta { Nascimento = null }.PodeSerAnalisado(Referencia));
        Assert.True(new Atleta { Nascimento = new DateOnly(2008, 10, 9) }.PodeSerAnalisado(Referencia));
    }
}

public class FuncaoTaticaTests
{
    [Theory]
    // Coritiba x Brusque (Série B 2024): Jamerson, lateral-esquerdo, aparece em 2:1
    [InlineData("4-2-3-1", "2:1", Setor.Defesa, Lado.Esquerda, "Lateral esquerdo")]
    [InlineData("4-2-3-1", "2:2", Setor.Defesa, Lado.Esquerda, "Zagueiro esquerdo")]
    [InlineData("4-2-3-1", "2:3", Setor.Defesa, Lado.Direita, "Zagueiro direito")]
    [InlineData("4-2-3-1", "2:4", Setor.Defesa, Lado.Direita, "Lateral direito")]
    [InlineData("4-2-3-1", "3:1", Setor.Meio, Lado.Esquerda, "Volante")]
    [InlineData("4-2-3-1", "4:1", Setor.Meio, Lado.Esquerda, "Meia esquerdo")]
    [InlineData("4-2-3-1", "4:2", Setor.Meio, Lado.Centro, "Meia central")]
    [InlineData("4-2-3-1", "5:1", Setor.Ataque, Lado.Centro, "Centroavante")]
    [InlineData("3-4-3", "2:1", Setor.Defesa, Lado.Esquerda, "Zagueiro esquerdo")]
    [InlineData("3-4-3", "2:2", Setor.Defesa, Lado.Centro, "Zagueiro central")]
    [InlineData("3-4-3", "3:1", Setor.Meio, Lado.Esquerda, "Ala esquerdo")]
    [InlineData("3-4-3", "4:1", Setor.Ataque, Lado.Esquerda, "Ponta esquerda")]
    [InlineData("3-4-3", "4:2", Setor.Ataque, Lado.Centro, "Centroavante")]
    [InlineData("5-3-2", "2:1", Setor.Defesa, Lado.Esquerda, "Ala esquerdo")]
    [InlineData("4-4-2", "4:2", Setor.Ataque, Lado.Direita, "Atacante direito")]
    public void Identifica_a_funcao_pela_grade(string esquema, string grade, Setor setor, Lado lado, string funcao) =>
        Assert.Equal(new PosicaoTatica(setor, lado, funcao), FuncaoTatica.Identificar(esquema, grade));

    [Theory]
    [InlineData("4-3-3", "1:1")]
    public void Linha_um_e_goleiro(string esquema, string grade) =>
        Assert.Equal("Goleiro", FuncaoTatica.Identificar(esquema, grade)!.Funcao);

    [Theory]
    [InlineData(null, "2:1")]
    [InlineData("4-3-3", null)]
    [InlineData("4-3-3", "2:9")]
    [InlineData("4-3-3", "7:1")]
    [InlineData("abc", "2:1")]
    public void Dados_invalidos_nao_geram_funcao(string? esquema, string? grade) =>
        Assert.Null(FuncaoTatica.Identificar(esquema, grade));
}
