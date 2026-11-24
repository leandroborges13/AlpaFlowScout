using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Domain;
using Xunit;

namespace Scout.Tests;

public class LadoALadoTests
{
    static LinhaAnalise Atacante(int gols, int noAlvo, int finalizacoes) =>
        new() { Setor = Setor.Ataque, Minutos = 900, Gols = gols, FinalizacoesNoAlvo = noAlvo, Finalizacoes = finalizacoes };

    [Fact]
    public void Linhas_do_setor_do_candidato_com_quem_vence_cada_uma()
    {
        var candidato = Atacante(gols: 10, noAlvo: 20, finalizacoes: 40);
        var atual = Atacante(gols: 3, noAlvo: 12, finalizacoes: 20);
        var linhas = ObterLadoALadoHandler.Comparar(candidato, atual);

        Assert.Equal(ObterLadoALadoHandler.Chaves(Setor.Ataque), linhas.Select(l => l.Chave));
        var gols = linhas.Single(l => l.Chave == "gols90");
        Assert.Equal(1.0, gols.ValorA);
        Assert.Equal(0.3, gols.ValorB);
        Assert.Equal("a", gols.Vence);
        // 50% no alvo contra 60%: o atual vence nessa
        Assert.Equal("b", linhas.Single(l => l.Chave == "aproveitamentoFinalizacoes").Vence);
    }

    [Fact]
    public void Sem_o_numero_de_um_dos_lados_ninguem_vence()
    {
        // menos de 10 finalizações: sem % no alvo
        var linhas = ObterLadoALadoHandler.Comparar(Atacante(5, 4, 8), Atacante(3, 12, 20));
        var aproveitamento = linhas.Single(l => l.Chave == "aproveitamentoFinalizacoes");
        Assert.Null(aproveitamento.ValorA);
        Assert.Null(aproveitamento.Vence);
    }

    [Fact]
    public void Quando_menos_e_melhor_o_menor_vence()
    {
        var a = new LinhaAnalise { Setor = Setor.Defesa, Minutos = 900, FaltasCometidas = 9 };
        var b = new LinhaAnalise { Setor = Setor.Defesa, Minutos = 900, FaltasCometidas = 18 };
        Assert.Equal("a", ObterLadoALadoHandler.Comparar(a, b).Single(l => l.Chave == "faltasCometidas90").Vence);
    }
}
