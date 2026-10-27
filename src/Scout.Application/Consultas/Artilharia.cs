using Microsoft.EntityFrameworkCore;

namespace Scout.Application.Consultas;

/// <summary>Gols (ou assistências) de um atleta na temporada, somando as competições pedidas. Fatos públicos.</summary>
public record GolsDoAtleta(int AtletaId, string Nome, int? Idade, IReadOnlyList<string> Clubes,
    IReadOnlyList<(string Competicao, int Gols, int Assistencias)> PorCompeticao, int Jogos, int Minutos, int PenaltisConvertidos)
{
    public int Gols => PorCompeticao.Sum(c => c.Gols);
    public int Assistencias => PorCompeticao.Sum(c => c.Assistencias);
}

/// <param name="Posicao">Mesmo número de gols (ou assistências) divide a posição.</param>
public record ItemArtilharia(int Posicao, bool Empatado, int AtletaId, string Nome, int? Idade, string Clubes,
    int Gols, int Assistencias, int Jogos, int Minutos, int GolsDePenalti, string PorCompeticao);

public record Artilharia(string Criterio, int Temporada, IReadOnlyList<string> CompeticoesConsideradas, IReadOnlyList<ItemArtilharia> Atletas,
    IReadOnlyList<string> Observacoes);

/// <param name="Criterio">gols (padrão) ou assistencias.</param>
public record ObterArtilharia(string Criterio = "gols", int? Temporada = null, int[]? Competicoes = null, int? ClubeId = null, int Limite = 10)
    : IQuery<Artilharia>;

public class ObterArtilhariaHandler(IScoutDb db) : IQueryHandler<ObterArtilharia, Artilharia>
{
    public async Task<Artilharia> Handle(ObterArtilharia q, CancellationToken ct)
    {
        var temporada = q.Temporada ?? await db.Estatisticas.AsNoTracking()
            .Where(e => q.Competicoes == null || q.Competicoes.Contains(e.CompeticaoId))
            .MaxAsync(e => (int?)e.Temporada, ct) ?? DateTime.Today.Year;

        var linhas = await db.Estatisticas.AsNoTracking()
            .Where(e => e.Temporada == temporada && (q.Competicoes == null || q.Competicoes.Contains(e.CompeticaoId))
                        && (q.ClubeId == null || e.ClubeId == q.ClubeId))
            .Select(e => new
            {
                e.AtletaId, e.Atleta.Nome, e.Atleta.Nascimento, Clube = e.Clube.Nome, Competicao = e.Competicao.Nome,
                e.Gols, e.Assistencias, e.Jogos, e.Minutos, e.PenaltisConvertidos,
            })
            .ToListAsync(ct);

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        int? Idade(DateOnly? n) => n is { } d ? hoje.Year - d.Year - (hoje < d.AddYears(hoje.Year - d.Year) ? 1 : 0) : null;
        var atletas = linhas.GroupBy(l => l.AtletaId)
            .Select(g => new GolsDoAtleta(g.Key, g.First().Nome, Idade(g.First().Nascimento), g.Select(l => l.Clube).Distinct().ToList(),
                g.GroupBy(l => l.Competicao).Select(c => (c.Key, c.Sum(l => l.Gols), c.Sum(l => l.Assistencias))).ToList(),
                g.Sum(l => l.Jogos), g.Sum(l => l.Minutos), g.Sum(l => l.PenaltisConvertidos)))
            .Where(a => a.Idade is null or >= 18) // a plataforma não analisa menores de 18 anos
            .ToList();

        var criterio = Criterio(q.Criterio);
        var competicoes = linhas.Select(l => l.Competicao).Distinct().OrderBy(c => c).ToList();
        var observacoes = new List<string>
        {
            "Soma só as competições da base (Séries A, B e C, Copa do Brasil, Libertadores e Sul-Americana): estaduais e jogos de seleção não entram.",
            "Atletas com a mesma posição estão empatados: cite como empate, sem inventar desempate.",
        };
        return new Artilharia(criterio, temporada, competicoes, Ordenar(atletas, criterio, q.Limite), observacoes);
    }

    static string Criterio(string? c) => (c ?? "").Trim().ToLowerInvariant().StartsWith("assist") ? "assistencias" : "gols";

    /// <summary>Ordena e numera (sem banco, para testar). Desempate de exibição: menos jogos primeiro, mas a posição é a mesma.</summary>
    public static List<ItemArtilharia> Ordenar(IEnumerable<GolsDoAtleta> atletas, string? criterio, int limite)
    {
        int Valor(GolsDoAtleta a) => Criterio(criterio) == "assistencias" ? a.Assistencias : a.Gols;
        var ordenados = atletas.Where(a => Valor(a) > 0).OrderByDescending(Valor).ThenBy(a => a.Jogos).ThenBy(a => a.Nome)
            .Take(Math.Clamp(limite, 1, 30)).ToList();
        return ordenados.Select(a => new ItemArtilharia(
            ordenados.FindIndex(o => Valor(o) == Valor(a)) + 1, ordenados.Count(o => Valor(o) == Valor(a)) > 1,
            a.AtletaId, a.Nome, a.Idade, string.Join(", ", a.Clubes), a.Gols, a.Assistencias, a.Jogos, a.Minutos, a.PenaltisConvertidos,
            string.Join("; ", a.PorCompeticao.Where(c => c.Gols > 0 || c.Assistencias > 0).OrderByDescending(c => c.Gols)
                .Select(c => $"{c.Competicao}: {c.Gols} gols, {c.Assistencias} assistências"))))
            .ToList();
    }
}
