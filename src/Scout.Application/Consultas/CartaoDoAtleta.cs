using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Cartão do atleta para vídeo (por 90 min, percentil e ranking no setor) ----------

/// <summary>Um número do cartão: valor (por 90 min ou %), percentil no setor (0–100, já invertido quando menos é melhor) e a
/// posição no ranking do setor ("3º de 41"), contando só quem tem o número.</summary>
public record ItemDoCartao(string Chave, string Nome, double? Valor, bool Porcentagem, double? Percentil, int? Posicao, int Total);

public record BlocoDoCartao(string Nome, IReadOnlyList<ItemDoCartao> Itens);

/// <summary>`Comparados`: atletas do mesmo setor na competição com 450+ min (a régua do percentil e do ranking).</summary>
public record CartaoDoAtleta(ResumoAtleta Resumo, string Competicao, string Comparacao, int Comparados, double? Nota, int? PosicaoNaNota,
    IReadOnlyList<BlocoDoCartao> Blocos, IReadOnlyList<string>? OutrasCompeticoes = null);

public record ObterCartaoDoAtleta(int AtletaId, int? Temporada = null) : IQuery<CartaoDoAtleta?>;

public class ObterCartaoDoAtletaHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<ObterCartaoDoAtleta, CartaoDoAtleta?>
{
    public async Task<CartaoDoAtleta?> Handle(ObterCartaoDoAtleta q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, null, ct, incluirExterior: true);
        // Se jogou mais de uma liga na temporada, fica a de mais minutos (como no painel do atleta)
        var l = linhas.Where(x => x.AtletaId == q.AtletaId).OrderByDescending(x => x.Minutos).FirstOrDefault();
        if (l is null) return null;
        var setor = linhas.Where(x => x.CompeticaoId == l.CompeticaoId && x.Temporada == l.Temporada && x.Setor == l.Setor).ToList();
        // O cartão compara só a liga; o que ele fez nas outras competições da temporada (gol de copa, por exemplo) vai numa linha à parte
        var outras = (from t in db.Estatisticas
                      join c in db.Competicoes on t.CompeticaoId equals c.Id
                      where t.AtletaId == q.AtletaId && t.Temporada == l.Temporada && t.CompeticaoId != l.CompeticaoId && t.Jogos > 0
                      select new { c.Nome, t.Jogos, t.Gols, t.Assistencias }).ToList()
            .GroupBy(x => x.Nome) // a mesma copa por dois clubes (trocou no meio do ano) soma numa linha só
            .Select(g => (Nome: g.Key, Jogos: g.Sum(x => x.Jogos), Gols: g.Sum(x => x.Gols), Assistencias: g.Sum(x => x.Assistencias)))
            .OrderByDescending(x => x.Jogos)
            .Select(x => DescreverOutra(x.Nome, x.Jogos, x.Gols, x.Assistencias)).ToList();
        var cartao = Montar(l, setor) with { OutrasCompeticoes = outras };

