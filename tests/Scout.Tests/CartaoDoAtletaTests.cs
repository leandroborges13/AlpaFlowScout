using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Tests;

public class CartaoDoAtletaTests
{
    static LinhaAnalise Atacante(int id, int gols, int faltas) => new()
    {
        AtletaId = id, Nome = $"Atleta Teste {id}", Clube = "Clube Teste", Competicao = "Liga Teste", Setor = Setor.Ataque,
        Minutos = 900, Jogos = 10, Gols = gols, FaltasCometidas = faltas, Finalizacoes = 20, FinalizacoesNoAlvo = 10,
    };

    [Fact]
    public void Cartao_traz_ranking_no_setor_e_inverte_quando_menos_e_melhor()
    {
        var setor = new List<LinhaAnalise> { Atacante(1, 8, 10), Atacante(2, 4, 30), Atacante(3, 6, 20), Atacante(4, 6, 5) };
        MotorDeAnalise.CalcularPercentis(setor);

        var cartao = ObterCartaoDoAtletaHandler.Montar(setor[2], setor);

        Assert.Equal("Ataque", cartao.Blocos[0].Nome);
        Assert.Equal(4, cartao.Comparados);
        var gols = cartao.Blocos.SelectMany(b => b.Itens).Single(i => i.Chave == "gols90");
        Assert.Equal(0.6, gols.Valor);
        Assert.Equal(2, gols.Posicao); // empatado com o 4: os dois são 2º
        Assert.Equal(4, gols.Total);
        var faltas = cartao.Blocos.SelectMany(b => b.Itens).Single(i => i.Chave == "faltasCometidas90");
        Assert.Equal(3, faltas.Posicao); // 20 faltas: só 5 e 10 são melhores
        Assert.True(cartao.Blocos.SelectMany(b => b.Itens).Single(i => i.Chave == "aproveitamentoFinalizacoes").Porcentagem);
    }

    [Fact]
    public void Goleiro_tem_bloco_do_gol_primeiro()
    {
        Assert.Equal("Gol", ObterCartaoDoAtletaHandler.Blocos(Setor.Goleiro)[0].Nome);
        Assert.Equal("Defesa", ObterCartaoDoAtletaHandler.Blocos(Setor.Defesa)[0].Nome);
    }
}
