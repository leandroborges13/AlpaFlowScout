using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scout.Domain;

namespace Scout.Application.Analise;

/// <summary>Um número medido por time (média por jogo). `Inverso` = quanto menor, melhor.</summary>
public record IndicadorTime(string Chave, string Nome, Func<LinhaTime, double?> Valor, bool Inverso = false);

/// <summary>Uma faceta do jogo do time ("solidez defensiva") e os indicadores que a medem.</summary>
public record DimensaoTime(string Chave, string Nome, string Descricao, string[] Indicadores);

/// <summary>Como o time está servido numa função: quem começa jogando ali e o nível dessas escolhas.</summary>
public class FuncaoNoTime
{
    public string Funcao { get; init; } = "";
    public Setor Setor { get; init; }
    /// <summary>Jogos em que o time escalou alguém nesta função.</summary>
    public int Jogos { get; init; }
    /// <summary>Quantos atletas por jogo (2 volantes no 4-2-3-1, por exemplo).</summary>
    public int PorJogo { get; init; } = 1;
    /// <summary>Nota média dos titulares da função, pesada pelos jogos de cada um (só atletas analisáveis).</summary>
    public double? NotaTitulares { get; init; }
    /// <summary>Percentil da NotaTitulares entre os times da competição que usam a mesma função.</summary>
    public double? PercentilNaCompeticao { get; set; }
    public List<(int AtletaId, string Nome, int Jogos, double? Nota)> Atletas { get; init; } = [];
}

/// <summary>Um time numa competição/temporada: resultados, esquemas, elenco e médias por jogo.</summary>
public class LinhaTime
{
    public int ClubeId { get; init; }
    public string Nome { get; init; } = "";
    public string? Escudo { get; init; }
    public int CompeticaoId { get; init; }
    public string Competicao { get; init; } = "";
    public int Temporada { get; init; }
    /// <summary>Copa (mata-mata ou continental): sem classificação, e fica fora das listas gerais.</summary>
    public bool Copa { get; init; }

    public int Jogos { get; init; }
    public int Vitorias { get; init; }
    public int Empates { get; init; }
    public int Derrotas { get; init; }
    public int GolsPro { get; init; }
    public int GolsContra { get; init; }
    public int Pontos => Vitorias * 3 + Empates;
    public double? Aproveitamento => Jogos > 0 ? Pontos * 100.0 / (Jogos * 3) : null;
    public int Posicao { get; set; }

    /// <summary>Esquema de cada jogo com escalação: (esquema, em casa?, pontos ganhos).</summary>
    public List<(string Esquema, bool EmCasa, int Pontos)> EsquemasPorJogo { get; init; } = [];

    // Médias por jogo vindas das estatísticas de partida (uso interno; a tela só vê percentis)
    public int JogosComEstatistica { get; init; }
    public double? Posse { get; init; }
    public double? Passes { get; init; }
    public double? PrecisaoPasses { get; init; }
    public double? Finalizacoes { get; init; }
    public double? FinalizacoesNoAlvo { get; init; }
    public double? FinalizacoesCedidas { get; init; }
    public double? FinalizacoesNoAlvoCedidas { get; init; }
    public double? Escanteios { get; init; }
    public double? Faltas { get; init; }
    public double? Cartoes { get; init; }

    // Titulares (das escalações), com a idade na data de cada jogo
    public double? IdadeMediaTitulares { get; init; }
    /// <summary>% das titularidades dadas a atletas de até 23 anos.</summary>
    public double? TitularesAte23 { get; init; }

    // Elenco
    public double? IdadeMedia { get; init; }
    public double? MinutosAte23 { get; init; }
    public int AtletasUsados { get; init; }
    /// <summary>% dos minutos jogados pelos 11 atletas que mais jogaram (alto = time-base fixo, baixo = muito rodízio).</summary>
    public double? MinutosDosOnzeMaisUsados { get; init; }

    public List<FuncaoNoTime> Funcoes { get; init; } = [];
    public Dictionary<string, double?> Valores { get; } = [];
    public Dictionary<string, double?> Percentis { get; } = [];
}

