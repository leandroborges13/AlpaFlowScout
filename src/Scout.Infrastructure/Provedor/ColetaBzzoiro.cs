using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Provedor;

public record ResultadoNascimentos(int Clubes, int ClubesLigados, int SemData, int Preenchidos, int Duvidosos);

/// <summary>
/// Bzzoiro Sports Data (sports.bzzoiro.com, grátis, chave em BZZOIRO_KEY, header "Authorization: Token ..."). Cobre o que a
/// PitchAPI não tem: copas, elencos e data de nascimento (backlog itens 9 e 11). Primeiro uso: completar a data de nascimento de
/// quem o provedor principal deixou sem (sem data, o atleta fica fora das análises pela regra dos 18 anos).
/// Os ids são outros: o clube é ligado pelo nome e o atleta pelo nome entre os do clube (<see cref="LigarNomes"/>).
/// </summary>
public class ColetaBzzoiro(HttpClient http, ScoutDbContext db, IConfiguration config, ILogger<ColetaBzzoiro> log)
{
    public const string Fonte = "Bzzoiro (sports.bzzoiro.com)";
    /// <summary>Série A, Série B, Copa do Brasil, Libertadores e Sul-Americana na Bzzoiro.</summary>
    static readonly int[] Ligas = [9, 34, 35, 32, 33];

    public async Task<ResultadoNascimentos> PreencherNascimentos(int temporada, CancellationToken ct)
    {
        var chave = config["BZZOIRO_KEY"];
        if (string.IsNullOrWhiteSpace(chave)) return new(0, 0, 0, 0, 0);

        // Quem está sem data, por clube (estatística na temporada)
        var semData = (await db.Estatisticas.AsNoTracking()
                .Where(e => e.Temporada == temporada && e.Atleta.Nascimento == null)
                .Select(e => new { e.ClubeId, Clube = e.Clube.Nome, e.AtletaId, e.Atleta.Nome, e.Atleta.NomeCompleto })
                .ToListAsync(ct))
            .GroupBy(x => (x.ClubeId, x.Clube))
            .ToDictionary(g => g.Key, g => g.Select(x => (x.AtletaId, x.Nome, x.NomeCompleto)).Distinct().ToList());
        if (semData.Count == 0) return new(0, 0, 0, 0, 0);

        // Os times da Bzzoiro vêm dos jogos dos últimos 60 dias das nossas competições
        var times = new Dictionary<int, (string Nome, string? Curto)>();
        var de = DateTime.UtcNow.AddDays(-60).ToString("yyyy-MM-dd");
        var ate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        foreach (var liga in Ligas)
            foreach (var ev in await TodasAsPaginas($"/api/events/?league={liga}&date_from={de}&date_to={ate}", chave, ct))
                foreach (var lado in new[] { "home_team_obj", "away_team_obj" })
                    if (ev.TryGetProperty(lado, out var t) && t.ValueKind == JsonValueKind.Object && Int(t, "id") is { } id)
                        times[id] = (Texto(t, "name") ?? "", Texto(t, "short_name"));

        int ligados = 0, preenchidos = 0, duvidosos = 0;
        foreach (var ((clubeId, clube), candidatos) in semData)
        {
            var bz = times.Where(t => MesmoClube(clube, t.Value.Nome, t.Value.Curto)).Select(t => t.Key).ToList();
            if (bz.Count != 1) continue;
            ligados++;
            var elenco = await TodasAsPaginas($"/api/players/?team={bz[0]}", chave, ct);
            // Cada atleta nosso → as datas que a Bzzoiro tem para ele (duplicata com datas diferentes = dúvida, não usa)
            var datas = new Dictionary<int, HashSet<DateOnly>>();
            foreach (var p in elenco)
            {
                if (!DateOnly.TryParse(Texto(p, "date_of_birth"), out var nasc) || !Confiavel(nasc)) continue;
                var nosso = LigarNomes.Ligar(Texto(p, "name") ?? "", candidatos) ?? LigarNomes.Ligar(Texto(p, "short_name") ?? "", candidatos);
                if (nosso is { } id) (datas.TryGetValue(id, out var s) ? s : datas[id] = []).Add(nasc);
            }
            foreach (var (atletaId, conjunto) in datas)
            {
                if (conjunto.Count != 1) { duvidosos++; continue; }
                var atleta = await db.Atletas.FindAsync([atletaId], ct);
                if (atleta is null || atleta.Nascimento is not null) continue;
                atleta.Nascimento = conjunto.First();
                atleta.FonteNascimento = Fonte;
                preenchidos++;
                log.LogInformation("  nascimento de {Atleta} ({Clube}): {Data:dd/MM/yyyy} pela Bzzoiro", atleta.Nome, clube, atleta.Nascimento);
            }
            await db.SaveChangesAsync(ct);
        }
        var total = semData.Sum(g => g.Value.Count);
        log.LogInformation("Bzzoiro: {Preenchidos} de {SemData} atletas sem data preenchidos ({Ligados}/{Clubes} clubes ligados, {Duvidosos} com datas conflitantes)",
            preenchidos, total, ligados, semData.Count, duvidosos);
        return new(semData.Count, ligados, total, preenchidos, duvidosos);
    }

    /// <summary>"Atletico-MG" = "Atlético Mineiro"/"Atlético-MG"; "RB Bragantino" = "Red Bull Bragantino"/"Bragantino".</summary>
    public static bool MesmoClube(string nosso, string nome, string? curto)
    {
        var n = PitchApi.Nome(nosso);
        if (n == PitchApi.Nome(nome) || (curto is not null && n == PitchApi.Nome(curto))) return true;
        return LigarNomes.Forca(nome, nosso, null) >= 2 || (curto is not null && LigarNomes.Forca(curto, nosso, null) >= 2);
    }

    /// <summary>1º de janeiro é a data que as bases usam quando só sabem o ano: não serve para a regra dos 18.</summary>
    public static bool Confiavel(DateOnly d) => !(d.Month == 1 && d.Day == 1) && d.Year > 1960;

    async Task<List<JsonElement>> TodasAsPaginas(string caminho, string chave, CancellationToken ct)
    {
        var itens = new List<JsonElement>();
        string? proxima = caminho;
        for (var pagina = 0; proxima is not null && pagina < 30; pagina++)
        {
            var raiz = await Buscar(proxima, chave, ct);
            if (raiz is null) break;
            if (raiz.Value.TryGetProperty("results", out var r)) itens.AddRange(r.EnumerateArray().Select(x => x.Clone()));
            proxima = Texto(raiz.Value, "next");
        }
        return itens;
    }

    DateTime ultima = DateTime.MinValue;

    async Task<JsonElement?> Buscar(string caminho, string chave, CancellationToken ct)
    {
        var espera = ultima.AddMilliseconds(500) - DateTime.UtcNow;
        if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
        ultima = DateTime.UtcNow;
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
            pedido.Headers.Add("Authorization", $"Token {chave}");
            using var r = await http.SendAsync(pedido, ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) { log.LogWarning("Bzzoiro pediu para esperar (429)"); return null; }
            r.EnsureSuccessStatusCode();
            return await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Bzzoiro falhou em {Caminho}", caminho);
            return null;
        }
    }

    static string? Texto(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static int? Int(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
