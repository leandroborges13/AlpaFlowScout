using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scout.Domain;
using Scout.Infrastructure.Dados;

/// <summary>Conferências sobre os dados importados (cobertura do provedor e consistência da função tática).</summary>
static class Diagnostico
{
    /// <summary>Em quais competições o provedor registrou as estatísticas dos elencos desta liga/temporada.</summary>
    public static async Task CompeticoesDosElencos(ScoutDbContext db, int liga, int temporada, CancellationToken ct)
    {
        var clubes = await db.Partidas.Where(p => p.CompeticaoId == liga && p.Temporada == temporada)
            .Select(p => p.MandanteId).Distinct().ToListAsync(ct);
        var respostas = await db.RespostasProvedor.AsNoTracking()
            .Where(r => r.Chave.StartsWith("/players?") && r.Chave.Contains($"season={temporada}"))
            .Select(r => new { r.Chave, r.Json }).ToListAsync(ct);

        var contagem = new Dictionary<string, (int linhas, int comMinutos)>();
        foreach (var r in respostas.Where(r => clubes.Any(c => r.Chave.Contains($"team={c}"))))
            foreach (var item in JsonDocument.Parse(r.Json).RootElement.GetProperty("response").EnumerateArray())
                foreach (var s in item.GetProperty("statistics").EnumerateArray())
                {
                    var nome = $"{s.GetProperty("league").GetProperty("id")} {s.GetProperty("league").GetProperty("name").GetString()}";
                    var min = s.GetProperty("games").GetProperty("minutes");
                    var c = contagem.GetValueOrDefault(nome);
                    contagem[nome] = (c.linhas + 1, c.comMinutos + (min.ValueKind == JsonValueKind.Number && min.GetInt32() > 0 ? 1 : 0));
                }
        Console.WriteLine($"Estatísticas dos elencos da liga {liga}/{temporada} por competição (linhas · com minutos):");
        foreach (var (nome, c) in contagem.OrderByDescending(x => x.Value.linhas))
            Console.WriteLine($"  {nome}: {c.linhas} · {c.comMinutos}");
    }

    /// <summary>
    /// Se a grade do provedor fosse espelhada entre mandante e visitante, o mesmo lateral apareceria
    /// ora na esquerda, ora na direita. Mede a consistência do lado de cada atleta.
    /// </summary>
    public static async Task ConsistenciaDosLados(ScoutDbContext db, int liga, int temporada, CancellationToken ct)
    {
        var linhas = await (from e in db.Escalacoes.AsNoTracking()
                            join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                            join a in db.Atletas.AsNoTracking() on e.AtletaId equals a.Id
                            where p.CompeticaoId == liga && p.Temporada == temporada && e.Lado != Lado.Centro
                                  && (e.Funcao.StartsWith("Lateral") || e.Funcao.StartsWith("Ala"))
                            select new { e.AtletaId, a.Nome, e.Lado, Mandante = p.MandanteId == e.ClubeId })
            .ToListAsync(ct);

        var porAtleta = linhas.GroupBy(l => (l.AtletaId, l.Nome)).Where(g => g.Count() >= 8).ToList();
        var consistentes = porAtleta.Count(g => g.GroupBy(x => x.Lado).Max(x => x.Count()) >= g.Count() * 0.9);
        Console.WriteLine($"Laterais/alas com 8+ jogos: {porAtleta.Count}; mesmo lado em 90%+ dos jogos: {consistentes}");
        foreach (var g in porAtleta.OrderByDescending(g => g.Count()).Take(12))
        {
            var casa = g.Where(x => x.Mandante).GroupBy(x => x.Lado).Select(x => $"{x.Key} {x.Count()}");
            var fora = g.Where(x => !x.Mandante).GroupBy(x => x.Lado).Select(x => $"{x.Key} {x.Count()}");
            Console.WriteLine($"  {g.Key.Nome}: em casa [{string.Join(", ", casa)}] · fora [{string.Join(", ", fora)}]");
        }
    }
}
