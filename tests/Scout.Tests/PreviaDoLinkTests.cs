using Scout.Application.Consultas;

namespace Scout.Tests;

/// <summary>Prévia do link público (/jogo/&lt;id&gt;): título e descrição que o WhatsApp e o X mostram.</summary>
public class PreviaDoLinkTests
{
    static ResumoPartida Jogo(int? gm, int? gv) => new(1, new DateTime(2026, 12, 2), "Regular Season - 38", 71, "Serie A", 2026, gm is null ? "NS" : "FT",
        118, "Clube Fictício", null, 1062, "Atletico-MG", null, gm, gv);

    [Fact]
    public void Antes_do_jogo_mostra_o_palpite_com_o_Galo()
    {
        var p = new Palpite(Jogo(null, null), 44.6, 27.2, 28.3, "mandante", "1 x 1", 12.8, DateTime.UtcNow, false, null, null, null);
        var (titulo, descricao) = PreviaDoLink.De(new PaginaPublica(p.Partida, null, [], [], [], p, ""));
        Assert.Equal("Clube Fictício x Galo: o palpite do modelo | Galo na Lupa", titulo);
        Assert.Equal("O modelo dá Clube Fictício 45%, empate 27%, Galo 28%. Placar mais provável: 1 x 1.", descricao);
    }

    [Fact]
    public void Depois_do_jogo_mostra_placar_gols_e_se_o_modelo_acertou()
    {
        var p = new Palpite(Jogo(1, 2), 44.6, 27.2, 28.3, "mandante", "1 x 1", 12.8, DateTime.UtcNow, true, "visitante", false, false);
        var gols = new List<GolNaPagina> { new("10'", "Clube Fictício", "Atleta Teste Um", null, null), new("90+2'", "Atletico-MG", "Atleta Teste Dois", null, null) };
        var (titulo, descricao) = PreviaDoLink.De(new PaginaPublica(p.Partida, null, gols, [], [], p, ""));
        Assert.Equal("Clube Fictício 1 x 2 Galo: os números do jogo | Galo na Lupa", titulo);
        Assert.StartsWith("Gols: Atleta Teste Um 10', Atleta Teste Dois 90+2'. O modelo errou.", descricao);
    }
}
