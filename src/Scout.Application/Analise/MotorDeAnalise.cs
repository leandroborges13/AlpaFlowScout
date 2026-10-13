using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scout.Domain;

namespace Scout.Application.Analise;

/// <summary>Um atleta numa competição/temporada, já somado entre clubes e com métricas calculadas.</summary>
public class LinhaAnalise
{
    public int AtletaId { get; init; }
    public string Nome { get; init; } = "";
    public string? NomeCompleto { get; init; }
    public DateOnly? Nascimento { get; init; }
    public int Idade { get; init; }
    public int? AlturaCm { get; init; }
    public string? Nacionalidade { get; init; }
    public string? Foto { get; init; }
    public int CompeticaoId { get; init; }
    public string Competicao { get; init; } = "";
    public int Temporada { get; init; }
    public string Clube { get; init; } = "";
    public string[] Clubes { get; init; } = [];
    public int[] ClubeIds { get; init; } = [];
    public string? Escudo { get; init; }
    public Setor Setor { get; init; }
    /// <summary>Função mais frequente nas escalações (ex.: "Zagueiro esquerdo"), quando houver escalações.</summary>
    public string? Funcao { get; init; }
    public Lado? Lado { get; init; }
    public Dictionary<string, int> Funcoes { get; init; } = [];
    public string? EsquemaMaisComum { get; init; }
    /// <summary>Quantas vezes foi titular em cada esquema ("3-4-3": 12).</summary>
    public Dictionary<string, int> Esquemas { get; init; } = [];
    public int Jogos { get; init; }
    public int Titular { get; init; }
    public int Minutos { get; init; }
    public int Gols { get; init; }
    public int Assistencias { get; init; }
    public int GolsSofridos { get; init; }
    public int Defesas { get; init; }
    public int Finalizacoes { get; init; }
    public int FinalizacoesNoAlvo { get; init; }
    public int Passes { get; init; }
    public int PassesDecisivos { get; init; }
    public double? PrecisaoPasses { get; init; }
    public int Desarmes { get; init; }
    public int Bloqueios { get; init; }
    public int Interceptacoes { get; init; }
    public int Duelos { get; init; }
    public int DuelosGanhos { get; init; }
    public int DriblesTentados { get; init; }
    public int DriblesCertos { get; init; }
    public int FaltasCometidas { get; init; }
    public int FaltasSofridas { get; init; }
    public int Amarelos { get; init; }
    public int Vermelhos { get; init; }
    public double ParticipacaoMinutos { get; init; }
    /// <summary>Posse média do time (%) nos jogos com estatística; base do ajuste dos números defensivos.</summary>
    public double? PosseDoTime { get; init; }

    public Dictionary<string, double?> Valores { get; } = [];
    /// <summary>Percentil (0–100) dentro do mesmo setor na competição; já invertido nos indicadores "quanto menos, melhor".</summary>
    public Dictionary<string, double?> Percentis { get; } = [];
    public double? Nota { get; set; }
}

public record AnaliseTemporada(int CompeticaoId, string Competicao, int Temporada, DateOnly Referencia,
    IReadOnlyList<LinhaAnalise> Linhas, int TotalNaCompeticao, int ForaPorIdade, int ForaPorMinutos);

public class MotorDeAnalise(IScoutDb db, IMemoryCache cache)
{
    public const int MinutosMinimos = 450;

    static readonly Dictionary<string, Setor> SetorDoProvedor = new()
    {
        ["Goalkeeper"] = Setor.Goleiro, ["Defender"] = Setor.Defesa, ["Midfielder"] = Setor.Meio, ["Attacker"] = Setor.Ataque,
    };

    /// <summary>Data de referência da idade: fim da temporada, ou hoje se ela ainda está em andamento.</summary>
    public static DateOnly ReferenciaDa(int temporada, DateOnly hoje) =>
        temporada < hoje.Year ? new DateOnly(temporada, 12, 31) : hoje;

