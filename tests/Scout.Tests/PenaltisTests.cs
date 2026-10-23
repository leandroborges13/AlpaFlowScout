using System.Text.Json;
using Scout.Application.Consultas;
using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class PenaltisTests
{
    static PenaltisDoAtleta Atleta(int id, string nome, int conv, int perd, int convDisp = 0, int perdDisp = 0, int def = 0, int sof = 0, int com = 0) =>
        new(id, nome, 25, ["Clube"], ["Serie A"], conv, perd, convDisp, perdDisp, def, sof, com);

    [Fact]
    public void Ranking_de_batedores_exige_minimo_e_desempata_por_convertidos()
    {
        var atletas = new[]
        {
            Atleta(1, "Um de um", 1, 0),          // 100%, mas só 1 cobrança
            Atleta(2, "Nove de dez", 9, 1),       // 90%
            Atleta(3, "Quatro de quatro", 4, 0),  // 100%
            Atleta(4, "Oito de oito", 8, 0),      // 100%, mais convertidos: na frente do 4 de 4
        };
        var r = ObterRankingPenaltisHandler.Ordenar(atletas, "batedores", comDisputas: false, minimoDeCobrancas: 3, limite: 10);
        Assert.Equal(["Oito de oito", "Quatro de quatro", "Nove de dez"], r.Select(x => x.Nome));
        Assert.Equal(90, r[2].AproveitamentoPercentual);
        Assert.Equal(1, r[0].Posicao);
    }

    [Fact]
    public void Empatados_dividem_a_posicao()
    {
        var r = ObterRankingPenaltisHandler.Ordenar([Atleta(1, "A", 7, 0), Atleta(2, "B", 7, 0), Atleta(3, "C", 5, 1)], "batedores", false, 3, 5);
        Assert.Equal([1, 1, 3], r.Select(x => x.Posicao));
        Assert.Equal([true, true, false], r.Select(x => x.Empatado));
    }

    [Fact]
    public void Disputa_de_penaltis_soma_quando_pedida()
    {
        var a = Atleta(1, "Batedor", conv: 2, perd: 0, convDisp: 0, perdDisp: 2);
        var sem = ObterRankingPenaltisHandler.Ordenar([a], "batedores", comDisputas: false, minimoDeCobrancas: 1, limite: 5).Single();
        var com = ObterRankingPenaltisHandler.Ordenar([a], "batedores", comDisputas: true, minimoDeCobrancas: 1, limite: 5).Single();
        Assert.Equal((2, 2, 100), (sem.Convertidos, sem.Cobrados, sem.AproveitamentoPercentual));
        Assert.Equal((2, 4, 50), (com.Convertidos, com.Cobrados, com.AproveitamentoPercentual));
        Assert.Equal("2 de 2", com.NoTempoDeJogo);
        Assert.Equal("0 de 2", com.NaDisputa);
    }

    [Fact]
    public void Goleiros_ordenam_por_defendidos()
    {
        var r = ObterRankingPenaltisHandler.Ordenar([Atleta(1, "A", 0, 0, def: 1), Atleta(2, "B", 0, 0, def: 3), Atleta(3, "C", 0, 0)], "goleiros", true, 3, 5);
        Assert.Equal(["B", "A"], r.Select(x => x.Nome));
    }

    [Fact]
    public void Le_so_as_cobrancas_da_disputa_nos_lances()
    {
        var eventos = JsonDocument.Parse("""
            [
              {"type":"Goal","detail":"Penalty","comments":null,"team":{"id":1},"player":{"id":10,"name":"Gol de pênalti no jogo"}},
              {"type":"Goal","detail":"Penalty","comments":"Penalty Shootout","team":{"id":1},"player":{"id":11,"name":"Converteu"}},
              {"type":"Goal","detail":"Missed Penalty","comments":"Penalty Shootout","team":{"id":2},"player":{"id":20,"name":"Perdeu"}},
              {"type":"Card","detail":"Yellow Card","comments":null,"team":{"id":2},"player":{"id":21,"name":"Cartão"}}
            ]
            """).RootElement.EnumerateArray().ToList();
        var c = Importador.LerDisputa(99, eventos);
        Assert.Equal(2, c.Count);
        Assert.True(c[0].Convertida);
        Assert.Equal((11, 1, 1), (c[0].AtletaId!.Value, c[0].ClubeId, c[0].Ordem));
        Assert.False(c[1].Convertida);
        Assert.Equal(2, c[1].Ordem);
    }
}
