using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Application.Analise;

/// <summary>
/// Diferença de nível de uma liga para a Série A, em gols por jogo, e o deslocamento que ela dá na nota dos atletas.
/// `Deslocamento` é em desvios-padrão do atleta (negativo = liga mais fraca): GolsPorJogo / (desvio do saldo dos times da Série A × √11).
/// </summary>
/// `Estimativa`: menos de 10 jogos direto contra clubes da Série A (a ligação vem por outras ligas, como México e MLS pelo Mundial
/// de Clubes e pela Leagues Cup): o número serve de ordem de grandeza, e a tela mostra "≈".
public record ForcaDaLiga(int CompeticaoId, string Competicao, double GolsPorJogo, int Jogos, double Deslocamento, bool Estimativa = false,
    int JogosContraReferencia = 0);

/// <summary>
/// Força das ligas pelos confrontos nas copas continentais (Libertadores, Sul-Americana): saldo de gols de clubes de ligas diferentes,
/// descontado o nível de cada clube dentro da própria liga (saldo por jogo no campeonato nacional), com vantagem de mando e um prior
/// que puxa para zero quem tem poucos jogos. Assim o "melhor do Equador contra o 8º do Brasil" não infla a liga equatoriana.
/// A Série A é a referência (0). Serve para converter a nota de um atleta de fora para a escala da Série A (<see cref="NotaNaReferencia"/>).
/// </summary>
public class ForcaDasLigas(IScoutDb db, MotorDeAnalise motor, IMemoryCache cache)
{
    public const int Referencia = 71;
    public const int JogosMinimos = 20;
    const double Prior = 4; // ~ força com desvio de 0,5 gol/jogo antes dos dados
    static readonly double RaizDeOnze = Math.Sqrt(11); // o time é a soma de 11: o desvio do atleta é o do time / √11

