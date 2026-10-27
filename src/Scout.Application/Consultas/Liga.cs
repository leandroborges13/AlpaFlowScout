using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application.Consultas;

/// <summary>
/// Chances de um time na liga (pontos corridos) pelo modelo AlpaFlow: a mesma força das copas (rating tipo Elo pelos resultados
/// da temporada) e os jogos que faltam simulados 10 mil vezes. É probabilidade, não previsão garantida.
/// </summary>
public record ChanceNaLiga(int ClubeId, string Time, int Posicao, int Pontos, int Jogos, int Vitorias, int SaldoDeGols, int JogosRestantes,
    int PontosEsperados, int TituloPercentual, int Top4Percentual, int Top6Percentual, int? RebaixamentoPercentual,
    int? AcessoPercentual = null, int? AcessoDiretoPercentual = null, int? PlayoffPercentual = null);

/// <summary>
/// Acesso da Série B: até 2025 os 4 primeiros subiam direto; a partir de 2026 sobem os 2 primeiros e do 3º ao 6º há playoff
/// em ida e volta (3º x 6º e 4º x 5º), e os dois vencedores também sobem.
/// </summary>
public record RegraDeAcesso(int Direto, bool Playoff)
{
    public static RegraDeAcesso? Da(string liga, int temporada) =>
        !liga.EndsWith(" B") ? null : temporada >= 2026 ? new(2, true) : new(4, false);

    public string Explicacao => Playoff
        ? $"Sobem os {Direto} primeiros; do 3º ao 6º há playoff de ida e volta (3º x 6º, 4º x 5º), simulado com a mesma força, o segundo jogo na casa do mais bem colocado e empate no agregado decidido nos pênaltis (50%)."
        : $"Sobem os {Direto} primeiros.";
}

public record ChancesNaLiga(int CompeticaoId, string Competicao, int Temporada, int JogosRestantes, int? Rebaixados,
    IReadOnlyList<ChanceNaLiga> Times, string Metodo);

/// <summary>Sem competição, a Série A da temporada mais recente.</summary>
public record ObterChancesNaLiga(int? CompeticaoId = null, int? Temporada = null) : IQuery<ChancesNaLiga?>;

public class ObterChancesNaLigaHandler(IScoutDb db) : IQueryHandler<ObterChancesNaLiga, ChancesNaLiga?>
{
    const int Simulacoes = ObterCopasDoTimeHandler.Simulacoes;
    static readonly string[] NaoVaiTerJogo = ["CANC", "ABD", "AWD", "WO"];

    public async Task<ChancesNaLiga?> Handle(ObterChancesNaLiga q, CancellationToken ct)
    {
        var ligas = await db.Competicoes.AsNoTracking().Where(c => c.Tipo != Competicao.TipoCopa).ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var ligaId = q.CompeticaoId ?? ligas.Where(l => l.Value.EndsWith(" A")).Select(l => (int?)l.Key).FirstOrDefault();
        if (ligaId is null || !ligas.TryGetValue(ligaId.Value, out var nomeLiga)) return null;
        var temporada = q.Temporada ?? await db.Partidas.AsNoTracking().Where(p => p.CompeticaoId == ligaId).MaxAsync(p => (int?)p.Temporada, ct);
        if (temporada is null) return null;

        var todas = await db.Partidas.AsNoTracking().Where(p => p.Temporada == temporada).ToListAsync(ct);
        var daLiga = todas.Where(p => p.CompeticaoId == ligaId).ToList();
        if (daLiga.Count == 0) return null;
        var nomes = await db.Clubes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var forca = ObterCopasDoTimeHandler.Forca(todas, ligas);
        var acesso = RegraDeAcesso.Da(nomeLiga, temporada.Value);
        var (lista, restantes, rebaixados) = Simular(daLiga, forca, nomes, ligaId.Value * 7919 + temporada.Value, acesso);
        return new ChancesNaLiga(ligaId.Value, nomeLiga, temporada.Value, restantes, rebaixados, lista,
            $"Força de cada time calculada pelos resultados de todas as competições da temporada (rating tipo Elo, com mando de campo) e os {restantes} jogos que faltam na liga simulados {Simulacoes:N0} vezes, com o desempate do campeonato (pontos, vitórias, saldo, gols pró).{(acesso is null ? "" : " " + acesso.Explicacao)}");
    }

