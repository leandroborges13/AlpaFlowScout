using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;
using Scout.Domain;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Provedor;

public record ResultadoColetaPitch(int Jogos, int Atletas, int Ligados, int SemPar);

/// <summary>
/// Desempenho avançado dos atletas jogo a jogo pela PitchAPI (grátis): /v1/matches/{id}/players e /advanced/players, 2 consultas
/// por jogo. Liga o jogo da PitchAPI ao nosso pela data e pelo nome dos times, e o atleta pelo nome entre os do clube
/// (<see cref="LigarNomes"/>). Jogo já coletado não é buscado de novo. A PitchAPI não publica cota: vai devagar e para no 429.
/// </summary>
public class ColetaPitch(HttpClient http, ScoutDbContext db, IConfiguration config, ILogger<ColetaPitch> log)
{
    const int IntervaloMs = 800;

    public async Task<ResultadoColetaPitch> Coletar(int competicaoId, int temporada, int maxJogos, CancellationToken ct)
    {
        var chave = config["PITCHAPI_KEY"];
        if (string.IsNullOrWhiteSpace(chave) || !PitchApi.Ligas.TryGetValue(competicaoId, out var liga)) return new(0, 0, 0, 0);

        var lista = await Buscar($"/v1/leagues/{liga}/matches?season={temporada}", chave, ct);
        if (lista is null) return new(0, 0, 0, 0);
        var partidas = await db.Partidas.Where(p => p.CompeticaoId == competicaoId && p.Temporada == temporada).ToListAsync(ct);
        var clubes = await db.Clubes.ToDictionaryAsync(c => c.Id, c => PitchApi.Nome(c.Nome), ct);

        int jogos = 0, atletas = 0, ligados = 0, semPar = 0;
        var finalizados = lista.Value.GetProperty("matches").EnumerateArray()
            .Where(m => Texto(m, "status") == "finished")
            .OrderByDescending(m => Texto(m, "time_utc"));
        foreach (var m in finalizados)
        {
            if (jogos >= maxJogos) break;
            var partida = Achar(m, partidas, clubes);
            if (partida is null) { semPar++; continue; }
            if (partida.PitchImportada || !partida.Encerrada) continue;

            var id = Texto(m, "id")!;
            var basico = await Buscar($"/v1/matches/{id}/players", chave, ct);
            var avancado = await Buscar($"/v1/matches/{id}/advanced/players", chave, ct);
            if (basico is null || avancado is null) break; // freio da PitchAPI: continua amanhã

            var times = new Dictionary<string, int>
            {
                [Texto(m, "home_team", "id") ?? "casa"] = partida.MandanteId,
                [Texto(m, "away_team", "id") ?? "fora"] = partida.VisitanteId,
            };
            var (n, l) = await Gravar(partida, basico.Value, avancado.Value, times, ct);
            partida.PitchId = id;
            partida.PitchImportada = true;
            await db.SaveChangesAsync(ct);
            jogos++; atletas += n; ligados += l;
            if (jogos % 20 == 0) log.LogInformation("  PitchAPI: {Jogos} jogos, {Ligados}/{Atletas} atletas ligados", jogos, ligados, atletas);
        }
        log.LogInformation("PitchAPI {Competicao}/{Temporada}: {Jogos} jogos novos, {Ligados}/{Atletas} atletas ligados, {SemPar} jogos sem par na nossa base",
            competicaoId, temporada, jogos, ligados, atletas, semPar);
        return new(jogos, atletas, ligados, semPar);
    }

