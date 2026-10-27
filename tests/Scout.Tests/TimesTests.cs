using System.Text.Json;
using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Domain;
using Scout.Infrastructure.Provedor;

namespace Scout.Tests;

public class TimesTests
{
    static LinhaTime Time(int id, int v, int e, int d, int pro, int contra, double? posse = null, double? cedidas = null, double? faltas = null) => new()
    {
        ClubeId = id, Nome = $"Time Teste {id}", Jogos = v + e + d, Vitorias = v, Empates = e, Derrotas = d,
        GolsPro = pro, GolsContra = contra, Posse = posse, FinalizacoesCedidas = cedidas, Faltas = faltas,
    };

    [Fact]
    public void Classificacao_por_pontos_saldo_e_gols()
    {
        var times = new List<LinhaTime>
        {
            Time(1, v: 5, e: 1, d: 4, pro: 12, contra: 10),
            Time(2, v: 6, e: 0, d: 4, pro: 15, contra: 9),
            Time(3, v: 5, e: 1, d: 4, pro: 14, contra: 10),
        };
        CatalogoTimes.CalcularPercentis(times);

        Assert.Equal(1, times[1].Posicao); // 18 pontos
        Assert.Equal(2, times[2].Posicao); // 16 pontos, saldo 4
        Assert.Equal(3, times[0].Posicao); // 16 pontos, saldo 2
    }

    [Fact]
    public void Indicadores_inversos_premiam_quem_cede_menos()
    {
        var times = new List<LinhaTime>
        {
            Time(1, 3, 3, 3, 9, 9, posse: 60, cedidas: 8, faltas: 10),
            Time(2, 3, 3, 3, 9, 9, posse: 50, cedidas: 12, faltas: 14),
            Time(3, 3, 3, 3, 9, 9, posse: 40, cedidas: 16, faltas: 18),
        };
        CatalogoTimes.CalcularPercentis(times);

        Assert.Equal(100, times[0].Percentis["posse"]);
        Assert.Equal(100, times[0].Percentis["finalizacoesCedidas"]);
        Assert.Equal(0, times[2].Percentis["finalizacoesCedidas"]);
        Assert.Equal(0, times[2].Percentis["faltas"]);
    }

    [Fact]
    public void Funcao_entra_no_plano_do_time_so_com_25_por_cento_dos_jogos()
    {
        var titulares = new List<(int, int, string, Setor)>();
        for (var i = 0; i < 10; i++) titulares.Add((i, 1, "Lateral esquerdo", Setor.Defesa));
        for (var i = 10; i < 12; i++) titulares.Add((i, 2, "Ala esquerdo", Setor.Defesa));

        var funcoes = CatalogoTimes.FuncoesDoTime(12, titulares, new Dictionary<int, LinhaAnalise>());

        Assert.Single(funcoes);
        Assert.Equal("Lateral esquerdo", funcoes[0].Funcao);
        Assert.Null(funcoes[0].NotaTitulares); // ninguém analisável: sem nota
    }

    [Fact]
    public void Nota_da_funcao_pesa_pelos_jogos_de_cada_titular()
    {
        var titulares = new List<(int, int, string, Setor)>();
        for (var i = 0; i < 9; i++) titulares.Add((i, 1, "Volante", Setor.Meio));
        for (var i = 9; i < 12; i++) titulares.Add((i, 2, "Volante", Setor.Meio));
        for (var i = 0; i < 12; i++) titulares.Add((i, 3, "Volante", Setor.Meio)); // segundo volante, sem nota
        var analisaveis = new Dictionary<int, LinhaAnalise>
        {
            [1] = new() { AtletaId = 1, Nome = "Atleta Teste 1", Nota = 80 },
            [2] = new() { AtletaId = 2, Nome = "Atleta Teste 2", Nota = 40 },
        };

        var volante = CatalogoTimes.FuncoesDoTime(12, titulares, analisaveis).Single();

        Assert.Equal(12, volante.Jogos);
        Assert.Equal(2, volante.PorJogo);
        Assert.Equal(70, volante.NotaTitulares); // (80×9 + 40×3) / 12; o terceiro não tem nota
        Assert.Equal("Atleta Teste 1", volante.Atletas[0].Nome);
    }