/// <summary>Um treinador num clube durante a temporada: os jogos em que comandou, analisados como um time.</summary>
public record PassagemNaTemporada(int TreinadorId, string Nome, string? Foto, LinhaTime Linha, DateTime Inicio, DateTime Fim, bool NoCargo,
    IReadOnlyList<int> Partidas)
{
    public const int JogosMinimos = 5;
}

public record AnaliseTimesTemporada(int CompeticaoId, string Competicao, int Temporada, IReadOnlyList<LinhaTime> Times,
    IReadOnlyList<PassagemNaTemporada> Treinadores);

public static class CatalogoTimes
{
    public const int JogosMinimosComEstatistica = 3;

    static double? PorJogo(int valor, LinhaTime t) => t.Jogos > 0 ? (double)valor / t.Jogos : null;

    public static readonly IndicadorTime[] Indicadores =
    [
        new("aproveitamento", "Aproveitamento de pontos", t => t.Aproveitamento),
        new("golsPro", "Gols marcados por jogo", t => PorJogo(t.GolsPro, t)),
        new("golsContra", "Gols sofridos por jogo", t => PorJogo(t.GolsContra, t), Inverso: true),
        new("posse", "Posse de bola", t => t.Posse),
        new("passes", "Passes por jogo", t => t.Passes),
        new("precisaoPasses", "Precisão de passe", t => t.PrecisaoPasses),
        new("finalizacoes", "Finalizações por jogo", t => t.Finalizacoes),
        new("finalizacoesNoAlvo", "Finalizações no alvo por jogo", t => t.FinalizacoesNoAlvo),
        new("escanteios", "Escanteios por jogo", t => t.Escanteios),
        new("finalizacoesCedidas", "Finalizações cedidas por jogo", t => t.FinalizacoesCedidas, Inverso: true),
        new("noAlvoCedidas", "Finalizações no alvo cedidas por jogo", t => t.FinalizacoesNoAlvoCedidas, Inverso: true),
        new("faltas", "Faltas cometidas por jogo", t => t.Faltas, Inverso: true),
        new("cartoes", "Cartões por jogo", t => t.Cartoes, Inverso: true),
        new("titularesAte23", "Titularidades de atletas até 23 anos", t => t.TitularesAte23),
    ];

    public static readonly Dictionary<string, IndicadorTime> PorChave = Indicadores.ToDictionary(i => i.Chave);

    public static readonly DimensaoTime[] Dimensoes =
    [
        new("construcao", "Posse e construção", "Quanto fica com a bola e troca passes", ["posse", "passes", "precisaoPasses"]),
        new("ataque", "Força ofensiva", "Gols e volume de finalizações", ["golsPro", "finalizacoes", "finalizacoesNoAlvo", "escanteios"]),
        new("defesa", "Solidez defensiva", "Quanto deixa o adversário finalizar e marcar", ["golsContra", "finalizacoesCedidas", "noAlvoCedidas"]),
        new("disciplina", "Disciplina", "Poucas faltas e cartões", ["faltas", "cartoes"]),
    ];

    public static double? NotaDaDimensao(LinhaTime t, DimensaoTime d) =>
        MotorDeAnalise.Media(d.Indicadores.Select(k => t.Percentis.GetValueOrDefault(k)));

    /// <summary>
    /// O provedor às vezes tem o mesmo treinador com dois cadastros ("Alex" e "Souza Alex") no mesmo clube.
    /// Nomes em que as palavras de um estão todas no outro viram um só, com o Id de quem tem mais jogos.
    /// </summary>
    public static Dictionary<int, int> UnificarTreinadores(IReadOnlyDictionary<int, int> jogosPorTreinador, Func<int, string> nome)
    {
        var unificado = jogosPorTreinador.Keys.ToDictionary(id => id, id => id);
        var ordem = jogosPorTreinador.OrderByDescending(x => x.Value).Select(x => x.Key).ToList();
        for (var i = 0; i < ordem.Count; i++)
            for (var j = i + 1; j < ordem.Count; j++)
            {
                var (maior, menor) = (ordem[i], ordem[j]);
                if (unificado[maior] != maior || unificado[menor] != menor) continue;
                var a = Palavras(nome(maior));
                var b = Palavras(nome(menor));
                if (a.Count > 0 && b.Count > 0 && (a.IsSubsetOf(b) || b.IsSubsetOf(a))) unificado[menor] = maior;
            }
        return unificado;
    }

