using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Tests;

public class LigaTests
{
    static int _id;
    static Partida Jogo(int rodada, int mandante, int visitante, int? gm, int? gv) => new()
    {
        Id = ++_id, CompeticaoId = 71, Temporada = 2026, Data = new DateTime(2026, 4, 1).AddDays(rodada * 7),
        Status = gm is null ? "NS" : "FT", MandanteId = mandante, VisitanteId = visitante, GolsMandante = gm, GolsVisitante = gv,
    };

    /// <summary>
    /// 20 times, 5 rodadas jogadas: o time 1 venceu todas (15 pontos), os 15 do meio têm 1 vitória e 4 empates (7 pontos)
    /// e os times 17 a 20 perderam todas. Falta 1 rodada.
    /// </summary>
    static List<Partida> Liga()
    {
        var jogos = new List<Partida>();
        for (var r = 0; r < 5; r++)
        {
            var ultimos = Enumerable.Range(0, 4).Select(i => 17 + (r + i) % 4).ToList();
            var vencedores = Enumerable.Range(0, 3).Select(i => 2 + 3 * r + i).ToList(); // cada time do meio vence uma vez
            jogos.Add(Jogo(r, 1, ultimos[0], 2, 0));
            for (var i = 0; i < 3; i++) jogos.Add(Jogo(r, vencedores[i], ultimos[i + 1], 1, 0));
            var empatam = Enumerable.Range(2, 15).Except(vencedores).ToList();
            for (var i = 0; i < empatam.Count; i += 2) jogos.Add(Jogo(r, empatam[i], empatam[i + 1], 1, 1));
        }
        // Última rodada, a jogar
        jogos.Add(Jogo(9, 1, 2, null, null));
        for (var t = 3; t <= 15; t += 2) jogos.Add(Jogo(9, t, t + 1, null, null));
        jogos.Add(Jogo(9, 17, 18, null, null));
        jogos.Add(Jogo(9, 19, 20, null, null));
        return jogos;
    }

    [Fact]
    public void Lider_com_folga_e_campeao_e_lanterna_sem_pontos_cai()
    {
        var forca = Enumerable.Range(1, 20).ToDictionary(c => c, _ => 1600.0);
        var (times, restantes, rebaixados) = ObterChancesNaLigaHandler.Simular(Liga(), forca, [], 42);

        Assert.Equal(4, rebaixados);
        Assert.Equal(10, restantes);
        var lider = times.Single(t => t.ClubeId == 1);
        Assert.Equal(1, lider.Posicao);
        Assert.Equal(15, lider.Pontos);
        Assert.Equal(100, lider.TituloPercentual);
        Assert.Equal(0, lider.RebaixamentoPercentual);
        // Quem tem 5 pontos de vantagem sobre a zona com 1 jogo a fazer não cai
        Assert.All(times.Where(t => t.ClubeId is >= 2 and <= 16), t => Assert.Equal(0, t.RebaixamentoPercentual));
        // Os quatro últimos não alcançam ninguém: caem em todas as simulações
        Assert.All(times.Where(t => t.ClubeId >= 17), t => Assert.Equal(100, t.RebaixamentoPercentual));
    }

    [Fact]
    public void Serie_B_de_2026_sobem_dois_direto_e_dois_pelo_playoff()
    {
        Assert.Equal(new RegraDeAcesso(2, true), RegraDeAcesso.Da("Serie B", 2026));
        Assert.Equal(new RegraDeAcesso(4, false), RegraDeAcesso.Da("Serie B", 2025));
        Assert.Null(RegraDeAcesso.Da("Serie A", 2026));

        var forca = Enumerable.Range(1, 20).ToDictionary(c => c, c => 1500.0 + c * 3);
        var times = ObterChancesNaLigaHandler.Simular(Liga(), forca, [], 3, RegraDeAcesso.Da("Serie B", 2026)).Times;

        var lider = times.Single(t => t.ClubeId == 1);
        Assert.Equal(100, lider.AcessoDiretoPercentual);   // 15 pontos contra no máximo 10 dos outros
        Assert.Equal(0, lider.PlayoffPercentual);
        Assert.InRange(times.Sum(t => t.AcessoPercentual ?? 0), 398, 402);        // 4 sobem por simulação
        Assert.InRange(times.Sum(t => t.AcessoDiretoPercentual ?? 0), 199, 201);  // 2 direto
        Assert.InRange(times.Sum(t => t.PlayoffPercentual ?? 0), 398, 402);       // 4 no playoff
        Assert.All(times.Where(t => t.ClubeId >= 17), t => Assert.Equal(0, t.AcessoPercentual));
        Assert.All(times, t => Assert.True(t.AcessoPercentual <= t.AcessoDiretoPercentual + t.PlayoffPercentual));
    }

    [Fact]
    public void Mesma_semente_da_o_mesmo_resultado_e_as_chances_somam_o_numero_de_vagas()
    {
        var forca = Enumerable.Range(1, 20).ToDictionary(c => c, c => 1500.0 + c * 5);
        var a = ObterChancesNaLigaHandler.Simular(Liga(), forca, [], 7).Times;
        var b = ObterChancesNaLigaHandler.Simular(Liga(), forca, [], 7).Times;

        Assert.Equal(a.Select(t => t.Top6Percentual), b.Select(t => t.Top6Percentual));
        Assert.InRange(a.Sum(t => t.TituloPercentual), 98, 102);   // arredondamento
        Assert.InRange(a.Sum(t => t.Top4Percentual), 396, 404);
        Assert.InRange(a.Sum(t => t.RebaixamentoPercentual ?? 0), 396, 404);
    }
}
