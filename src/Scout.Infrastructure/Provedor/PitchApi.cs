using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Mapa de finalizações e de calor pela PitchAPI (pitchapi.dev, grátis, chave em PITCHAPI_KEY). Só pós-jogo e só ligas: Série A
/// (desde 2023) e Série B (desde 2024); copas não têm. A partida é achada pela data (até 3 h de diferença) e pelo nome de um dos
/// times, porque os ids são outros. Serviço pequeno e sem garantia: qualquer falha vira "sem mapa", nunca erro na tela.
/// </summary>
public class PitchApi(HttpClient http, IConfiguration config, IMemoryCache cache, ILogger<PitchApi> log) : IMapaDaPartida, IEstiloDaPartida
{
    /// <summary>Nossas competições (ids da API-Football) e as ligas correspondentes na PitchAPI.</summary>
    public static readonly IReadOnlyDictionary<int, string> Ligas = new Dictionary<int, string>
    {
        [71] = "l_1D8Xrl", [72] = "l_3qUkB8", [128] = "l_45VZuL", [239] = "l_1xXulJ", [242] = "l_1dmt9H",
        // 26/11/2026: as ligas de fora que o Scout já importa e que a PitchAPI tem de graça
        [262] = "l_3v84VE", [94] = "l_4QexZg", [253] = "l_0eg6C9", [235] = "l_2Mh0jT", [301] = "l_0aEfof", [305] = "l_3wr44B",
    };
    // 128 Argentina, 239 Colômbia, 242 Equador, 262 México, 94 Portugal, 253 MLS, 235 Rússia, 301 Emirados, 305 Catar (grátis).
    // O Uruguai e a Arábia Saudita não estão na PitchAPI.

    /// <summary>Ligas em que a PitchAPI usa a temporada de dois anos ("2026/2027"): calendário europeu e o México (Apertura + Clausura).</summary>
    static readonly HashSet<int> TemporadaDividida = [262, 94, 235, 301, 305];

    /// <summary>
    /// A temporada como a PitchAPI pede: "2026" nas ligas de ano civil e "2026/2027" nas divididas (a temporada 2026 da API-Football
    /// nessas ligas é a que começa em 2026). Sem isso, o México dava 0 jogos.
    /// </summary>
    public static string Temporada(int competicaoId, int temporada) =>
        TemporadaDividida.Contains(competicaoId) ? $"{temporada}/{temporada + 1}" : temporada.ToString();