    static HashSet<string> Palavras(string nome)
    {
        var semAcento = new string(nome.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return semAcento.ToLowerInvariant().Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length > 1).ToHashSet();
    }

    /// <summary>"Lateral esquerdo" no 4-4-2 faz o papel do "Ala esquerdo" no 3-5-2, e vice-versa.</summary>
    public static IEnumerable<string> FuncoesEquivalentes(string funcao)
    {
        yield return funcao;
        if (funcao.StartsWith("Lateral ")) yield return "Ala " + funcao["Lateral ".Length..];
        if (funcao.StartsWith("Ala ")) yield return "Lateral " + funcao["Ala ".Length..];
    }

    /// <summary>Percentis entre os times da mesma competição (o banco já vem separado por competição).</summary>
    public static void CalcularPercentis(List<LinhaTime> times)
    {
        foreach (var t in times)
            foreach (var ind in Indicadores)
                t.Valores[ind.Chave] = ind.Valor(t);

        foreach (var ind in Indicadores)
        {
            var valores = times.Select(t => t.Valores[ind.Chave]).OfType<double>().ToList();
            foreach (var t in times)
            {
                var p = MotorDeAnalise.Percentil(valores, t.Valores[ind.Chave]);
                t.Percentis[ind.Chave] = p is null ? null : ind.Inverso ? 100 - p : p;
            }
        }

        // Cada função é comparada só com os times que também jogam com ela
        foreach (var grupo in times.SelectMany(t => t.Funcoes).GroupBy(f => f.Funcao))
        {
            var notas = grupo.Select(f => f.NotaTitulares).OfType<double>().ToList();
            foreach (var f in grupo) f.PercentilNaCompeticao = notas.Count >= 4 ? MotorDeAnalise.Percentil(notas, f.NotaTitulares) : null;
        }

        var ordem = times.OrderByDescending(t => t.Pontos).ThenByDescending(t => t.GolsPro - t.GolsContra).ThenByDescending(t => t.GolsPro).ToList();
        for (var i = 0; i < ordem.Count; i++) ordem[i].Posicao = i + 1;
    }

    /// <summary>
    /// Funções do plano de jogo do time (aparecem em pelo menos 25% dos jogos), a partir das escalações,
    /// com a nota de quem começou jogando ali.
    /// </summary>
    public static List<FuncaoNoTime> FuncoesDoTime(int jogosComEscalacao, IEnumerable<(int PartidaId, int AtletaId, string Funcao, Setor Setor)> titulares,
        IReadOnlyDictionary<int, LinhaAnalise> analisaveis)
    {
        if (jogosComEscalacao == 0) return [];
        return titulares
            .GroupBy(x => x.Funcao)
            .Where(g => g.Select(x => x.PartidaId).Distinct().Count() >= Math.Max(3, jogosComEscalacao * 0.25))
            .Select(g =>
            {
                var porAtleta = g.GroupBy(x => x.AtletaId).Select(a => (id: a.Key, jogos: a.Count(), linha: analisaveis.GetValueOrDefault(a.Key))).ToList();
                var comNota = porAtleta.Where(a => a.linha?.Nota is not null).ToList();
                var jogosComNota = comNota.Sum(a => a.jogos);
                // Sem nota para pelo menos metade dos jogos (estreantes, menores, poucos minutos), não dá para avaliar
                double? nota = jogosComNota * 2 >= g.Count() ? comNota.Sum(a => a.linha!.Nota!.Value * a.jogos) / jogosComNota : null;
                return new FuncaoNoTime
                {
                    Funcao = g.Key, Setor = g.First().Setor, Jogos = g.Select(x => x.PartidaId).Distinct().Count(),
                    PorJogo = (int)Math.Round((double)g.Count() / g.Select(x => x.PartidaId).Distinct().Count()), NotaTitulares = nota,
                    Atletas = porAtleta.Where(a => a.linha is not null).OrderByDescending(a => a.jogos)
                        .Select(a => (a.id, a.linha!.Nome, a.jogos, a.linha.Nota)).ToList(),
                };
            })
            .OrderBy(f => f.Setor).ThenBy(f => f.Funcao)
            .ToList();
    }
}

