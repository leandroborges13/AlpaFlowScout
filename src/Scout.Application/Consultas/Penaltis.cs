using Microsoft.EntityFrameworkCore;

namespace Scout.Application.Consultas;

/// <summary>
/// Pênaltis de cada atleta na temporada: no tempo de jogo (como o provedor informa; ele não conta a disputa)
/// e nas disputas de pênaltis (lidas dos lances). São fatos públicos, como gols: "converteu 7 de 8".
/// </summary>
public record PenaltisDoAtleta(
    int AtletaId, string Nome, int? Idade, IReadOnlyList<string> Clubes, IReadOnlyList<string> Competicoes,
    int ConvertidosNoJogo, int PerdidosNoJogo, int ConvertidosNaDisputa, int PerdidosNaDisputa,
    int Defendidos, int Sofridos, int Cometidos)
{
    public int CobradosNoJogo => ConvertidosNoJogo + PerdidosNoJogo;
    public int CobradosNaDisputa => ConvertidosNaDisputa + PerdidosNaDisputa;
    public int Convertidos(bool comDisputas) => ConvertidosNoJogo + (comDisputas ? ConvertidosNaDisputa : 0);
    public int Cobrados(bool comDisputas) => CobradosNoJogo + (comDisputas ? CobradosNaDisputa : 0);
}

/// <param name="Posicao">Empatados (mesmo aproveitamento e mesmos convertidos, ou mesmo total) dividem a posição.</param>
public record ItemRankingPenaltis(
    int Posicao, bool Empatado, int AtletaId, string Nome, int? Idade, string Clubes, string Competicoes,
    int Convertidos, int Cobrados, int? AproveitamentoPercentual, string NoTempoDeJogo, string? NaDisputa,
    int DefendidosNoTempoDeJogo, int Sofridos, int Cometidos);

public record RankingPenaltis(
    string Tipo, int Temporada, IReadOnlyList<string> CompeticoesConsideradas, bool ComDisputas, int MinimoDeCobrancas,
    IReadOnlyList<ItemRankingPenaltis> Atletas, IReadOnlyList<string> Observacoes);

/// <param name="Tipo">batedores (padrão), goleiros (pênaltis defendidos), sofridos ou cometidos.</param>
public record ObterRankingPenaltis(
    string Tipo = "batedores", int? Temporada = null, int[]? Competicoes = null, int? ClubeId = null,
    bool ComDisputas = true, int MinimoDeCobrancas = 3, int Limite = 10, int? AtletaId = null) : IQuery<RankingPenaltis>;

public class ObterRankingPenaltisHandler(IScoutDb db) : IQueryHandler<ObterRankingPenaltis, RankingPenaltis>
{
    public async Task<RankingPenaltis> Handle(ObterRankingPenaltis q, CancellationToken ct)
    {
        var temporada = q.Temporada ?? await db.Estatisticas.AsNoTracking()
            .Where(e => q.Competicoes == null || q.Competicoes.Contains(e.CompeticaoId))
            .MaxAsync(e => (int?)e.Temporada, ct) ?? DateTime.Today.Year;

        var linhas = await db.Estatisticas.AsNoTracking()
            .Where(e => e.Temporada == temporada && (q.Competicoes == null || q.Competicoes.Contains(e.CompeticaoId)) && (q.ClubeId == null || e.ClubeId == q.ClubeId)
                        && (q.AtletaId == null || e.AtletaId == q.AtletaId))
            .Select(e => new
            {
                e.AtletaId, e.Atleta.Nome, e.Atleta.Nascimento, Clube = e.Clube.Nome, Competicao = e.Competicao.Nome,
                e.PenaltisConvertidos, e.PenaltisPerdidos, e.PenaltisDefendidos, e.PenaltisSofridos, e.PenaltisCometidos,
            })
            .ToListAsync(ct);

        var cobrancas = await (from c in db.CobrancasDisputa.AsNoTracking()
                               join p in db.Partidas.AsNoTracking() on c.PartidaId equals p.Id
                               where p.Temporada == temporada && c.AtletaId != null
                                     && (q.Competicoes == null || q.Competicoes.Contains(p.CompeticaoId)) && (q.ClubeId == null || c.ClubeId == q.ClubeId)
                                     && (q.AtletaId == null || c.AtletaId == q.AtletaId)
                               select new { AtletaId = c.AtletaId!.Value, c.Jogador, c.Convertida })
            .ToListAsync(ct);
        var disputa = cobrancas.GroupBy(c => c.AtletaId).ToDictionary(g => g.Key, g => (Convertidas: g.Count(c => c.Convertida), Perdidas: g.Count(c => !c.Convertida)));
        // Quem só bateu na disputa (sem linha de estatística na competição) também entra
        var nomesDisputa = cobrancas.GroupBy(c => c.AtletaId).ToDictionary(g => g.Key, g => g.First().Jogador);
        var semLinha = disputa.Keys.Except(linhas.Select(l => l.AtletaId)).ToList();
        var atletasSemLinha = await db.Atletas.AsNoTracking().Where(a => semLinha.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => new { a.Nome, a.Nascimento }, ct);

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        int? Idade(DateOnly? n) => n is { } d ? hoje.Year - d.Year - (hoje < d.AddYears(hoje.Year - d.Year) ? 1 : 0) : null;

        var atletas = linhas.GroupBy(l => l.AtletaId).Select(g =>
        {
            disputa.TryGetValue(g.Key, out var d);
            return new PenaltisDoAtleta(g.Key, g.First().Nome, Idade(g.First().Nascimento),
                g.Select(l => l.Clube).Distinct().ToList(), g.Select(l => l.Competicao).Distinct().ToList(),
                g.Sum(l => l.PenaltisConvertidos), g.Sum(l => l.PenaltisPerdidos), d.Convertidas, d.Perdidas,
                g.Sum(l => l.PenaltisDefendidos), g.Sum(l => l.PenaltisSofridos), g.Sum(l => l.PenaltisCometidos));
        }).Concat(semLinha.Select(id =>
        {
            var d = disputa[id];
            var a = atletasSemLinha.GetValueOrDefault(id);
            return new PenaltisDoAtleta(id, a?.Nome ?? nomesDisputa[id], Idade(a?.Nascimento), [], [], 0, 0, d.Convertidas, d.Perdidas, 0, 0, 0);
        }))
        // A plataforma não analisa menores de 18 anos
        .Where(a => a.Idade is null or >= 18)
        .ToList();

        var competicoes = linhas.Select(l => l.Competicao).Distinct().OrderBy(c => c).ToList();
        var r = Ordenar(atletas, q.Tipo, q.ComDisputas, q.MinimoDeCobrancas, q.Limite);
        var observacoes = new List<string>
        {
            "Pênaltis no tempo de jogo vêm do provedor por atleta; os da disputa de pênaltis são contados lance a lance (o provedor não os soma ao atleta).",
        };
        if (Tipo(q.Tipo) == "goleiros")
            observacoes.Add("Defesas de pênalti só no tempo de jogo: o provedor não informa quem defendeu na disputa de pênaltis (lá só se sabe quem converteu ou perdeu).");
        if (r.Any(x => x.Empatado))
            observacoes.Add("Atletas com a mesma posição estão empatados: não há desempate entre eles, cite como empate.");
        if (Tipo(q.Tipo) == "batedores")
            observacoes.Add($"Só entra no ranking quem cobrou pelo menos {q.MinimoDeCobrancas} pênaltis, para 1 de 1 não passar na frente de 9 de 10. Desempate: mais convertidos.");
        return new RankingPenaltis(Tipo(q.Tipo), temporada, competicoes, q.ComDisputas, q.MinimoDeCobrancas, r, observacoes);
    }

