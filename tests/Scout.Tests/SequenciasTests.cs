using Scout.Application.Consultas;

namespace Scout.Tests;

public class SequenciasTests
{
    static int dia;
    static JogoDoTimeNaSequencia J(int pro, int contra, bool casa = true) =>
        new(++dia, new DateTime(2026, 9, 1, 22, 0, 0, DateTimeKind.Utc).AddDays(dia * 4), "Galo", $"Rival {dia}", casa, pro, contra, "Serie A");

    [Fact]
    public void Conta_a_sequencia_atual_e_o_jogo_que_quebrou_a_anterior()
    {
        dia = 0;
        var jogos = new List<JogoDoTimeNaSequencia> { J(2, 0), J(0, 1, false), J(1, 1), J(3, 0), J(2, 1, false), J(1, 0) };
        var r = ObterSequenciasHandler.Calcular("Galo", "temporada", jogos);

        var invicto = r.Atuais.Single(s => s.Tipo == "invicto");
        Assert.Equal(4, invicto.Jogos);
        Assert.Equal(3, invicto.Desde!.Id);           // começou no 1 x 1
        Assert.Equal(2, invicto.QuebradaEm!.Id);       // depois da derrota fora
        Assert.Equal("Rival 2 1 x 0 Galo", invicto.QuebradaEm.Jogo);
        Assert.Equal(3, r.Atuais.Single(s => s.Tipo == "vitorias").Jogos);
        Assert.Equal(0, r.Atuais.Single(s => s.Tipo == "sem_vencer").Jogos);
        Assert.Equal(6, r.Atuais.Single(s => s.Tipo == "sem_vencer").QuebradaEm!.Id); // a vitória mais recente
        Assert.Equal(3, r.MaiorVitoria!.Saldo);
        Assert.Equal(1, r.MaiorDerrota!.Saldo);
    }

    [Fact]
    public void Recorde_fica_com_a_maior_e_separa_casa_e_fora()
    {
        dia = 0;
        var jogos = new List<JogoDoTimeNaSequencia> { J(1, 0), J(2, 0), J(1, 0, false), J(0, 2), J(1, 0), J(0, 0, false) };
        var r = ObterSequenciasHandler.Calcular("Galo", "temporada", jogos);

        var vitorias = r.Recordes.Single(s => s.Tipo == "vitorias");
        Assert.Equal(3, vitorias.Jogos);
        Assert.Equal(1, vitorias.Desde!.Id);
        Assert.Equal(4, vitorias.QuebradaEm!.Id);
        Assert.Equal(3, r.Recordes.Single(s => s.Tipo == "sem_sofrer").Jogos);
        Assert.Equal(1, r.EmCasa.Single(s => s.Tipo == "invicto").Jogos); // em casa a vitória veio logo depois de uma derrota
    }
}