public class AnaliseDeTimes(IScoutDb db, MotorDeAnalise motor, IMemoryCache cache)
{
    public Task<AnaliseTimesTemporada?> Analisar(int competicaoId, int temporada, CancellationToken ct) =>
        cache.GetOrCreateAsync($"times:{competicaoId}:{temporada}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return Calcular(competicaoId, temporada, ct);
        });

    async Task<AnaliseTimesTemporada?> Calcular(int competicaoId, int temporada, CancellationToken ct)
    {
        var competicao = await db.Competicoes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == competicaoId, ct);
        if (competicao is null) return null;
        var referencia = MotorDeAnalise.ReferenciaDa(temporada, DateOnly.FromDateTime(DateTime.Today));

        var partidas = await db.Partidas.AsNoTracking()
            .Where(p => p.CompeticaoId == competicaoId && p.Temporada == temporada
                        && (p.Status == "FT" || p.Status == "AET" || p.Status == "PEN") && p.GolsMandante != null)
            .OrderBy(p => p.Data)
            .ToListAsync(ct);
        if (partidas.Count == 0) return null;
        var ids = partidas.Select(p => p.Id).ToList();

        var estatisticas = (await db.EstatisticasPartida.AsNoTracking().Where(e => ids.Contains(e.PartidaId)).ToListAsync(ct))
            .ToLookup(e => e.PartidaId);
        var escalacoes = await db.Escalacoes.AsNoTracking().Where(e => ids.Contains(e.PartidaId))
            .Select(e => new { e.PartidaId, e.ClubeId, e.AtletaId, e.Esquema, e.Funcao, e.Setor })
            .ToListAsync(ct);
        var escalacoesDoClube = escalacoes.ToLookup(e => e.ClubeId);
        var idsTitulares = escalacoes.Select(e => e.AtletaId).Distinct().ToList();
        var nascimentos = await db.Atletas.AsNoTracking().Where(a => idsTitulares.Contains(a.Id) && a.Nascimento != null)
            .ToDictionaryAsync(a => a.Id, a => a.Nascimento!.Value, ct);
        var elencos = (await db.Estatisticas.AsNoTracking()
                .Where(e => e.CompeticaoId == competicaoId && e.Temporada == temporada && e.Minutos > 0)
                .Select(e => new { e.ClubeId, e.AtletaId, e.Minutos, e.Atleta.Nascimento })
                .ToListAsync(ct))
            .ToLookup(e => e.ClubeId);
        // Id 0 é "treinador não informado" (gravado por versões antigas do importador)
        var comandos = await db.TreinadoresNaPartida.AsNoTracking().Where(t => ids.Contains(t.PartidaId) && t.TreinadorId > 0).ToListAsync(ct);
        var idsTreinadores = comandos.Select(t => t.TreinadorId).Distinct().ToList();
        var treinadores = await db.Treinadores.AsNoTracking().Where(t => idsTreinadores.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);

        var analise = await motor.Analisar(competicaoId, temporada, ct);
        var analisaveis = (analise?.Linhas ?? []).ToDictionary(l => l.AtletaId);

        var clubeIds = partidas.SelectMany(p => new[] { p.MandanteId, p.VisitanteId }).Distinct().ToList();
        var clubes = await db.Clubes.AsNoTracking().Where(c => clubeIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        // Um recorte de jogos do clube vira uma linha: a temporada inteira (time) ou só os jogos de um treinador
        LinhaTime Montar(int clubeId, List<Partida> jogos, bool comElenco)
        {
            var resultados = jogos.Select(p =>
            {
                var emCasa = p.MandanteId == clubeId;
                int pro = emCasa ? p.GolsMandante!.Value : p.GolsVisitante!.Value, contra = emCasa ? p.GolsVisitante!.Value : p.GolsMandante!.Value;
                return (p.Id, emCasa, pro, contra, pontos: pro > contra ? 3 : pro == contra ? 1 : 0, data: DateOnly.FromDateTime(p.Data));
            }).ToList();
            var porPartida = resultados.ToDictionary(r => r.Id);

            // Médias das estatísticas de partida: as do time e as do adversário ("cedidas")
            var proprias = new List<EstatisticaPartida>();
            var doAdversario = new List<EstatisticaPartida>();
            foreach (var p in jogos)
            {
                var doJogo = estatisticas[p.Id].ToList();
                if (doJogo.FirstOrDefault(e => e.ClubeId == clubeId) is { } minha) proprias.Add(minha);
                if (doJogo.FirstOrDefault(e => e.ClubeId != clubeId) is { } dele) doAdversario.Add(dele);
            }
            var comEstatistica = proprias.Count >= CatalogoTimes.JogosMinimosComEstatistica;
            double? Media(IEnumerable<int?> v) => comEstatistica ? MotorDeAnalise.Media(v.Select(x => (double?)x)) : null;
            var comPasses = proprias.Where(e => e.Passes > 0 && e.PassesCertos is not null).ToList();

            var esc = escalacoesDoClube[clubeId].Where(e => porPartida.ContainsKey(e.PartidaId)).ToList();
            var esquemasPorJogo = esc.GroupBy(e => e.PartidaId)
                .Select(g => (g.First().Esquema, porPartida[g.Key].emCasa, porPartida[g.Key].pontos))
                .Where(x => x.Esquema != "")
                .ToList();
            // Idade de cada titular na data do jogo
            var idadesTitulares = esc.Where(e => nascimentos.ContainsKey(e.AtletaId))
                .Select(e => IdadeEm(nascimentos[e.AtletaId], porPartida[e.PartidaId].data)).ToList();

            var elenco = comElenco ? elencos[clubeId].ToList() : [];
            var minutosTotais = elenco.Sum(e => e.Minutos);
            var comIdade = elenco.Where(e => e.Nascimento is not null).Select(e => (idade: IdadeEm(e.Nascimento!.Value, referencia), e.Minutos)).ToList();
            var minutosComIdade = comIdade.Sum(e => e.Minutos);

            clubes.TryGetValue(clubeId, out var clube);
            return new LinhaTime
            {
                ClubeId = clubeId, Nome = clube?.Nome ?? $"Clube {clubeId}", Escudo = clube?.Escudo,
                CompeticaoId = competicaoId, Competicao = competicao.Nome, Temporada = temporada,
                Copa = competicao.EhCopa,
                Jogos = resultados.Count, Vitorias = resultados.Count(r => r.pontos == 3), Empates = resultados.Count(r => r.pontos == 1),
                Derrotas = resultados.Count(r => r.pontos == 0), GolsPro = resultados.Sum(r => r.pro), GolsContra = resultados.Sum(r => r.contra),
                EsquemasPorJogo = esquemasPorJogo,
                JogosComEstatistica = proprias.Count,
                Posse = Media(proprias.Select(e => e.Posse)),
                Passes = Media(proprias.Select(e => e.Passes)),
                PrecisaoPasses = comEstatistica && comPasses.Count > 0 ? comPasses.Sum(e => e.PassesCertos!.Value) * 100.0 / comPasses.Sum(e => e.Passes!.Value) : null,
                Finalizacoes = Media(proprias.Select(e => e.Finalizacoes)),
                FinalizacoesNoAlvo = Media(proprias.Select(e => e.FinalizacoesNoAlvo)),
                FinalizacoesCedidas = Media(doAdversario.Select(e => e.Finalizacoes)),
                FinalizacoesNoAlvoCedidas = Media(doAdversario.Select(e => e.FinalizacoesNoAlvo)),
                Escanteios = Media(proprias.Select(e => e.Escanteios)),
                Faltas = Media(proprias.Select(e => e.Faltas)),
                Cartoes = Media(proprias.Select(e => e.Amarelos is null && e.Vermelhos is null ? (int?)null : (e.Amarelos ?? 0) + 2 * (e.Vermelhos ?? 0))),
                IdadeMediaTitulares = idadesTitulares.Count > 0 ? idadesTitulares.Average() : null,
                TitularesAte23 = idadesTitulares.Count > 0 ? idadesTitulares.Count(i => i <= 23) * 100.0 / idadesTitulares.Count : null,
                IdadeMedia = minutosComIdade > 0 ? comIdade.Sum(e => e.idade * (double)e.Minutos) / minutosComIdade : null,
                MinutosAte23 = minutosComIdade > 0 ? comIdade.Where(e => e.idade <= 23).Sum(e => e.Minutos) * 100.0 / minutosComIdade : null,
                AtletasUsados = elenco.Select(e => e.AtletaId).Distinct().Count(),
                MinutosDosOnzeMaisUsados = minutosTotais > 0
                    ? elenco.GroupBy(e => e.AtletaId).Select(g => g.Sum(e => e.Minutos)).OrderByDescending(m => m).Take(11).Sum() * 100.0 / minutosTotais
                    : null,
                Funcoes = comElenco ? CatalogoTimes.FuncoesDoTime(esquemasPorJogo.Count, esc.Select(e => (e.PartidaId, e.AtletaId, e.Funcao, e.Setor)), analisaveis) : [],
            };
        }

        var jogosDoClube = clubeIds.ToDictionary(id => id, id => partidas.Where(p => p.MandanteId == id || p.VisitanteId == id).ToList());
        var times = clubeIds.Select(id => Montar(id, jogosDoClube[id], comElenco: true)).ToList();
        CatalogoTimes.CalcularPercentis(times);

        // Passagens de cada treinador por clube na temporada
        var comandoPorJogo = comandos.ToDictionary(c => (c.PartidaId, c.ClubeId), c => c.TreinadorId);
        var passagens = new List<PassagemNaTemporada>();
        foreach (var clubeId in clubeIds)
        {
            var comandados = jogosDoClube[clubeId].Where(p => comandoPorJogo.ContainsKey((p.Id, clubeId))).ToList();
            if (comandados.Count == 0) continue;
            var porJogos = comandados.GroupBy(p => comandoPorJogo[(p.Id, clubeId)]).ToDictionary(g => g.Key, g => g.Count());
            string NomeDe(int id) => treinadores.TryGetValue(id, out var t) ? t.Nome : "";
            var unificado = CatalogoTimes.UnificarTreinadores(porJogos, NomeDe);
            int Comandante(Partida p) => unificado[comandoPorJogo[(p.Id, clubeId)]];
            var atual = Comandante(comandados[^1]);
            foreach (var g in comandados.GroupBy(Comandante))
            {
                var doTreinador = g.ToList();
                treinadores.TryGetValue(g.Key, out var t);
                passagens.Add(new PassagemNaTemporada(g.Key, t?.Nome ?? $"Treinador {g.Key}", t?.Foto,
                    Montar(clubeId, doTreinador, comElenco: false), doTreinador[0].Data, doTreinador[^1].Data, g.Key == atual,
                    doTreinador.Select(p => p.Id).ToList()));
            }
        }
        // Treinador com poucos jogos fica fora da comparação: percentil de 2 ou 3 jogos não diz nada
        CatalogoTimes.CalcularPercentis(passagens.Where(p => p.Linha.Jogos >= PassagemNaTemporada.JogosMinimos).Select(p => p.Linha).ToList());

        return new AnaliseTimesTemporada(competicaoId, competicao.Nome, temporada, times, passagens);
    }

    static int IdadeEm(DateOnly nascimento, DateOnly referencia)
    {
        var idade = referencia.Year - nascimento.Year;
        return nascimento > referencia.AddYears(-idade) ? idade - 1 : idade;
    }
}
