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

public class PresencaTests
{
    static JogoNaLinhaDoTempo Jogo(bool titular) => new(DateTime.Today, null, "Clube Fictício", true, null, titular, titular ? "Volante" : null);

    [Fact]
    public void Calcula_titularidade_e_sequencias()
    {
        var jogos = new[] { true, true, true, false, true, true }.Select(Jogo).ToList();
        var p = ObterAtletaHandler.CalcularPresenca(jogos);
        Assert.Equal(new Presenca(JogosDoClube: 6, ComoTitular: 5, SequenciaAtual: 2, MaiorSequencia: 3), p);
    }

    [Fact]
    public void Sem_jogos_tudo_zero() =>
        Assert.Equal(new Presenca(0, 0, 0, 0), ObterAtletaHandler.CalcularPresenca([]));
}

public class TrajetoriaTests
{
    [Fact]
    public void Clube_sem_temporada_usa_o_ano_das_transferencias()
    {
        var clubes = new List<ClubeNaCarreira>
        {
            new("Clube Fictício A", null, [2025, 2026]),
            new("Clube Fictício B", null, []),
        };
        var transferencias = new List<Transferencia>
        {
            new(new DateOnly(2021, 1, 10), "Clube Fictício B", "Clube Fictício C", "Free"),
            new(new DateOnly(2019, 7, 1), "Clube Fictício D", "Clube Fictício B", "N/A"),
        };
        var r = Scout.Infrastructure.Provedor.HistoricoDoAtleta.CompletarAnos(clubes, transferencias);
        Assert.Equal([2021, 2019], r.Single(c => c.Clube == "Clube Fictício B").Temporadas);
        Assert.Equal("Clube Fictício A", r[0].Clube);
    }
}

public class AtualizacaoDiariaTests
{
    [Fact]
    public void Le_a_lista_de_competicoes_e_ignora_itens_invalidos() =>
        Assert.Equal([(71, 2026), (72, 2026), (75, 2025)],
            Scout.Infrastructure.Provedor.AtualizacaoDiaria.Competicoes(" 71:2026, 72:2026,abc, 75:2025 ,9"));

    [Fact]
    public void Sem_configuracao_nao_atualiza_nada() =>
        Assert.Empty(Scout.Infrastructure.Provedor.AtualizacaoDiaria.Competicoes(null));
}