    public async Task<MapaDaPartida?> Obter(ResumoPartida p, CancellationToken ct)
    {
        var chave = config["PITCHAPI_KEY"];
        if (string.IsNullOrWhiteSpace(chave) || !Ligas.TryGetValue(p.CompeticaoId, out var liga) || p.GolsMandante is null) return null;
        try
        {
            var jogos = await Buscar($"/v1/leagues/{liga}/matches?season={Uri.EscapeDataString(Temporada(p.CompeticaoId, p.Temporada))}", chave, TimeSpan.FromMinutes(30), ct);
            var jogo = Achar(jogos.GetProperty("matches"), p);
            if (jogo is null) return null;
            var (id, casaId, foraId) = jogo.Value;
            int Nosso(string? time) => time == casaId ? p.MandanteId : time == foraId ? p.VisitanteId : 0;

            var chutes = await Buscar($"/v1/matches/{id}/shots", chave, TimeSpan.FromHours(12), ct);
            var finalizacoes = chutes.GetProperty("periods").EnumerateArray()
                .SelectMany(per => per.GetProperty("shots").EnumerateArray())
                .Select(s => new FinalizacaoNoMapa(
                    Nosso(Texto(s, "team_id")), Texto(s, "player", "name") ?? "?", Numero(s, "minute") is { } m ? (int)m : 0,
                    Numero(s, "x") ?? 0, Numero(s, "y") ?? 0, Numero(s, "expected_goals") ?? 0,
                    // a PitchAPI marca chute bloqueado como is_on_target: no alvo é só o que chegou ao gol (como na estatística do jogo)
                    Logico(s, "is_on_target") && !Logico(s, "is_blocked"), Texto(s, "event_type") == "Goal", Logico(s, "is_inside_box"),
                    Texto(s, "situation"), Logico(s, "is_blocked")))
                .Where(f => f.ClubeId != 0)
                .OrderBy(f => f.Minuto)
                .ToList();

            var calor = await Buscar($"/v1/matches/{id}/heatmaps?frame=home_ltr", chave, TimeSpan.FromHours(12), ct);
            var times = calor.GetProperty("teams").EnumerateArray()
                .Select(t => new CalorDoTime(
                    Texto(t, "side") == "home" ? p.MandanteId : p.VisitanteId,
                    Numero(t, "actions") is { } a ? (int)a : 0,
                    t.GetProperty("cells").EnumerateArray().Select(c => c.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList()))
                .ToList();
            var jogadores = calor.TryGetProperty("players", out var lista) ? lista.EnumerateArray()
                .Select(j => new CalorDoJogador(
                    Texto(j, "side") == "home" ? p.MandanteId : p.VisitanteId,
                    Texto(j, "player", "name") ?? "?",
                    Numero(j, "player", "shirt_number") is { } n ? (int)n : null,
                    Numero(j, "actions") is { } a ? (int)a : 0,
                    j.GetProperty("cells").EnumerateArray().Select(c => c.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToList()))
                .ToList() : [];
            return finalizacoes.Count == 0 && times.Count == 0 ? null : new MapaDaPartida(finalizacoes, times, "PitchAPI", jogadores);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Mapa da partida {Partida} indisponível na PitchAPI", p.Id);
            return null;
        }
    }

    public async Task<IReadOnlyList<Scout.Domain.EstiloNoJogo>?> ObterEstilo(ResumoPartida p, CancellationToken ct)
    {
        var chave = config["PITCHAPI_KEY"];
        if (string.IsNullOrWhiteSpace(chave) || !Ligas.TryGetValue(p.CompeticaoId, out var liga) || p.GolsMandante is null) return null;
        try
        {
            var jogos = await Buscar($"/v1/leagues/{liga}/matches?season={Uri.EscapeDataString(Temporada(p.CompeticaoId, p.Temporada))}", chave, TimeSpan.FromMinutes(30), ct);
            var jogo = Achar(jogos.GetProperty("matches"), p);
            if (jogo is null) return null;
            var (id, casaId, foraId) = jogo.Value;
            var adv = await Buscar($"/v1/matches/{id}/advanced", chave, TimeSpan.FromHours(12), ct);
            var r = adv.GetProperty("teams").EnumerateArray()
                .Select(t => (Time: Texto(t, "team", "id"), Dados: t))
                .Where(t => t.Time == casaId || t.Time == foraId)
                .Select(t => ColetaPitch.LerEstilo(t.Dados, p.Id, t.Time == casaId ? p.MandanteId : p.VisitanteId))
                .ToList();
            return r.Count == 2 ? r : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Métricas de time da partida {Partida} indisponíveis na PitchAPI", p.Id);
            return null;
        }
    }

    /// <summary>O jogo da lista com horário até 3 h do nosso e o mandante ou o visitante com o mesmo nome (sem acento e pontuação).</summary>
    public static (string Id, string? CasaId, string? ForaId)? Achar(JsonElement jogos, ResumoPartida p)
    {
        // A nossa data vem no horário de Brasília (ProcurarPartidasHandler.Resumir); a da PitchAPI, em UTC
        var nossa = p.Data.Kind == DateTimeKind.Utc ? p.Data
            : TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(p.Data, DateTimeKind.Unspecified), ProcurarPartidasHandler.Brasilia);
        foreach (var j in jogos.EnumerateArray())
        {
            if (!DateTime.TryParse(Texto(j, "time_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var quando)) continue;
            if (Math.Abs((quando - nossa).TotalHours) > 3) continue;
            if (Nome(Texto(j, "home_team", "name")) != Nome(p.Mandante) && Nome(Texto(j, "away_team", "name")) != Nome(p.Visitante)) continue;
            return (Texto(j, "id")!, Texto(j, "home_team", "id"), Texto(j, "away_team", "id"));
        }
        return null;
    }

    /// <summary>"Atlético-MG" e "Atletico-MG" viram "atleticomg".</summary>
    public static string Nome(string? nome)
    {
        var s = new StringBuilder();
        foreach (var c in (nome ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) s.Append(char.ToLowerInvariant(c));
        return s.ToString();
    }

    async Task<JsonElement> Buscar(string caminho, string chave, TimeSpan validade, CancellationToken ct) =>
        await cache.GetOrCreateAsync($"pitchapi:{caminho}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = validade;
            using var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
            pedido.Headers.Add("X-API-KEY", chave);
            using var r = await http.SendAsync(pedido, ct);
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").Clone();
        });

    static JsonElement? No(JsonElement e, string[] caminho)
    {
        foreach (var p in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        return e;
    }
    static string? Texto(JsonElement e, params string[] caminho) => No(e, caminho) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    static double? Numero(JsonElement e, params string[] caminho) => No(e, caminho) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
    static bool Logico(JsonElement e, params string[] caminho) => No(e, caminho) is { ValueKind: JsonValueKind.True };
}