    public Task<AnaliseTemporada?> Analisar(int competicaoId, int temporada, CancellationToken ct) =>
        cache.GetOrCreateAsync($"analise:{competicaoId}:{temporada}", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return Calcular(competicaoId, temporada, ct);
        });

    async Task<AnaliseTemporada?> Calcular(int competicaoId, int temporada, CancellationToken ct)
    {
        var competicao = await db.Competicoes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == competicaoId, ct);
        if (competicao is null) return null;
        var referencia = ReferenciaDa(temporada, DateOnly.FromDateTime(DateTime.Today));

        var estatisticas = await db.Estatisticas.AsNoTracking()
            .Where(e => e.CompeticaoId == competicaoId && e.Temporada == temporada)
            .Include(e => e.Atleta).Include(e => e.Clube)
            .ToListAsync(ct);

        // Partidas encerradas por clube: base para "% dos minutos do clube"
        var partidas = await db.Partidas.AsNoTracking()
            .Where(p => p.CompeticaoId == competicaoId && p.Temporada == temporada && (p.Status == "FT" || p.Status == "AET" || p.Status == "PEN"))
            .Select(p => new { p.MandanteId, p.VisitanteId })
            .ToListAsync(ct);
        var jogosDoClube = partidas.SelectMany(p => new[] { p.MandanteId, p.VisitanteId })
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

        var escalacoes = await (from e in db.Escalacoes.AsNoTracking()
                                join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                                where p.CompeticaoId == competicaoId && p.Temporada == temporada
                                select new { e.AtletaId, e.Funcao, e.Lado, e.Setor, e.Esquema })
            .ToListAsync(ct);
        // Posse média de cada clube: quem tem muito a bola defende menos vezes (ajuste dos números defensivos)
        var posses = await (from e in db.EstatisticasPartida.AsNoTracking()
                            join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                            where p.CompeticaoId == competicaoId && p.Temporada == temporada && e.Posse != null
                            group e by e.ClubeId into g
                            select new { ClubeId = g.Key, Jogos = g.Count(), Posse = g.Average(x => (double)x.Posse!.Value) })
            .ToListAsync(ct);
        var posseDoClube = posses.Where(p => p.Jogos >= 3).ToDictionary(p => p.ClubeId, p => p.Posse);

        var escalacoesPorAtleta = escalacoes.GroupBy(e => e.AtletaId).ToDictionary(g => g.Key, g => g.ToList());

        var porAtleta = estatisticas.GroupBy(e => e.AtletaId).ToList();
        int foraIdade = 0, foraMinutos = 0;
        var linhas = new List<LinhaAnalise>();

        foreach (var g in porAtleta)
        {
            var atleta = g.First().Atleta;
            if (!atleta.PodeSerAnalisado(referencia)) { foraIdade++; continue; }
            var minutos = g.Sum(e => e.Minutos);
            if (minutos < MinutosMinimos) { foraMinutos++; continue; }

            var principal = g.OrderByDescending(e => e.Minutos).First();
            escalacoesPorAtleta.TryGetValue(atleta.Id, out var esc);
            var funcoes = (esc ?? []).GroupBy(e => e.Funcao).ToDictionary(x => x.Key, x => x.Count());
            var funcaoPrincipal = funcoes.OrderByDescending(f => f.Value).Select(f => f.Key).FirstOrDefault();
            var ladoPrincipal = esc?.Where(e => e.Funcao == funcaoPrincipal).Select(e => (Lado?)e.Lado).FirstOrDefault();
            var setorTatico = esc?.Where(e => e.Funcao == funcaoPrincipal).Select(e => (Setor?)e.Setor).FirstOrDefault();
            var setor = setorTatico ?? (SetorDoProvedor.TryGetValue(principal.PosicaoProvedor ?? "", out var s) ? s : (Setor?)null);
            if (setor is null) continue;

            var minutosPossiveis = g.Sum(e => jogosDoClube.TryGetValue(e.ClubeId, out var j) ? j * 90 : 0);
            if (minutosPossiveis == 0) minutosPossiveis = g.Max(e => e.Jogos) * 90; // sem partidas importadas
            var comPrecisao = g.Where(e => e.PrecisaoPasses is not null && e.Minutos > 0).ToList();

            linhas.Add(new LinhaAnalise
            {
                AtletaId = atleta.Id, Nome = atleta.Nome, NomeCompleto = atleta.NomeCompleto, Nascimento = atleta.Nascimento,
                Idade = atleta.IdadeEm(referencia)!.Value, AlturaCm = atleta.AlturaCm, Nacionalidade = atleta.Nacionalidade, Foto = atleta.Foto,
                CompeticaoId = competicaoId, Competicao = competicao.Nome, Temporada = temporada,
                Clube = principal.Clube.Nome, Clubes = g.Select(e => e.Clube.Nome).Distinct().ToArray(),
                ClubeIds = g.Select(e => e.ClubeId).Distinct().ToArray(), Escudo = principal.Clube.Escudo,
                Setor = setor.Value, Funcao = funcaoPrincipal, Lado = ladoPrincipal, Funcoes = funcoes,
                Esquemas = (esc ?? []).GroupBy(e => e.Esquema).ToDictionary(x => x.Key, x => x.Count()),
                EsquemaMaisComum = esc?.GroupBy(e => e.Esquema).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault(),
                // O provedor às vezes não informa os jogos; as escalações servem de piso
                Jogos = Math.Max(g.Sum(e => e.Jogos), Math.Max(g.Sum(e => e.Titular), esc?.Count ?? 0)),
                Titular = Math.Max(g.Sum(e => e.Titular), esc?.Count ?? 0), Minutos = minutos,
                Gols = g.Sum(e => e.Gols), Assistencias = g.Sum(e => e.Assistencias), GolsSofridos = g.Sum(e => e.GolsSofridos),
                Defesas = g.Sum(e => e.Defesas), Finalizacoes = g.Sum(e => e.Finalizacoes), FinalizacoesNoAlvo = g.Sum(e => e.FinalizacoesNoAlvo),
                Passes = g.Sum(e => e.Passes), PassesDecisivos = g.Sum(e => e.PassesDecisivos),
                PrecisaoPasses = comPrecisao.Count == 0 ? null : (double)comPrecisao.Sum(e => e.PrecisaoPasses!.Value * e.Minutos) / comPrecisao.Sum(e => e.Minutos),
                Desarmes = g.Sum(e => e.Desarmes), Bloqueios = g.Sum(e => e.Bloqueios), Interceptacoes = g.Sum(e => e.Interceptacoes),
                Duelos = g.Sum(e => e.Duelos), DuelosGanhos = g.Sum(e => e.DuelosGanhos),
                DriblesTentados = g.Sum(e => e.DriblesTentados), DriblesCertos = g.Sum(e => e.DriblesCertos),
                FaltasCometidas = g.Sum(e => e.FaltasCometidas), FaltasSofridas = g.Sum(e => e.FaltasSofridas),
                Amarelos = g.Sum(e => e.Amarelos), Vermelhos = g.Sum(e => e.Vermelhos),
                ParticipacaoMinutos = Math.Min(1, (double)minutos / minutosPossiveis),
                PosseDoTime = g.Where(e => posseDoClube.ContainsKey(e.ClubeId) && e.Minutos > 0) is var comPosse && comPosse.Any()
                    ? comPosse.Sum(e => posseDoClube[e.ClubeId] * e.Minutos) / comPosse.Sum(e => e.Minutos)
                    : null,
            });
        }

        CalcularPercentis(linhas);
        return new AnaliseTemporada(competicaoId, competicao.Nome, temporada, referencia, linhas,
            porAtleta.Count, foraIdade, foraMinutos);
    }

    /// <summary>Percentis por setor (só se compara zagueiro com defensor, atacante com atacante...).</summary>
    public static void CalcularPercentis(List<LinhaAnalise> linhas)
    {
        foreach (var l in linhas)
            foreach (var ind in Catalogo.Indicadores)
                l.Valores[ind.Chave] = ind.Valor(l);

        foreach (var grupo in linhas.GroupBy(l => l.Setor))
        {
            var lista = grupo.ToList();
            foreach (var ind in Catalogo.Indicadores)
            {
                var valores = lista.Select(l => l.Valores[ind.Chave]).Where(v => v is not null).Select(v => v!.Value).ToList();
                foreach (var l in lista)
                {
                    var p = Percentil(valores, l.Valores[ind.Chave]);
                    l.Percentis[ind.Chave] = p is null ? null : ind.Inverso ? 100 - p : p;
                }
            }
            foreach (var l in lista)
                l.Nota = Media(Catalogo.IndicadoresDoSetor(grupo.Key).Select(k => l.Percentis[k]));
        }
    }

    /// <summary>Percentil de `valor` entre `valores` (empates contam pela metade).</summary>
    public static double? Percentil(IReadOnlyList<double> valores, double? valor)
    {
        if (valor is null || valores.Count < 2) return null;
        var abaixo = valores.Count(v => v < valor);
        var iguais = valores.Count(v => v == valor);
        return (abaixo + (iguais - 1) / 2.0) / (valores.Count - 1) * 100;
    }

    public static double? Media(IEnumerable<double?> valores)
    {
        var v = valores.Where(x => x is not null).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : v.Average();
    }
}