    /// <summary>Classificação de hoje e os jogos que faltam simulados. Semente fixa: a mesma pergunta dá o mesmo número.</summary>
    public static (List<ChanceNaLiga> Times, int Restantes, int? Rebaixados) Simular(List<Partida> daLiga, Dictionary<int, double> forca,
        Dictionary<int, string> nomes, int semente, RegraDeAcesso? acesso = null)
    {
        var jogados = daLiga.Where(p => p.Encerrada && p.GolsMandante is not null).ToList();
        var restantes = daLiga.Where(p => !p.Encerrada && !NaoVaiTerJogo.Contains(p.Status)).ToList();
        var times = daLiga.SelectMany(p => new[] { p.MandanteId, p.VisitanteId }).Distinct().ToList();
        var idx = times.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var n = times.Count;
        // Série A e B com 20 times: os 4 últimos caem. Em outros formatos (grupos, mata-mata) não há zona fixa.
        int? rebaixados = n == 20 ? 4 : null;

        var (pts, vit, saldo, gp, jogos) = (new int[n], new int[n], new int[n], new int[n], new int[n]);
        foreach (var p in jogados)
            Somar(pts, vit, saldo, gp, idx[p.MandanteId], idx[p.VisitanteId], p.GolsMandante!.Value, p.GolsVisitante!.Value, jogos);

        var rnd = new Random(semente);
        double F(int c) => forca.GetValueOrDefault(c, 1500);
        var (titulo, top4, top6, queda) = (new int[n], new int[n], new int[n], new int[n]);
        var (sobe, sobeDireto, playoff) = (new int[n], new int[n], new int[n]);
        var pontosSomados = new long[n];
        var ordem = Enumerable.Range(0, n).ToArray();
        var sorteio = new double[n];
        for (var s = 0; s < Simulacoes; s++)
        {
            var (sp, sv, ss, sg) = ((int[])pts.Clone(), (int[])vit.Clone(), (int[])saldo.Clone(), (int[])gp.Clone());
            foreach (var p in restantes)
            {
                var (gm, gv) = ObterCopasDoTimeHandler.Placar(rnd, F(p.MandanteId), F(p.VisitanteId), neutro: false);
                Somar(sp, sv, ss, sg, idx[p.MandanteId], idx[p.VisitanteId], gm, gv, null);
            }
            for (var i = 0; i < n; i++) { sorteio[i] = rnd.NextDouble(); pontosSomados[i] += sp[i]; }
            // Desempate do Brasileirão: pontos, vitórias, saldo, gols pró; depois sorteio
            Array.Sort(ordem, (a, b) => sp[b] != sp[a] ? sp[b] - sp[a] : sv[b] != sv[a] ? sv[b] - sv[a]
                : ss[b] != ss[a] ? ss[b] - ss[a] : sg[b] != sg[a] ? sg[b] - sg[a] : sorteio[a].CompareTo(sorteio[b]));
            for (var pos = 0; pos < n; pos++)
            {
                var t = ordem[pos];
                if (pos == 0) titulo[t]++;
                if (pos < 4) top4[t]++;
                if (pos < 6) top6[t]++;
                if (rebaixados is { } r && pos >= n - r) queda[t]++;
                if (acesso is not null && pos < acesso.Direto) { sobeDireto[t]++; sobe[t]++; }
            }
            if (acesso is { Playoff: true } && n >= acesso.Direto + 4)
            {
                // 3º x 6º e 4º x 5º: o pior colocado manda o primeiro jogo
                var d = acesso.Direto;
                for (var k = d; k < d + 4; k++) playoff[ordem[k]]++;
                int Vencedor(int melhor, int pior) =>
                    ObterCopasDoTimeHandler.Confronto(rnd, times[pior], times[melhor], F, idaEVolta: true, neutro: false) == times[melhor] ? melhor : pior;
                sobe[Vencedor(ordem[d], ordem[d + 3])]++;
                sobe[Vencedor(ordem[d + 1], ordem[d + 2])]++;
            }
            ordem = Enumerable.Range(0, n).ToArray();
        }

        int Pct(int x) => (int)Math.Round(x * 100.0 / Simulacoes);
        var faltam = restantes.SelectMany(p => new[] { p.MandanteId, p.VisitanteId }).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var lista = Enumerable.Range(0, n)
            .OrderByDescending(i => pts[i]).ThenByDescending(i => vit[i]).ThenByDescending(i => saldo[i]).ThenByDescending(i => gp[i])
            .Select((i, pos) => new ChanceNaLiga(times[i], nomes.GetValueOrDefault(times[i], "?"), pos + 1, pts[i], jogos[i], vit[i], saldo[i],
                faltam.GetValueOrDefault(times[i]), (int)Math.Round(pontosSomados[i] / (double)Simulacoes),
                Pct(titulo[i]), Pct(top4[i]), Pct(top6[i]), rebaixados is null ? null : Pct(queda[i]),
                acesso is null ? null : Pct(sobe[i]), acesso is null ? null : Pct(sobeDireto[i]), acesso is { Playoff: true } ? Pct(playoff[i]) : null))
            .ToList();

        return (lista, restantes.Count, rebaixados);
    }

    static void Somar(int[] pts, int[] vit, int[] saldo, int[] gp, int m, int v, int gm, int gv, int[]? jogos)
    {
        if (jogos is not null) { jogos[m]++; jogos[v]++; }
        gp[m] += gm; gp[v] += gv; saldo[m] += gm - gv; saldo[v] += gv - gm;
        if (gm > gv) { pts[m] += 3; vit[m]++; }
        else if (gv > gm) { pts[v] += 3; vit[v]++; }
        else { pts[m]++; pts[v]++; }
    }
}