    /// <summary>O nosso jogo com horário até 3 h do da PitchAPI e o mandante ou o visitante com o mesmo nome.</summary>
    static Partida? Achar(JsonElement m, List<Partida> partidas, Dictionary<int, string> clubes)
    {
        if (!DateTime.TryParse(Texto(m, "time_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var quando)) return null;
        var casa = PitchApi.Nome(Texto(m, "home_team", "name"));
        var fora = PitchApi.Nome(Texto(m, "away_team", "name"));
        return partidas.FirstOrDefault(p => Math.Abs((p.Data - quando).TotalHours) <= 3
            && (clubes.GetValueOrDefault(p.MandanteId) == casa || clubes.GetValueOrDefault(p.VisitanteId) == fora));
    }

    async Task<(int Atletas, int Ligados)> Gravar(Partida partida, JsonElement basico, JsonElement avancado, Dictionary<string, int> times, CancellationToken ct)
    {
        var avancados = avancado.GetProperty("players").EnumerateArray()
            .Where(a => Texto(a, "player", "id") is not null)
            .ToDictionary(a => Texto(a, "player", "id")!);
        // Candidatos: quem tem estatística pelo clube na temporada (qualquer competição) e quem foi escalado neste jogo
        var clubeIds = times.Values.ToList();
        var candidatos = (await db.Estatisticas
                .Where(e => clubeIds.Contains(e.ClubeId) && e.Temporada == partida.Temporada)
                .Select(e => new { e.ClubeId, e.AtletaId, e.Atleta.Nome, e.Atleta.NomeCompleto }).ToListAsync(ct))
            .Concat(await db.Escalacoes.Where(e => e.PartidaId == partida.Id)
                .Join(db.Atletas, e => e.AtletaId, a => a.Id, (e, a) => new { e.ClubeId, e.AtletaId, a.Nome, a.NomeCompleto }).ToListAsync(ct))
            .GroupBy(c => c.ClubeId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.AtletaId, c.Nome, c.NomeCompleto)).Distinct().ToList());

        await db.DesempenhosAvancados.Where(d => d.PartidaId == partida.Id).ExecuteDeleteAsync(ct);
        int n = 0, ligados = 0;
        foreach (var item in basico.EnumerateArray())
        {
            var pid = Texto(item, "player", "id");
            if (pid is null || !times.TryGetValue(Texto(item, "team_id") ?? "", out var clubeId)) continue;
            var s = Valores(item);
            var minutos = Inteiro(s, "minutes_played");
            if (minutos <= 0) continue; // reserva que não entrou
            avancados.TryGetValue(pid, out var adv);
            var nome = Texto(item, "player", "name") ?? "?";
            var atletaId = candidatos.TryGetValue(clubeId, out var lista) ? LigarNomes.Ligar(nome, lista) : null;
            var d = new DesempenhoAvancado
            {
                PartidaId = partida.Id, ClubeId = clubeId, AtletaId = atletaId, PitchJogadorId = pid, Nome = nome, Minutos = minutos,
                Nota = Numero(s, "rating_title"), Gols = Inteiro(s, "goals"), Assistencias = Inteiro(s, "assists"),
                Xg = Numero(s, "expected_goals"), Xa = Numero(s, "expected_assists"),
                Finalizacoes = Inteiro(s, "total_shots"), FinalizacoesNoAlvo = Inteiro(s, "ShotsOnTarget"),
                Toques = Inteiro(s, "touches"), ToquesNaArea = Inteiro(s, "touches_opp_box"),
                Passes = Total(s, "accurate_passes"), PassesCertos = Inteiro(s, "accurate_passes"),
                PassesTercoFinal = Inteiro(s, "passes_into_final_third"),
                BolasLongas = Total(s, "long_balls_accurate"), BolasLongasCertas = Inteiro(s, "long_balls_accurate"),
                Cruzamentos = Total(s, "accurate_crosses"), CruzamentosCertos = Inteiro(s, "accurate_crosses"),
                ChancesCriadas = Inteiro(s, "chances_created"),
                Dribles = Total(s, "dribbles_succeeded"), DriblesCertos = Inteiro(s, "dribbles_succeeded"),
                Desarmes = Inteiro(s, "matchstats.headers.tackles"), Interceptacoes = Inteiro(s, "interceptions"),
                Recuperacoes = Inteiro(s, "recoveries"), Cortes = Inteiro(s, "clearances"),
                DuelosGanhos = Inteiro(s, "duel_won"), DuelosPerdidos = Inteiro(s, "duel_lost"),
                Aereos = Total(s, "aerials_won"), AereosGanhos = Inteiro(s, "aerials_won"),
                FaltasCometidas = Inteiro(s, "fouls"), FaltasSofridas = Inteiro(s, "was_fouled"), PerdasDePosse = Inteiro(s, "dispossessed"),
                Defesas = Inteiro(s, "saves"), GolsEvitados = Numero(s, "goals_prevented"),
                Bruto = new JsonObject { ["players"] = JsonNode.Parse(item.GetRawText()), ["advanced"] = adv.ValueKind == JsonValueKind.Object ? JsonNode.Parse(adv.GetRawText()) : null }.ToJsonString(),
            };
            if (adv.ValueKind == JsonValueKind.Object)
            {
                d.PassesProgressivos = (int)(Num(adv, "passing", "progressive_passes") ?? 0);
                d.PassesNaArea = (int)(Num(adv, "passing", "passes_into_box") ?? 0);
                d.ConducoesProgressivas = (int)(Num(adv, "carrying", "progressive_carries") ?? 0);
                d.ConducoesTercoFinal = (int)(Num(adv, "carrying", "carries_into_final_third") ?? 0);
                d.Sca = (int)(Num(adv, "creation", "sca") ?? 0);
                d.Gca = (int)(Num(adv, "creation", "gca") ?? 0);
                d.XgChain = Num(adv, "creation", "xg_chain");
                d.Xt = Num(adv, "possession_value", "xt_total");
                d.Vaep = Num(adv, "possession_value", "vaep_total");
            }
            db.DesempenhosAvancados.Add(d);
            n++;
            if (atletaId is not null) ligados++;
        }
        return (n, ligados);
    }

    /// <summary>Os blocos de /players (top_stats, attack, defense, duels...) achatados: chave → (valor, total).</summary>
    public static Dictionary<string, (double Valor, double? Total)> Valores(JsonElement item)
    {
        var r = new Dictionary<string, (double, double?)>();
        if (!item.TryGetProperty("stats", out var grupos) || grupos.ValueKind != JsonValueKind.Array) return r;
        foreach (var g in grupos.EnumerateArray())
        {
            if (!g.TryGetProperty("stats", out var stats) || stats.ValueKind != JsonValueKind.Object) continue;
            foreach (var p in stats.EnumerateObject())
            {
                var chave = Texto(p.Value, "key");
                if (string.IsNullOrEmpty(chave) || r.ContainsKey(chave) || !p.Value.TryGetProperty("stat", out var st)) continue;
                if (Num(st, "value") is not { } v) continue;
                r[chave] = (v, Num(st, "total"));
            }
        }
        return r;
    }

    static int Inteiro(Dictionary<string, (double Valor, double? Total)> s, string k) => s.TryGetValue(k, out var v) ? (int)Math.Round(v.Valor) : 0;
    static double? Numero(Dictionary<string, (double Valor, double? Total)> s, string k) => s.TryGetValue(k, out var v) ? v.Valor : null;
    static int Total(Dictionary<string, (double Valor, double? Total)> s, string k) => s.TryGetValue(k, out var v) && v.Total is { } t ? (int)Math.Round(t) : 0;

    DateTime ultima = DateTime.MinValue;

    /// <summary>GET com intervalo entre chamadas; nulo no 429 (freio) ou em falha, para a coleta parar e seguir no dia seguinte.</summary>
    async Task<JsonElement?> Buscar(string caminho, string chave, CancellationToken ct)
    {
        var espera = ultima.AddMilliseconds(IntervaloMs) - DateTime.UtcNow;
        if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
        ultima = DateTime.UtcNow;
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, caminho);
            pedido.Headers.Add("X-API-KEY", chave);
            using var r = await http.SendAsync(pedido, ct);
            if (r.StatusCode == HttpStatusCode.TooManyRequests) { log.LogWarning("PitchAPI pediu para esperar (429) em {Caminho}", caminho); return null; }
            r.EnsureSuccessStatusCode();
            return (await r.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("data").Clone();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "PitchAPI falhou em {Caminho}", caminho);
            return null;
        }
    }

    static JsonElement? No(JsonElement e, string[] caminho)
    {
        foreach (var p in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e)) return null;
        return e;
    }
    static string? Texto(JsonElement e, params string[] caminho) => No(e, caminho) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    static double? Num(JsonElement e, params string[] caminho) => No(e, caminho) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
}
