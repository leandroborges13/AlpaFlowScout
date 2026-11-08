using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Palpite do modelo antes do jogo e "acertou ou errou?" depois (backlog item 4) ----------

/// <summary>
/// O palpite de um jogo. `Favorito`: "mandante", "empate" ou "visitante" (o resultado de maior chance). Depois do jogo:
/// `Resultado` (do mesmo jeito), `Acertou` (o favorito aconteceu) e `AcertouPlacar`. `Congelado`: o jogo já começou e o palpite
/// não muda mais. Sem palpite guardado antes do apito, `Palpite` vem nulo (não se inventa palpite depois do jogo).
/// </summary>
public record Palpite(ResumoPartida Partida, double ChanceMandante, double ChanceEmpate, double ChanceVisitante, string Favorito,
    string PlacarProvavel, double ChancePlacar, DateTime FeitoEm, bool Congelado, string? Resultado, bool? Acertou, bool? AcertouPlacar);

public record ResultadoPalpite(Palpite? Palpite, ResumoPartida? Partida, string Explicacao);

public record ObterPalpite(int? PartidaId = null, string? Time = null) : IQuery<ResultadoPalpite?>;

public record HistoricoDePalpites(string? Time, int Jogos, int Acertos, int AcertosDePlacar, double? Aproveitamento, IReadOnlyList<Palpite> Palpites);

public record ObterHistoricoDePalpites(string? Time = null, int? Temporada = null) : IQuery<HistoricoDePalpites>;

public class PalpitesHandler(IScoutDb db) : IQueryHandler<ObterPalpite, ResultadoPalpite?>, IQueryHandler<ObterHistoricoDePalpites, HistoricoDePalpites>
{
    const string Explicacao = "Modelo AlpaFlow: força de cada time pelos resultados de todas as competições da temporada (rating tipo Elo, " +
        "com mando de campo) e gols de cada lado por distribuição de Poisson. O palpite é atualizado até o apito inicial e depois fica " +
        "guardado: acertou = o resultado de maior chance aconteceu.";

    public async Task<ResultadoPalpite?> Handle(ObterPalpite q, CancellationToken ct)
    {
        Partida? partida = null;
        if (q.PartidaId is { } id) partida = await db.Partidas.FirstOrDefaultAsync(p => p.Id == id, ct);
        else if (!string.IsNullOrWhiteSpace(q.Time))
        {
            var ids = await IdsDoTime(q.Time, ct);
            var agora = DateTime.UtcNow;
            partida = await db.Partidas.Where(p => (ids.Contains(p.MandanteId) || ids.Contains(p.VisitanteId)) && p.Data > agora && (p.Status == "NS" || p.Status == "TBD"))
                .OrderBy(p => p.Data).FirstOrDefaultAsync(ct);
        }
        if (partida is null) return null;
        var palpite = await Palpitar(partida, ct);
        var resumo = (await ProcurarPartidasHandler.Resumir(db, [partida], ct))[0];
        return new ResultadoPalpite(palpite is null ? null : Montar(palpite, partida, resumo), resumo,
            palpite is null ? "Não há palpite guardado de antes do jogo: o modelo não faz palpite depois que a bola rola." : Explicacao);
    }

    /// <summary>Faz (ou refaz) o palpite enquanto o jogo não começou; depois devolve o guardado (ou nulo).</summary>
    public async Task<PalpiteDaPartida?> Palpitar(Partida partida, CancellationToken ct)
    {
        var guardado = await db.Palpites.FirstOrDefaultAsync(x => x.PartidaId == partida.Id, ct);
        if (partida.Data <= DateTime.UtcNow || partida.Status is not ("NS" or "TBD")) return guardado;

        var ligas = await db.Competicoes.AsNoTracking().Where(c => c.Tipo != Competicao.TipoCopa).ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var daTemporada = await db.Partidas.AsNoTracking().Where(p => p.Temporada == partida.Temporada).ToListAsync(ct);
        var forca = ObterCopasDoTimeHandler.Forca(daTemporada, ligas);
        var (fm, fv) = (forca.GetValueOrDefault(partida.MandanteId, 1550), forca.GetValueOrDefault(partida.VisitanteId, 1550));
        var c = Calcular(fm, fv);
        if (guardado is null) db.Palpites.Add(guardado = new PalpiteDaPartida { PartidaId = partida.Id });
        guardado.FeitoEm = DateTime.UtcNow;
        (guardado.ForcaMandante, guardado.ForcaVisitante) = (Math.Round(fm, 1), Math.Round(fv, 1));
        (guardado.ChanceMandante, guardado.ChanceEmpate, guardado.ChanceVisitante) = (c.Mandante, c.Empate, c.Visitante);
        (guardado.PlacarMandante, guardado.PlacarVisitante, guardado.ChancePlacar) = (c.PlacarMandante, c.PlacarVisitante, c.ChancePlacar);
        await db.SaveChangesAsync(ct);
        return guardado;
    }