        // Bloco avançado (item 10): métricas da PitchAPI somadas jogo a jogo na mesma liga e temporada, contra o mesmo setor
        var idsDoSetor = setor.Select(x => x.AtletaId).ToHashSet();
        var somas = await (from d in db.DesempenhosAvancados
                           join p in db.Partidas on d.PartidaId equals p.Id
                           where d.Fonte == "PitchAPI" && d.AtletaId != null && p.CompeticaoId == l.CompeticaoId && p.Temporada == l.Temporada
                           group d by d.AtletaId!.Value into g
                           select new SomaAvancada(g.Key, g.Sum(x => x.Minutos), g.Sum(x => x.Xg ?? 0), g.Sum(x => x.Xa ?? 0),
                               g.Sum(x => x.PassesProgressivos), g.Sum(x => x.ConducoesProgressivas), g.Sum(x => x.ToquesNaArea),
                               g.Sum(x => x.ChancesCriadas), g.Sum(x => x.Recuperacoes), g.Sum(x => x.PassesTercoFinal),
                               g.Sum(x => x.AereosGanhos), g.Sum(x => x.Cortes))).ToListAsync(ct);
        var avancado = BlocoAvancado(l.Setor, somas.Where(s => idsDoSetor.Contains(s.AtletaId)).ToList(), l.AtletaId);
        if (avancado is null) return cartao;
        // com o bloco avançado, a disciplina sai (o cartão tem altura fixa e o avançado diz mais)
        return cartao with { Blocos = [.. cartao.Blocos.Where(b => b.Nome != "Disciplina"), avancado] };
    }

    /// <summary>Soma das métricas avançadas (PitchAPI) de um atleta numa liga e temporada.</summary>
    public record SomaAvancada(int AtletaId, int Minutos, double Xg, double Xa, int PassesProgressivos, int ConducoesProgressivas,
        int ToquesNaArea, int ChancesCriadas, int Recuperacoes, int PassesTercoFinal, int AereosGanhos, int Cortes);

    /// <summary>Minutos mínimos nas métricas avançadas para entrar no bloco (e na régua), como no resto do cartão.</summary>
    public const int MinutosAvancado = 450;

    static readonly (string Chave, string Nome, Func<SomaAvancada, double> Valor)[] Avancadas =
    [
        ("xg90", "xG (gols esperados)", s => s.Xg), ("xa90", "xA (assistências esperadas)", s => s.Xa),
        ("toquesNaArea90", "Toques na área", s => s.ToquesNaArea), ("chancesCriadas90", "Chances criadas", s => s.ChancesCriadas),
        ("conducoesProgressivas90", "Conduções progressivas", s => s.ConducoesProgressivas),
        ("passesProgressivos90", "Passes progressivos", s => s.PassesProgressivos),
        ("passesTercoFinal90", "Passes no terço final", s => s.PassesTercoFinal), ("recuperacoes90", "Recuperações", s => s.Recuperacoes),
        ("aereosGanhos90", "Duelos aéreos ganhos", s => s.AereosGanhos), ("cortes90", "Cortes", s => s.Cortes),
    ];

    /// <summary>
    /// Bloco "Avançado (PitchAPI)": 4 métricas por 90 min, as que mais dizem do setor, com percentil e posição contra o mesmo setor
    /// da liga (só quem tem 450+ min na fonte). Nulo se o atleta não tem minutos suficientes na fonte ou é goleiro. Público para os testes.
    /// </summary>
    public static BlocoDoCartao? BlocoAvancado(Setor setor, IReadOnlyList<SomaAvancada> doSetor, int atletaId)
    {
        string[] chaves = setor switch
        {
            Setor.Ataque => ["xg90", "xa90", "toquesNaArea90", "conducoesProgressivas90"],
            Setor.Meio => ["passesProgressivos90", "xa90", "chancesCriadas90", "recuperacoes90"],
            Setor.Defesa => ["passesProgressivos90", "recuperacoes90", "aereosGanhos90", "cortes90"],
            _ => [],
        };
        var validos = doSetor.Where(s => s.Minutos >= MinutosAvancado).ToList();
        var eu = validos.FirstOrDefault(s => s.AtletaId == atletaId);
        if (chaves.Length == 0 || eu is null || validos.Count < 5) return null;
        var itens = chaves.Select(k =>
        {
            var m = Avancadas.First(a => a.Chave == k);
            double Por90(SomaAvancada s) => m.Valor(s) * 90.0 / s.Minutos;
            var v = Por90(eu);
            var outros = validos.Select(Por90).ToList();
            var abaixo = outros.Count(o => o < v - 1e-9) + 0.5 * (outros.Count(o => Math.Abs(o - v) <= 1e-9) - 1);
            return new ItemDoCartao(k, m.Nome, Math.Round(v, 2), false, Math.Round(100 * abaixo / Math.Max(1, outros.Count - 1)),
                1 + outros.Count(o => o > v + 1e-9), outros.Count);
        }).ToList();
        return new BlocoDoCartao("Avançado (PitchAPI)", itens);
    }

    public static string DescreverOutra(string competicao, int jogos, int gols, int assistencias)
    {
        var nome = competicao.Replace("CONMEBOL ", "").Replace("Sudamericana", "Sul-Americana").Replace(" Do ", " do ").Replace(" De ", " de ");
        var partes = new List<string> { jogos == 1 ? "1 jogo" : $"{jogos} jogos" };
        if (gols > 0) partes.Add(gols == 1 ? "1 gol" : $"{gols} gols");
        if (assistencias > 0) partes.Add(assistencias == 1 ? "1 assist." : $"{assistencias} assist.");
        return $"{nome}: {string.Join(", ", partes)}";
    }

    public static CartaoDoAtleta Montar(LinhaAnalise l, IReadOnlyList<LinhaAnalise> setor)
    {
        var blocos = Blocos(l.Setor)
            .Select(b => new BlocoDoCartao(b.Nome, b.Chaves.Select(k => Item(l, setor, Catalogo.PorChave[k])).ToList()))
            .ToList();
        var notas = setor.Where(x => x.Nota is not null).ToList();
        int? posicaoNaNota = l.Nota is { } n ? 1 + notas.Count(x => x.AtletaId != l.AtletaId && x.Nota > n) : null;
        return new CartaoDoAtleta(Resumir.De(l), l.Competicao, NomeDoSetor(l.Setor), setor.Count,
            l.Nota is { } nota ? Math.Round(nota) : null, posicaoNaNota, blocos);
    }

    static ItemDoCartao Item(LinhaAnalise l, IReadOnlyList<LinhaAnalise> setor, Indicador ind)
    {
        var valor = l.Valores.GetValueOrDefault(ind.Chave) ?? ind.Valor(l);
        var outros = setor.Select(x => x.Valores.GetValueOrDefault(ind.Chave) ?? ind.Valor(x)).OfType<double>().ToList();
        int? posicao = valor is { } v
            ? 1 + outros.Count(o => ind.Inverso ? o < v - 1e-9 : o > v + 1e-9) // empate divide a posição
            : null;
        var percentil = l.Percentis.GetValueOrDefault(ind.Chave);
        var porcentagem = ind.Chave.StartsWith("aproveitamento") || ind.Chave == "precisaoPasses";
        return new ItemDoCartao(ind.Chave, Rotulo(ind.Chave), valor is { } x ? Math.Round(x, porcentagem ? 0 : 2) : null, porcentagem,
            percentil is { } p ? Math.Round(p) : null, posicao, outros.Count);
    }

    /// <summary>Blocos do cartão, o mais importante para o setor primeiro.</summary>
    public static (string Nome, string[] Chaves)[] Blocos(Setor setor)
    {
        (string, string[]) ataque = ("Ataque", ["gols90", "finalizacoesNoAlvo90", "aproveitamentoFinalizacoes", "dribles90", "aproveitamentoDribles"]);
        (string, string[]) passe = ("Passe", ["passes90", "precisaoPasses", "passesDecisivos90", "assistencias90"]);
        (string, string[]) defesa = ("Defesa", ["desarmes90", "interceptacoes90", "bloqueios90"]);
        (string, string[]) duelos = ("Duelos", ["duelosGanhos90", "aproveitamentoDuelos", "faltasSofridas90"]);
        (string, string[]) disciplina = ("Disciplina", ["faltasCometidas90", "cartoes90"]);
        return setor switch
        {
            Setor.Goleiro => [("Gol", ["defesas90", "golsSofridos90"]), passe, disciplina],
            Setor.Defesa => [defesa, duelos, passe, ataque, disciplina],
            Setor.Meio => [passe, defesa, duelos, ataque, disciplina],
            _ => [ataque, passe, duelos, defesa, disciplina],
        };
    }

    /// <summary>Nome curto para caber no cartão (o "por 90 min" vai no cabeçalho).</summary>
    public static string Rotulo(string chave) => chave switch
    {
        "gols90" => "Gols", "assistencias90" => "Assistências", "finalizacoesNoAlvo90" => "Finalizações no alvo",
        "aproveitamentoFinalizacoes" => "% no alvo", "passes90" => "Passes", "precisaoPasses" => "Precisão de passe",
        "passesDecisivos90" => "Passes decisivos", "desarmes90" => "Desarmes", "interceptacoes90" => "Interceptações",
        "bloqueios90" => "Bloqueios", "duelosGanhos90" => "Duelos ganhos", "aproveitamentoDuelos" => "% de duelos ganhos",
        "dribles90" => "Dribles certos", "aproveitamentoDribles" => "% de dribles certos", "faltasSofridas90" => "Faltas sofridas",
        "faltasCometidas90" => "Faltas cometidas", "cartoes90" => "Cartões", "defesas90" => "Defesas", "golsSofridos90" => "Gols sofridos",
        _ => Catalogo.PorChave[chave].Nome,
    };

    static string NomeDoSetor(Setor s) => s switch
    {
        Setor.Goleiro => "goleiros", Setor.Defesa => "defensores", Setor.Meio => "meio-campistas", _ => "atacantes",
    };
}