    public async Task<IReadOnlyDictionary<int, ForcaDaLiga>> Obter(CancellationToken ct) =>
        (await cache.GetOrCreateAsync("forca-das-ligas", e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            return Calcular(ct);
        }))!;

    async Task<IReadOnlyDictionary<int, ForcaDaLiga>> Calcular(CancellationToken ct)
    {
        var competicoes = await db.Competicoes.AsNoTracking().ToListAsync(ct);
        var nacionais = competicoes.Where(c => !c.EhCopa && c.Pais != "World").ToDictionary(c => c.Id);
        var continentais = competicoes.Where(c => c.EhCopa && c.Pais == "World").Select(c => c.Id).ToList();
        if (!nacionais.ContainsKey(Referencia) || continentais.Count == 0) return new Dictionary<int, ForcaDaLiga>();
        var idsNacionais = nacionais.Keys.ToList();

        // Liga de cada clube: a nacional da temporada mais recente em que ele tem minutos (a de mais minutos)
        var minutos = await db.Estatisticas.AsNoTracking().Where(e => idsNacionais.Contains(e.CompeticaoId) && e.Minutos > 0)
            .GroupBy(e => new { e.ClubeId, e.CompeticaoId, e.Temporada })
            .Select(g => new { g.Key.ClubeId, g.Key.CompeticaoId, g.Key.Temporada, Minutos = g.Sum(x => x.Minutos) })
            .ToListAsync(ct);
        var ligaDoClube = minutos.GroupBy(x => x.ClubeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Temporada).ThenByDescending(x => x.Minutos).First());

        // Nível do clube na liga: saldo por jogo no campeonato nacional daquela temporada (a média da liga é zero)
        var nacionaisJogos = await db.Partidas.AsNoTracking()
            .Where(p => idsNacionais.Contains(p.CompeticaoId) && p.GolsMandante != null && p.GolsVisitante != null)
            .Select(p => new { p.CompeticaoId, p.Temporada, p.MandanteId, p.VisitanteId, Saldo = p.GolsMandante!.Value - p.GolsVisitante!.Value })
            .ToListAsync(ct);
        var nivel = nacionaisJogos
            .SelectMany(p => new[] { (p.CompeticaoId, p.Temporada, Clube: p.MandanteId, Saldo: (double)p.Saldo), (p.CompeticaoId, p.Temporada, Clube: p.VisitanteId, Saldo: (double)-p.Saldo) })
            .GroupBy(x => (x.CompeticaoId, x.Temporada, x.Clube))
            .ToDictionary(g => g.Key, g => g.Average(x => x.Saldo));

        // Desvio do saldo por jogo entre os times da Série A (temporada mais recente)
        var ultimaA = nacionaisJogos.Where(p => p.CompeticaoId == Referencia).Select(p => p.Temporada).DefaultIfEmpty().Max();
        var saldosA = nivel.Where(x => x.Key.CompeticaoId == Referencia && x.Key.Temporada == ultimaA).Select(x => x.Value).ToList();
        if (saldosA.Count < 10) return new Dictionary<int, ForcaDaLiga>();
        var media = saldosA.Average();
        var desvioTimes = Math.Sqrt(saldosA.Sum(x => (x - media) * (x - media)) / saldosA.Count);

        // Jogos de copa entre clubes de ligas nacionais diferentes, nas duas temporadas mais recentes
        var copa = await db.Partidas.AsNoTracking()
            .Where(p => continentais.Contains(p.CompeticaoId) && p.GolsMandante != null && p.GolsVisitante != null)
            .Select(p => new { p.Temporada, p.MandanteId, p.VisitanteId, Saldo = p.GolsMandante!.Value - p.GolsVisitante!.Value })
            .ToListAsync(ct);
        var ultimaCopa = copa.Select(c => c.Temporada).DefaultIfEmpty().Max();
        var jogos = new List<(int LigaMandante, int LigaVisitante, double Saldo)>();
        foreach (var c in copa.Where(c => c.Temporada >= ultimaCopa - 1))
        {
            if (!ligaDoClube.TryGetValue(c.MandanteId, out var lm) || !ligaDoClube.TryGetValue(c.VisitanteId, out var lv) || lm.CompeticaoId == lv.CompeticaoId)
                continue;
            var nm = nivel.GetValueOrDefault((lm.CompeticaoId, lm.Temporada, c.MandanteId));
            var nv = nivel.GetValueOrDefault((lv.CompeticaoId, lv.Temporada, c.VisitanteId));
            jogos.Add((lm.CompeticaoId, lv.CompeticaoId, c.Saldo - (nm - nv)));
        }

        int Diretos(int liga) => jogos.Count(j => (j.LigaMandante == liga && j.LigaVisitante == Referencia) || (j.LigaMandante == Referencia && j.LigaVisitante == liga));
        return Estimar(jogos, Referencia, Prior)
            .Where(x => x.Value.Jogos >= JogosMinimos || x.Key == Referencia)
            .ToDictionary(x => x.Key, x => new ForcaDaLiga(x.Key, nacionais.GetValueOrDefault(x.Key)?.Nome ?? $"Liga {x.Key}",
                Math.Round(x.Value.Gols, 2), x.Value.Jogos, x.Key == Referencia ? 0 : x.Value.Gols / (desvioTimes * RaizDeOnze),
                x.Key != Referencia && Diretos(x.Key) < 10, x.Key == Referencia ? 0 : Diretos(x.Key)));
    }

    /// <summary>
    /// Mínimos quadrados com prior (ridge): saldo = mando + força[liga mandante] − força[liga visitante]. A referência fica em zero.
    /// Devolve a força (gols por jogo em relação à referência) e quantos jogos cada liga teve.
    /// </summary>
    public static Dictionary<int, (double Gols, int Jogos)> Estimar(IReadOnlyList<(int LigaMandante, int LigaVisitante, double Saldo)> jogos, int referencia, double prior)
    {
        var ligas = jogos.SelectMany(j => new[] { j.LigaMandante, j.LigaVisitante }).Append(referencia).Distinct().Where(l => l != referencia).OrderBy(l => l).ToList();
        int n = ligas.Count + 1; // [0] = mando
        var a = new double[n, n];
        var b = new double[n];
        foreach (var (lm, lv, saldo) in jogos)
        {
            var x = new double[n];
            x[0] = 1;
            if (lm != referencia) x[1 + ligas.IndexOf(lm)] += 1;
            if (lv != referencia) x[1 + ligas.IndexOf(lv)] -= 1;
            for (int i = 0; i < n; i++)
            {
                b[i] += x[i] * saldo;
                for (int j = 0; j < n; j++) a[i, j] += x[i] * x[j];
            }
        }
        for (int i = 1; i < n; i++) a[i, i] += prior;
        a[0, 0] += 1e-6;
        var sol = Resolver(a, b);
        var r = new Dictionary<int, (double, int)> { [referencia] = (0, jogos.Count(j => j.LigaMandante == referencia || j.LigaVisitante == referencia)) };
        for (int k = 0; k < ligas.Count; k++)
            r[ligas[k]] = (sol[1 + k], jogos.Count(j => j.LigaMandante == ligas[k] || j.LigaVisitante == ligas[k]));
        return r;
    }

    static double[] Resolver(double[,] a, double[] b)
    {
        int n = b.Length;
        for (int i = 0; i < n; i++)
        {
            int p = i;
            for (int k = i + 1; k < n; k++) if (Math.Abs(a[k, i]) > Math.Abs(a[p, i])) p = k;
            for (int j = 0; j < n; j++) (a[i, j], a[p, j]) = (a[p, j], a[i, j]);
            (b[i], b[p]) = (b[p], b[i]);
            for (int k = 0; k < n; k++)
            {
                if (k == i) continue;
                var f = a[k, i] / a[i, i];
                for (int j = 0; j < n; j++) a[k, j] -= f * a[i, j];
                b[k] -= f * b[i];
            }
        }
        return Enumerable.Range(0, n).Select(i => b[i] / a[i, i]).ToArray();
    }

    /// <summary>
    /// Conversor para a escala da Série A de uma pontuação (a nota, ou a nota ponderada da busca) dos atletas de `linhas`.
    /// Nulo quando não há atleta de liga estrangeira com força conhecida: aí nada muda. Atleta de liga brasileira fica com a própria pontuação.
    /// </summary>
    public async Task<Func<LinhaAnalise, double?, double?>?> Conversor(IReadOnlyCollection<LinhaAnalise> linhas, Func<LinhaAnalise, double?> pontuar,
        CancellationToken ct)
    {
        var estrangeiras = linhas.Select(l => l.CompeticaoId).Distinct().ToList();
        var paises = await db.Competicoes.AsNoTracking().Where(c => estrangeiras.Contains(c.Id) && c.Pais != "Brazil" && c.Pais != "")
            .Select(c => c.Id).ToListAsync(ct);
        if (paises.Count == 0) return null;
        var forcas = await Obter(ct);
        var alvo = (await BuscarPorPerfilHandler.CompeticoesAlvo(db, null, [Referencia], ct)).FirstOrDefault();
        if (alvo == default || await motor.Analisar(alvo.Competicao, alvo.Temporada, ct) is not { } refe) return null;

        static List<double> Ordenar(IEnumerable<double?> v) => v.Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
        var naReferencia = refe.Linhas.GroupBy(l => l.Setor).ToDictionary(g => g.Key, g => Ordenar(g.Select(pontuar)));
        var naLiga = linhas.Where(l => paises.Contains(l.CompeticaoId)).GroupBy(l => (l.CompeticaoId, l.Setor))
            .ToDictionary(g => g.Key, g => Ordenar(g.Select(pontuar)));

        return (l, valor) =>
        {
            if (valor is null) return null;
            if (!paises.Contains(l.CompeticaoId)) return valor;
            if (!forcas.TryGetValue(l.CompeticaoId, out var f) || !naLiga.TryGetValue((l.CompeticaoId, l.Setor), out var liga)
                || !naReferencia.TryGetValue(l.Setor, out var referencia)) return null;
            return NotaNaReferencia.Converter(valor.Value, liga, referencia, f.Deslocamento);
        };
    }
}

