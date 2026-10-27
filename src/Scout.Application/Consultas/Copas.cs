using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application.Consultas;

/// <summary>Um jogo do time na copa. Gols nulos = ainda não jogado.</summary>
public record JogoNaCopa(DateTime Data, string Fase, string Adversario, bool EmCasa, int? GolsPro, int? GolsContra, bool Penaltis);

/// <summary>`Resultado`: "Classificado", "Eliminado", "Em disputa", "Campeão", "Vice-campeão" ou nulo (fase de grupos).</summary>
public record FaseNaCopa(string Fase, IReadOnlyList<JogoNaCopa> Jogos, string? Resultado);

public record ChanceRival(string Time, int TituloPercentual);

/// <summary>
/// Estimativa do modelo AlpaFlow: força de cada time pelos resultados da temporada (rating tipo Elo) e o chaveamento que falta
/// simulado 10 mil vezes. É probabilidade, não previsão garantida.
/// </summary>
public record ChanceDeTitulo(int TituloPercentual, int FinalPercentual, int PassarDaFasePercentual, string FaseAtual,
    string? AdversarioDaFase, IReadOnlyList<ChanceRival> Favoritos, string Metodo);

public record CampanhaNaCopa(int CompeticaoId, string Competicao, int Temporada, string Situacao,
    int Jogos, int Vitorias, int Empates, int Derrotas, int GolsPro, int GolsContra,
    IReadOnlyList<FaseNaCopa> Fases, JogoNaCopa? ProximoJogo, ChanceDeTitulo? Chance);

/// <summary>`ChanceDeAlgumTituloPercentual`: ganhar pelo menos uma das copas em que está vivo (as copas tratadas como independentes).</summary>
public record CopasDoTime(int ClubeId, string Time, int Temporada, IReadOnlyList<CampanhaNaCopa> Copas, int? ChanceDeAlgumTituloPercentual = null);

public record ObterCopasDoTime(int ClubeId, int? Temporada = null) : IQuery<CopasDoTime?>;

public class ObterCopasDoTimeHandler(IScoutDb db) : IQueryHandler<ObterCopasDoTime, CopasDoTime?>
{
    public const int Simulacoes = 10_000;
    const int CopaDoBrasil = 73; // final em ida e volta; Libertadores e Sul-Americana têm final única em campo neutro
    static readonly string[] Chave = ["Round of 128", "Round of 64", "Round of 32", "Round of 16", "Quarter-finals", "Semi-finals", "Final"];

    public async Task<CopasDoTime?> Handle(ObterCopasDoTime q, CancellationToken ct)
    {
        var clube = await db.Clubes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == q.ClubeId, ct);
        if (clube is null) return null;
        var copas = await db.Competicoes.AsNoTracking().Where(c => c.Tipo == Competicao.TipoCopa).ToDictionaryAsync(c => c.Id, ct);
        var temporada = q.Temporada ?? await db.Partidas.AsNoTracking()
            .Where(p => copas.Keys.Contains(p.CompeticaoId) && (p.MandanteId == q.ClubeId || p.VisitanteId == q.ClubeId))
            .MaxAsync(p => (int?)p.Temporada, ct);
        if (temporada is null) return new CopasDoTime(clube.Id, clube.Nome, q.Temporada ?? DateTime.UtcNow.Year, []);

        var todas = await db.Partidas.AsNoTracking().Where(p => p.Temporada == temporada).ToListAsync(ct);
        var nomes = await db.Clubes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var ligas = await db.Competicoes.AsNoTracking().Where(c => c.Tipo != Competicao.TipoCopa).ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var forca = Forca(todas, ligas);

