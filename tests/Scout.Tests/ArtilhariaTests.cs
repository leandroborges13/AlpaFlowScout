using Scout.Application.Consultas;

namespace Scout.Tests;

public class ArtilhariaTests
{
    static GolsDoAtleta Atleta(int id, string nome, int jogos, params (string, int, int)[] porCompeticao) =>
        new(id, nome, 26, ["Clube"], porCompeticao, jogos, jogos * 80, 0);

    [Fact]
    public void Ordena_por_gols_somando_as_competicoes_e_empate_divide_a_posicao()
    {
        var atletas = new[]
        {
            Atleta(1, "Encaixe alto, poucos gols", 24, ("Serie A", 10, 1)),
            Atleta(2, "Artilheiro", 30, ("Serie A", 21, 2)),
            Atleta(3, "Soma de copas", 40, ("Serie A", 12, 0), ("Copa do Brasil", 4, 1)),
            Atleta(4, "Empatado", 31, ("Serie A", 16, 3)),
            Atleta(5, "Sem gols", 20, ("Serie A", 0, 5)),
        };

        var r = ObterArtilhariaHandler.Ordenar(atletas, "gols", 10);

        Assert.Equal(["Artilheiro", "Empatado", "Soma de copas", "Encaixe alto, poucos gols"], r.Select(x => x.Nome));
        Assert.Equal([1, 2, 2, 4], r.Select(x => x.Posicao));
        Assert.Equal([false, true, true, false], r.Select(x => x.Empatado));
        Assert.Contains("Copa do Brasil: 4 gols", r[2].PorCompeticao);
    }

    [Fact]
    public void Assistencias_usa_o_outro_criterio()
    {
        var r = ObterArtilhariaHandler.Ordenar([Atleta(1, "Garçom", 20, ("Serie A", 0, 5)), Atleta(2, "Artilheiro", 30, ("Serie A", 21, 2))], "assistências", 5);
        Assert.Equal(["Garçom", "Artilheiro"], r.Select(x => x.Nome));
    }
}
