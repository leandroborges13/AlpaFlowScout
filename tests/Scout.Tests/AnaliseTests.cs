using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Tests;

public class AnaliseTests
{
    static LinhaAnalise Defensor(int id, int desarmes, int interceptacoes, int ganhos, int faltas, int minutos = 900) => new()
    {
        AtletaId = id, Nome = $"Atleta Teste {id}", Idade = 22, Setor = Setor.Defesa, Minutos = minutos,
        Desarmes = desarmes, Interceptacoes = interceptacoes, Duelos = 60, DuelosGanhos = ganhos, FaltasCometidas = faltas,
        ParticipacaoMinutos = 0.8,
    };

    [Fact]
    public void Percentil_conta_empates_pela_metade()
    {
        Assert.Equal(0, MotorDeAnalise.Percentil([1, 2, 3], 1));
        Assert.Equal(100, MotorDeAnalise.Percentil([1, 2, 3], 3));
        Assert.Equal(50, MotorDeAnalise.Percentil([1, 2, 2, 3], 2));
        Assert.Null(MotorDeAnalise.Percentil([1], 1));
    }

    [Fact]
    public void Melhor_defensor_tem_nota_maior_e_faltas_contam_ao_contrario()
    {
        var linhas = new List<LinhaAnalise>
        {
            Defensor(1, desarmes: 30, interceptacoes: 15, ganhos: 45, faltas: 2),
            Defensor(2, desarmes: 10, interceptacoes: 5, ganhos: 30, faltas: 8),
            Defensor(3, desarmes: 2, interceptacoes: 1, ganhos: 15, faltas: 20),
        };
        MotorDeAnalise.CalcularPercentis(linhas);

        Assert.Equal(100, linhas[0].Percentis["faltasCometidas90"]);
        Assert.Equal(0, linhas[2].Percentis["faltasCometidas90"]);
        Assert.True(linhas[0].Nota > linhas[1].Nota);
        Assert.True(linhas[1].Nota > linhas[2].Nota);
    }

    [Fact]
    public void Semelhanca_e_maior_entre_perfis_parecidos()
    {
        var linhas = new List<LinhaAnalise>
        {
            Defensor(1, 30, 15, 45, 2),
            Defensor(2, 29, 14, 44, 3),
            Defensor(3, 2, 1, 15, 20),
            Defensor(4, 15, 8, 30, 10),
        };
        MotorDeAnalise.CalcularPercentis(linhas);
        var chaves = Catalogo.IndicadoresDoSetor(Setor.Defesa);

        Assert.True(AtletasParecidosHandler.Semelhanca(linhas[0], linhas[1], chaves) >
                    AtletasParecidosHandler.Semelhanca(linhas[0], linhas[2], chaves));
    }

    [Fact]
    public void Resumo_nao_expoe_estatisticas_brutas()
    {
        var linhas = new List<LinhaAnalise> { Defensor(1, 30, 15, 45, 2), Defensor(2, 10, 5, 30, 8) };
        MotorDeAnalise.CalcularPercentis(linhas);
        var nomes = typeof(ResumoAtleta).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("Desarmes", nomes);
        Assert.DoesNotContain("Duelos", nomes);
        Assert.Contains("Destaques", nomes);
        Assert.NotEmpty(Resumir.De(linhas[0]).Destaques);
    }

    [Fact]
    public void Idade_de_referencia_e_o_fim_da_temporada_encerrada()
    {
        Assert.Equal(new DateOnly(2024, 12, 31), MotorDeAnalise.ReferenciaDa(2024, new DateOnly(2026, 10, 9)));
        Assert.Equal(new DateOnly(2026, 10, 9), MotorDeAnalise.ReferenciaDa(2026, new DateOnly(2026, 10, 9)));
    }
}
