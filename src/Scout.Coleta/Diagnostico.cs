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
    /// <summary>O que a base tem de um atleta (nascimento, estatísticas, escalações): para entender por que ficou fora da análise.</summary>
    public static async Task Atleta(ScoutDbContext db, string nome, CancellationToken ct)
    {
        var termo = nome.ToLower();
        var atletas = await db.Atletas.AsNoTracking()
            .Where(a => a.Nome.ToLower().Contains(termo) || (a.NomeCompleto ?? "").ToLower().Contains(termo)).Take(10).ToListAsync(ct);
        foreach (var a in atletas)
        {
            Console.WriteLine($"{a.Id} | {a.Nome} | {a.NomeCompleto} | nascimento: {a.Nascimento?.ToString() ?? "(sem)"} | atualizado {a.AtualizadoEm:dd/MM}");
            var estat = await db.Estatisticas.AsNoTracking().Where(e => e.AtletaId == a.Id).Include(e => e.Clube).Include(e => e.Competicao).ToListAsync(ct);
            foreach (var e in estat) Console.WriteLine($"   {e.Competicao.Nome} {e.Temporada} | {e.Clube.Nome} | {e.Jogos} jogos, {e.Titular} tit., {e.Minutos} min | posição {e.PosicaoProvedor}");
            var esc = await db.Escalacoes.AsNoTracking().Where(e => e.AtletaId == a.Id).GroupBy(e => e.Funcao).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
            if (esc.Count > 0) Console.WriteLine("   titular como: " + string.Join(", ", esc.Select(e => $"{e.Key} {e.N}")));
        }
        if (atletas.Count == 0) Console.WriteLine("Nenhum atleta com esse nome.");
    }

    /// <summary>Atletas que jogaram pelo menos 450 minutos numa liga mas estão sem data de nascimento (ficam fora da análise).</summary>
    public static async Task SemNascimento(ScoutDbContext db, CancellationToken ct)
    {
        var lista = await db.Estatisticas.AsNoTracking()
            .Where(e => e.Atleta.Nascimento == null && e.Minutos >= 450 && e.Competicao.Tipo != "Cup")
            .OrderBy(e => e.Clube.Nome)
            .Select(e => new { e.AtletaId, e.Atleta.Nome, Clube = e.Clube.Nome, Competicao = e.Competicao.Nome, e.Temporada, e.Minutos })
            .ToListAsync(ct);
        foreach (var x in lista) Console.WriteLine($"{x.AtletaId} | {x.Nome} | {x.Clube} | {x.Competicao} {x.Temporada} | {x.Minutos} min");
        Console.WriteLine($"Total: {lista.Count}");
    }

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
