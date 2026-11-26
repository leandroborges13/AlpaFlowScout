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

    [Fact]
    public void Outras_competicoes_aparecem_com_nome_limpo()
    {
        Assert.Equal("Copa do Brasil: 1 jogo, 1 gol", ObterCartaoDoAtletaHandler.DescreverOutra("Copa Do Brasil", 1, 1, 0));
        Assert.Equal("Sul-Americana: 2 jogos", ObterCartaoDoAtletaHandler.DescreverOutra("CONMEBOL Sudamericana", 2, 0, 0));
    }

    static ObterCartaoDoAtletaHandler.SomaAvancada Soma(int id, int minutos, double xg, int toques) =>
        new(id, minutos, xg, xg / 2, 10, 20, toques, 5, 30, 40, 3, 4);

    [Fact]
    public void Bloco_avancado_compara_por_90_com_o_mesmo_setor()
    {
        // 6 atacantes com 900 min; o 1 tem o maior xG (9 em 900 min = 0,9 por 90) e o 6 o menor
        var setor = Enumerable.Range(1, 6).Select(i => Soma(i, 900, 10 - i, 60 - i * 5)).ToList();
        var b = ObterCartaoDoAtletaHandler.BlocoAvancado(Setor.Ataque, setor, 1)!;
        Assert.Equal("Avançado (PitchAPI)", b.Nome);
        var xg = b.Itens.Single(i => i.Chave == "xg90");
        Assert.Equal(0.9, xg.Valor);
        Assert.Equal(1, xg.Posicao);
        Assert.Equal(100, xg.Percentil);
        Assert.Equal(6, xg.Total);
        Assert.Equal(["xg90", "xa90", "toquesNaArea90", "conducoesProgressivas90"], b.Itens.Select(i => i.Chave));
    }

    [Fact]
    public void Sem_minutos_na_fonte_ou_goleiro_nao_tem_bloco_avancado()
    {
        var setor = Enumerable.Range(1, 6).Select(i => Soma(i, 900, 5, 20)).Append(Soma(7, 300, 5, 20)).ToList();
        Assert.Null(ObterCartaoDoAtletaHandler.BlocoAvancado(Setor.Ataque, setor, 7));   // 300 min: fora
        Assert.Null(ObterCartaoDoAtletaHandler.BlocoAvancado(Setor.Goleiro, setor, 1));
        Assert.Null(ObterCartaoDoAtletaHandler.BlocoAvancado(Setor.Ataque, setor.Take(3).ToList(), 1));   // régua curta demais
    }
}