    [Fact]
    public void Carencia_compara_so_times_com_a_mesma_funcao()
    {
        var times = Enumerable.Range(1, 5).Select(i => Time(i, 3, 3, 3, 9, 9)).ToList();
        for (var i = 0; i < 5; i++)
            times[i].Funcoes.Add(new FuncaoNoTime { Funcao = "Lateral esquerdo", Setor = Setor.Defesa, Jogos = 9, NotaTitulares = 30 + i * 10 });
        CatalogoTimes.CalcularPercentis(times);

        var pior = ResumirTime.Necessidades(times[0]).Single();
        Assert.True(pior.Carencia);
        Assert.Equal(0, pior.Percentil);
        Assert.False(ResumirTime.Necessidades(times[4]).Single().Carencia);
        Assert.Contains("Lateral esquerdo", ResumirTime.De(times[0]).Carencias);
    }

    [Fact]
    public void Lateral_e_ala_do_mesmo_lado_sao_equivalentes()
    {
        Assert.Contains("Ala esquerdo", CatalogoTimes.FuncoesEquivalentes("Lateral esquerdo"));
        Assert.Contains("Lateral direito", CatalogoTimes.FuncoesEquivalentes("Ala direito"));
        Assert.Single(CatalogoTimes.FuncoesEquivalentes("Volante"));
    }

    [Fact]
    public void Estilo_pela_posse_em_relacao_a_competicao()
    {
        var times = Enumerable.Range(1, 5).Select(i => Time(i, 3, 3, 3, 9, 9, posse: 40 + i * 4)).ToList();
        CatalogoTimes.CalcularPercentis(times);

        Assert.StartsWith("Propositivo", ResumirTime.Estilo(times[4])!.Rotulo);
        Assert.StartsWith("Reativo", ResumirTime.Estilo(times[0])!.Rotulo);
        Assert.StartsWith("Equilibrado", ResumirTime.Estilo(times[2])!.Rotulo);
    }

    [Fact]
    public void Numeros_defensivos_sao_ajustados_a_posse_do_time()
    {
        var dominante = new LinhaAnalise { AtletaId = 1, Nome = "Atleta Teste 1", Minutos = 900, Desarmes = 20, PosseDoTime = 60 };
        var semPosse = new LinhaAnalise { AtletaId = 2, Nome = "Atleta Teste 2", Minutos = 900, Desarmes = 20 };

        Assert.Equal(1.25, Catalogo.AjustePosse(dominante)); // 50 / 40
        Assert.Equal(2.5, Catalogo.PorChave["desarmes90"].Valor(dominante)!.Value, 3);
        Assert.Equal(2.0, Catalogo.PorChave["desarmes90"].Valor(semPosse)!.Value, 3);
    }

    [Fact]
    public void Le_estatisticas_do_provedor_com_porcentagem_e_nulos()
    {
        var json = JsonDocument.Parse("""
            [{"type":"Ball Possession","value":"57%"},{"type":"Fouls","value":12},{"type":"Red Cards","value":null},{"type":"expected_goals","value":"1.38"}]
            """).RootElement;

        var v = Importador.LerEstatisticas(json);

        Assert.Equal(57, v["Ball Possession"]);
        Assert.Equal(12, v["Fouls"]);
        Assert.False(v.ContainsKey("Red Cards"));
    }

    [Fact]
    public void Le_o_xG_com_casas_decimais_e_nulo_quando_o_provedor_nao_informa()
    {
        JsonElement Lista(string json) => JsonDocument.Parse(json).RootElement;

        Assert.Equal(1.38, Importador.LerXg(Lista("""[{"type":"Ball Possession","value":"57%"},{"type":"expected_goals","value":"1.38"}]""")));
        Assert.Equal(0.7, Importador.LerXg(Lista("""[{"type":"expected_goals","value":0.7}]""")));
        Assert.Null(Importador.LerXg(Lista("""[{"type":"expected_goals","value":null}]""")));
        Assert.Null(Importador.LerXg(Lista("""[{"type":"Fouls","value":12}]""")));
    }
}