/// <summary>
/// Converte uma nota de uma liga para a escala de outra: a posição (percentil) na liga dele vira um desvio-padrão (normal),
/// soma-se o deslocamento da liga (negativo = liga mais fraca) e lê-se a nota que ocupa essa posição na liga de referência.
/// </summary>
public static class NotaNaReferencia
{
    public static double? Converter(double nota, IReadOnlyList<double> naLigaOrdenadas, IReadOnlyList<double> naReferenciaOrdenadas, double deslocamento)
    {
        if (naLigaOrdenadas.Count < 5 || naReferenciaOrdenadas.Count < 5) return null;
        int n = naLigaOrdenadas.Count;
        var p = (naLigaOrdenadas.Count(v => v < nota) + 0.5 * naLigaOrdenadas.Count(v => v == nota)) / n;
        p = Math.Clamp(p, 0.5 / n, 1 - 0.5 / n);
        var p2 = Normal.Cdf(Normal.InvCdf(p) + deslocamento);
        return Math.Round(Quantil(naReferenciaOrdenadas, p2), 1);
    }

    static double Quantil(IReadOnlyList<double> ordenadas, double p)
    {
        var pos = p * (ordenadas.Count - 1);
        int i = (int)Math.Floor(pos);
        if (i >= ordenadas.Count - 1) return ordenadas[^1];
        return ordenadas[i] + (pos - i) * (ordenadas[i + 1] - ordenadas[i]);
    }
}

/// <summary>Normal padrão: acumulada (Abramowitz-Stegun 7.1.26) e inversa (Acklam), precisas o bastante para percentis.</summary>
public static class Normal
{
    public static double Cdf(double z)
    {
        var t = 1 / (1 + 0.3275911 * Math.Abs(z) / Math.Sqrt(2));
        var erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z / 2);
        return z >= 0 ? (1 + erf) / 2 : (1 - erf) / 2;
    }

    public static double InvCdf(double p)
    {
        double[] a = [-39.69683028665376, 220.9460984245205, -275.9285104469687, 138.3577518672690, -30.66479806614716, 2.506628277459239];
        double[] b = [-54.47609879822406, 161.5858368580409, -155.6989798598866, 66.80131188771972, -13.28068155288572];
        double[] c = [-0.007784894002430293, -0.3223964580411365, -2.400758277161838, -2.549732539343734, 4.374664141464968, 2.938163982698783];
        double[] d = [0.007784695709041462, 0.3224671290700398, 2.445134137142996, 3.754408661907416];
        const double baixo = 0.02425;
        if (p < baixo)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        if (p > 1 - baixo)
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        var r = p - 0.5;
        var s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1);
    }
}