    public async Task<HistoricoDePalpites> Handle(ObterHistoricoDePalpites q, CancellationToken ct)
    {
        var consulta = from x in db.Palpites.AsNoTracking()
                       join p in db.Partidas.AsNoTracking() on x.PartidaId equals p.Id
                       where p.GolsMandante != null && (p.Status == "FT" || p.Status == "AET" || p.Status == "PEN")
                       select new { x, p };
        if (q.Temporada is { } t) consulta = consulta.Where(y => y.p.Temporada == t);
        if (!string.IsNullOrWhiteSpace(q.Time))
        {
            var ids = await IdsDoTime(q.Time, ct);
            consulta = consulta.Where(y => ids.Contains(y.p.MandanteId) || ids.Contains(y.p.VisitanteId));
        }
        var linhas = await consulta.OrderByDescending(y => y.p.Data).ToListAsync(ct);
        var resumos = (await ProcurarPartidasHandler.Resumir(db, linhas.Select(y => y.p).ToList(), ct)).ToDictionary(r => r.Id);
        var palpites = linhas.Select(y => Montar(y.x, y.p, resumos[y.p.Id])).ToList();
        var acertos = palpites.Count(p => p.Acertou == true);
        return new HistoricoDePalpites(q.Time, palpites.Count, acertos, palpites.Count(p => p.AcertouPlacar == true),
            palpites.Count == 0 ? null : Math.Round(acertos * 100.0 / palpites.Count), palpites);
    }

    async Task<List<int>> IdsDoTime(string time, CancellationToken ct)
    {
        var clubes = await db.Clubes.AsNoTracking().ToListAsync(ct);
        var pontuados = clubes.Select(c => (c.Id, pontos: BuscaPorNome.Pontuar(BuscaPorNome.TraduzirApelido(time), c.Nome))).Where(x => x.pontos > 0).ToList();
        var melhor = pontuados.Select(x => x.pontos).DefaultIfEmpty(0).Max();
        return pontuados.Where(x => x.pontos == melhor && melhor > 0).Select(x => x.Id).ToList();
    }

    public static Palpite Montar(PalpiteDaPartida x, Partida p, ResumoPartida resumo)
    {
        var favorito = Favorito(x.ChanceMandante, x.ChanceEmpate, x.ChanceVisitante);
        var encerrada = p.Encerrada && p.GolsMandante is not null && p.GolsVisitante is not null;
        string? resultado = encerrada ? (p.GolsMandante > p.GolsVisitante ? "mandante" : p.GolsMandante < p.GolsVisitante ? "visitante" : "empate") : null;
        return new Palpite(resumo, x.ChanceMandante, x.ChanceEmpate, x.ChanceVisitante, favorito, $"{x.PlacarMandante} x {x.PlacarVisitante}",
            x.ChancePlacar, x.FeitoEm, p.Data <= DateTime.UtcNow, resultado,
            encerrada ? resultado == favorito : null,
            encerrada ? p.GolsMandante == x.PlacarMandante && p.GolsVisitante == x.PlacarVisitante : null);
    }

    /// <summary>O resultado de maior chance (no empate de chances, o empate).</summary>
    public static string Favorito(double mandante, double empate, double visitante) =>
        mandante > empate && mandante > visitante ? "mandante" : visitante > empate && visitante > mandante ? "visitante" : "empate";

    /// <summary>
    /// Chances (em %, uma casa) e o placar mais provável pela força dos dois times: a mesma conta das simulações
    /// (<see cref="ObterCopasDoTimeHandler.Placar"/>), só que exata, somando a Poisson de 0 a 10 gols de cada lado.
    /// </summary>
    public static (double Mandante, double Empate, double Visitante, int PlacarMandante, int PlacarVisitante, double ChancePlacar) Calcular(
        double forcaMandante, double forcaVisitante, bool neutro = false)
    {
        var (lm, lv) = ObterCopasDoTimeHandler.Medias(forcaMandante, forcaVisitante, neutro);
        double[] Poisson(double l) { var r = new double[11]; r[0] = Math.Exp(-l); for (var k = 1; k <= 10; k++) r[k] = r[k - 1] * l / k; return r; }
        var (pm, pv) = (Poisson(lm), Poisson(lv));
        double vm = 0, em = 0, vv = 0, melhor = -1;
        int bm = 0, bv = 0;
        for (var i = 0; i <= 10; i++)
            for (var j = 0; j <= 10; j++)
            {
                var pr = pm[i] * pv[j];
                if (i > j) vm += pr; else if (i < j) vv += pr; else em += pr;
                if (pr > melhor) { melhor = pr; (bm, bv) = (i, j); }
            }
        var total = vm + em + vv;
        return (Math.Round(vm / total * 100, 1), Math.Round(em / total * 100, 1), Math.Round(vv / total * 100, 1), bm, bv, Math.Round(melhor * 100, 1));
    }
}