    static string Tipo(string? t) => (t ?? "").Trim().ToLowerInvariant() switch
    {
        "goleiros" or "goleiro" or "defendidos" => "goleiros",
        "sofridos" or "sofrido" or "cavados" => "sofridos",
        "cometidos" or "cometido" => "cometidos",
        _ => "batedores",
    };

    /// <summary>Ordena e numera o ranking do tipo pedido (sem banco, para testar).</summary>
    public static List<ItemRankingPenaltis> Ordenar(IEnumerable<PenaltisDoAtleta> atletas, string? tipo, bool comDisputas, int minimoDeCobrancas, int limite)
    {
        var lista = Tipo(tipo) switch
        {
            "goleiros" => atletas.Where(a => a.Defendidos > 0).OrderByDescending(a => a.Defendidos).ThenBy(a => a.Nome),
            "sofridos" => atletas.Where(a => a.Sofridos > 0).OrderByDescending(a => a.Sofridos).ThenBy(a => a.Nome),
            "cometidos" => atletas.Where(a => a.Cometidos > 0).OrderByDescending(a => a.Cometidos).ThenBy(a => a.Nome),
            _ => atletas.Where(a => a.Cobrados(comDisputas) >= Math.Max(1, minimoDeCobrancas))
                .OrderByDescending(a => (double)a.Convertidos(comDisputas) / a.Cobrados(comDisputas))
                .ThenByDescending(a => a.Convertidos(comDisputas))
                .ThenBy(a => a.Nome),
        };
        // Chave do critério: quem tem a mesma chave divide a posição
        object Chave(PenaltisDoAtleta a) => Tipo(tipo) switch
        {
            "goleiros" => a.Defendidos, "sofridos" => a.Sofridos, "cometidos" => a.Cometidos,
            _ => (a.Convertidos(comDisputas), a.Cobrados(comDisputas)),
        };
        var ordenados = lista.Take(Math.Clamp(limite, 1, 30)).ToList();
        return ordenados.Select((a, i) =>
        {
            var cobrados = a.Cobrados(comDisputas);
            var primeiro = ordenados.FindIndex(o => Chave(o).Equals(Chave(a)));
            var empatado = ordenados.Count(o => Chave(o).Equals(Chave(a))) > 1;
            return new ItemRankingPenaltis(primeiro + 1, empatado, a.AtletaId, a.Nome, a.Idade, string.Join(", ", a.Clubes), string.Join(", ", a.Competicoes),
                a.Convertidos(comDisputas), cobrados, cobrados > 0 ? (int)Math.Round(100.0 * a.Convertidos(comDisputas) / cobrados) : null,
                $"{a.ConvertidosNoJogo} de {a.CobradosNoJogo}",
                a.CobradosNaDisputa > 0 ? $"{a.ConvertidosNaDisputa} de {a.CobradosNaDisputa}" : null,
                a.Defendidos, a.Sofridos, a.Cometidos);
        }).ToList();
    }
}