        var lista = new List<CampanhaNaCopa>();
        foreach (var (copaId, copa) in copas)
        {
            var daCopa = todas.Where(p => p.CompeticaoId == copaId).ToList();
            var doTime = daCopa.Where(p => p.MandanteId == q.ClubeId || p.VisitanteId == q.ClubeId).OrderBy(p => p.Data).ToList();
            if (doTime.Count == 0) continue;
            lista.Add(Campanha(copa, temporada.Value, q.ClubeId, daCopa, doTime, nomes, forca));
        }
        var vivas = lista.Where(c => c.Chance is not null).ToList();
        int? algum = vivas.Count > 1 ? (int)Math.Round(100 * (1 - vivas.Aggregate(1.0, (acc, c) => acc * (1 - c.Chance!.TituloPercentual / 100.0)))) : null;
        return new CopasDoTime(clube.Id, clube.Nome, temporada.Value, lista.OrderBy(c => c.Situacao.StartsWith("Eliminado")).ToList(), algum);
    }

    static CampanhaNaCopa Campanha(Competicao copa, int temporada, int clubeId, List<Partida> daCopa, List<Partida> doTime,
        Dictionary<int, string> nomes, Dictionary<int, double> forca)
    {
        JogoNaCopa Jogo(Partida p)
        {
            var casa = p.MandanteId == clubeId;
            return new JogoNaCopa(p.Data, p.Rodada ?? "", nomes.GetValueOrDefault(casa ? p.VisitanteId : p.MandanteId, "?"), casa,
                casa ? p.GolsMandante : p.GolsVisitante, casa ? p.GolsVisitante : p.GolsMandante, p.Status == "PEN");
        }

        var jogados = doTime.Where(p => p.Encerrada && p.GolsMandante is not null).Select(Jogo).ToList();
        var ordemFases = OrdemDasFases(daCopa);
        var ultimaFaseDaCopa = ordemFases.LastOrDefault() ?? "";
        var fasesDoTime = doTime.GroupBy(p => p.Rodada ?? "").OrderBy(g => ordemFases.IndexOf(g.Key)).ToList();
        var ultimaFaseDoTime = fasesDoTime.Last().Key;

        string? ResultadoDa(string fase, List<Partida> jogos)
        {
            if (Grupo(fase)) return null;
            if (jogos.Any(p => !p.Encerrada)) return "Em disputa";
            if (fase != ultimaFaseDoTime) return "Classificado";
            var vence = VencedorDoConfronto(jogos);
            if (fase == "Final") return vence == clubeId ? "Campeão" : vence is null ? "Decidido nos pênaltis" : "Vice-campeão";
            if (ordemFases.IndexOf(ultimaFaseDaCopa) > ordemFases.IndexOf(fase)) return "Eliminado";
            return vence == clubeId ? "Classificado" : vence is null ? "Decidido nos pênaltis" : "Eliminado";
        }

        var fases = fasesDoTime.Select(g => new FaseNaCopa(Traduzir(g.Key), g.Select(Jogo).ToList(), ResultadoDa(g.Key, g.ToList()))).ToList();
        var ultima = fases.Last();
        var situacao = ultima.Resultado switch
        {
            "Eliminado" => $"Eliminado: {ultima.Fase.ToLowerInvariant()}",
            "Campeão" => "Campeão",
            "Vice-campeão" => "Vice-campeão",
            "Em disputa" => $"Vivo: disputando {ultima.Fase.ToLowerInvariant()}",
            "Classificado" => $"Vivo: classificado da {ultima.Fase.ToLowerInvariant()}",
            _ when Grupo(ultimaFaseDoTime) && ordemFases.IndexOf(ultimaFaseDaCopa) > ordemFases.IndexOf(ultimaFaseDoTime) => "Eliminado na fase de grupos",
            _ => $"Vivo: {ultima.Fase.ToLowerInvariant()}",
        };
        var proximo = doTime.Where(p => !p.Encerrada && p.Data > DateTime.UtcNow.AddHours(-3)).Select(Jogo).FirstOrDefault();

        ChanceDeTitulo? chance = null;
        if (situacao.StartsWith("Vivo") && !Grupo(ultimaFaseDaCopa))
            chance = Simular(copa.Id, clubeId, daCopa, ultimaFaseDaCopa, nomes, forca);

        return new CampanhaNaCopa(copa.Id, copa.Nome, temporada, situacao, jogados.Count,
            jogados.Count(j => j.GolsPro > j.GolsContra), jogados.Count(j => j.GolsPro == j.GolsContra), jogados.Count(j => j.GolsPro < j.GolsContra),
            jogados.Sum(j => j.GolsPro ?? 0), jogados.Sum(j => j.GolsContra ?? 0), fases, proximo, chance);
    }

    // ---------- Chaveamento ----------

    static bool Grupo(string fase) => fase.StartsWith("Group", StringComparison.OrdinalIgnoreCase);

    static List<string> OrdemDasFases(List<Partida> daCopa) =>
        daCopa.GroupBy(p => p.Rodada ?? "").OrderBy(g => g.Min(p => p.Data)).Select(g => g.Key).ToList();

    /// <summary>Quem passou num confronto encerrado (soma dos gols). Empate no agregado (pênaltis) = nulo: o placar dos pênaltis não fica salvo.</summary>
    static int? VencedorDoConfronto(List<Partida> jogos)
    {
        var a = jogos[0].MandanteId; var b = jogos[0].VisitanteId;
        int GolsDe(int clube) => jogos.Sum(p => p.MandanteId == clube ? p.GolsMandante ?? 0 : p.VisitanteId == clube ? p.GolsVisitante ?? 0 : 0);
        var (ga, gb) = (GolsDe(a), GolsDe(b));
        return ga > gb ? a : gb > ga ? b : null;
    }

    static string Traduzir(string fase) => fase switch
    {
        "Final" => "Final",
        "Semi-finals" => "Semifinal",
        "Quarter-finals" => "Quartas de final",
        "Round of 16" => "Oitavas de final",
        "Round of 32" => "16 avos de final",
        "Round of 64" => "32 avos de final",
        "Round of 128" => "64 avos de final",
        var f when f.StartsWith("Group Stage") => "Fase de grupos" + f["Group Stage".Length..],
        var f when f.StartsWith("Qualification Round") => "Pré-fase " + f["Qualification Round".Length..].Trim(),
        var f => f,
    };

    /// <summary>
    /// Simula o que falta da copa. Confrontos da fase atual com os jogos já disputados mantidos; fases seguintes com
    /// cruzamento sorteado (o chaveamento oficial não vem do provedor). Semente fixa: a mesma pergunta dá o mesmo número.
    /// </summary>
    static ChanceDeTitulo? Simular(int copaId, int clubeId, List<Partida> daCopa, string faseAtual, Dictionary<int, string> nomes, Dictionary<int, double> forca)
    {
        var confrontos = daCopa.Where(p => p.Rodada == faseAtual)
            .GroupBy(p => (Math.Min(p.MandanteId, p.VisitanteId), Math.Max(p.MandanteId, p.VisitanteId)))
            .Select(g => g.OrderBy(p => p.Data).ToList()).ToList();
        if (confrontos.Count == 0 || !confrontos.Any(c => c.Any(p => p.MandanteId == clubeId || p.VisitanteId == clubeId))) return null;
        var idx = Array.IndexOf(Chave, faseAtual);
        var seguintes = idx >= 0 ? Chave[(idx + 1)..] : [];
        var finalIdaEVolta = copaId == CopaDoBrasil;

        var rnd = new Random(copaId * 7919 + clubeId);
        var titulos = new Dictionary<int, int>();
        int passou = 0, final = 0;
        double F(int c) => forca.GetValueOrDefault(c, 1500);

        for (var s = 0; s < Simulacoes; s++)
        {
            var vivos = new List<int>();
            foreach (var jogos in confrontos)
            {
                int a = jogos[0].MandanteId, b = jogos[0].VisitanteId, ga = 0, gb = 0;
                foreach (var p in jogos)
                {
                    var (gm, gv) = p.Encerrada && p.GolsMandante is not null
                        ? (p.GolsMandante!.Value, p.GolsVisitante!.Value)
                        : Placar(rnd, F(p.MandanteId), F(p.VisitanteId), neutro: faseAtual == "Final" && !finalIdaEVolta);
                    if (p.MandanteId == a) { ga += gm; gb += gv; } else { ga += gv; gb += gm; }
                }
                vivos.Add(ga > gb ? a : gb > ga ? b : (rnd.NextDouble() < 0.5 ? a : b));
            }
            if (vivos.Contains(clubeId)) passou++;

            foreach (var fase in seguintes)
            {
                if (vivos.Count < 2) break;
                if (fase == "Final" && vivos.Contains(clubeId)) final++;
                Embaralhar(vivos, rnd);
                var proximos = new List<int>();
                for (var i = 0; i + 1 < vivos.Count; i += 2)
                    proximos.Add(Confronto(rnd, vivos[i], vivos[i + 1], F, idaEVolta: fase != "Final" || finalIdaEVolta, neutro: fase == "Final" && !finalIdaEVolta));
                vivos = proximos;
            }
            if (faseAtual == "Final" && vivos.Contains(clubeId)) final = Simulacoes; // já estava na final
            if (vivos.Count == 1) titulos[vivos[0]] = titulos.GetValueOrDefault(vivos[0]) + 1;
        }

        int Pct(int n) => (int)Math.Round(n * 100.0 / Simulacoes);
        var meu = confrontos.First(c => c.Any(p => p.MandanteId == clubeId || p.VisitanteId == clubeId));
        var adversario = meu[0].MandanteId == clubeId ? meu[0].VisitanteId : meu[0].MandanteId;
        return new ChanceDeTitulo(
            Pct(titulos.GetValueOrDefault(clubeId)),
            faseAtual == "Final" ? 100 : Pct(final),
            Pct(passou), Traduzir(faseAtual), nomes.GetValueOrDefault(adversario),
            titulos.OrderByDescending(t => t.Value).Take(4).Select(t => new ChanceRival(nomes.GetValueOrDefault(t.Key, "?"), Pct(t.Value))).ToList(),
            $"Força de cada time calculada pelos resultados de todas as competições da temporada (rating tipo Elo, com mando de campo) e o que falta da copa simulado {Simulacoes:N0} vezes. Empate no agregado vira pênaltis (50%). As fases seguintes têm cruzamento sorteado.");
    }

    static int Confronto(Random rnd, int a, int b, Func<int, double> f, bool idaEVolta, bool neutro)
    {
        var (ga, gb) = Placar(rnd, f(a), f(b), neutro);
        if (idaEVolta) { var (gv, gm) = Placar(rnd, f(b), f(a), false); ga += gm; gb += gv; }
        return ga > gb ? a : gb > ga ? b : (rnd.NextDouble() < 0.5 ? a : b);
    }

    /// <summary>Placar de um jogo: gols de cada lado por Poisson, com a média dividida pela expectativa do rating.</summary>
    internal static (int, int) Placar(Random rnd, double mandante, double visitante, bool neutro)
    {
        const double golsPorJogo = 2.4;
        var e = 1 / (1 + Math.Pow(10, -((mandante - visitante + (neutro ? 0 : MandoDeCampo)) / 400)));
        return (Poisson(rnd, golsPorJogo * e), Poisson(rnd, golsPorJogo * (1 - e)));
    }

    static int Poisson(Random rnd, double lambda)
    {
        var l = Math.Exp(-lambda); var k = 0; var p = 1.0;
        do { k++; p *= rnd.NextDouble(); } while (p > l);
        return k - 1;
    }

    static void Embaralhar(List<int> xs, Random rnd)
    {
        for (var i = xs.Count - 1; i > 0; i--) { var j = rnd.Next(i + 1); (xs[i], xs[j]) = (xs[j], xs[i]); }
    }

    // ---------- Força (rating tipo Elo) ----------

    const double MandoDeCampo = 65;

    /// <summary>
    /// Rating de cada clube pelos jogos encerrados da temporada, em ordem. Começa pela divisão da liga nacional
    /// (Série A 1600, B 1500, C 1420; sem liga na base, como os estrangeiros das copas, 1550) e ajusta jogo a jogo.
    /// </summary>
    public static Dictionary<int, double> Forca(List<Partida> partidas, Dictionary<int, string> ligas)
    {
        double Inicial(string? liga) => liga switch
        {
            var l when l is not null && l.EndsWith(" A") => 1600,
            var l when l is not null && l.EndsWith(" B") => 1500,
            var l when l is not null && l.EndsWith(" C") => 1420,
            _ => 1550,
        };
        var ligaDe = new Dictionary<int, string>();
        foreach (var p in partidas.Where(p => ligas.ContainsKey(p.CompeticaoId)))
        {
            ligaDe.TryAdd(p.MandanteId, ligas[p.CompeticaoId]);
            ligaDe.TryAdd(p.VisitanteId, ligas[p.CompeticaoId]);
        }
        var r = new Dictionary<int, double>();
        double R(int c) => r.TryGetValue(c, out var v) ? v : r[c] = Inicial(ligaDe.GetValueOrDefault(c));
        foreach (var p in partidas.Where(p => p.Encerrada && p.GolsMandante is not null).OrderBy(p => p.Data))
        {
            var (m, v) = (R(p.MandanteId), R(p.VisitanteId));
            var esperado = 1 / (1 + Math.Pow(10, -((m - v + MandoDeCampo) / 400)));
            var saldo = p.GolsMandante!.Value - p.GolsVisitante!.Value;
            var real = saldo > 0 ? 1 : saldo < 0 ? 0 : 0.5;
            var k = 16 * (saldo == 0 ? 1 : Math.Log(Math.Abs(saldo) + 1) + 1);
            r[p.MandanteId] = m + k * (real - esperado);
            r[p.VisitanteId] = v - k * (real - esperado);
        }
        return r;
    }
}
