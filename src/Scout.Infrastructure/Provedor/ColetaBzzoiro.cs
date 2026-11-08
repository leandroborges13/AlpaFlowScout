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

    /// <summary>Copas na Bzzoiro → nossas competições (ids da API-Football): Copa do Brasil, Libertadores e Sul-Americana.</summary>
    static readonly Dictionary<int, int> Copas = new() { [35] = 73, [32] = 13, [33] = 11 };

    /// <summary>
    /// Desempenho dos atletas nos jogos de copa (que a PitchAPI não tem): /api/player-stats/?event={id}, uma consulta por jogo.
    /// Só jogos com time da Série A (o foco do Scout) e que já estão na nossa base; ligados pela data e pelo nome dos times.
    /// </summary>
    public async Task<ResultadoColetaPitch> ColetarCopas(int temporada, int maxJogos, CancellationToken ct)
    {
        var chave = config["BZZOIRO_KEY"];
        if (string.IsNullOrWhiteSpace(chave)) return new(0, 0, 0, 0);

        var clubes = await db.Clubes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        var daSerieA = (await db.Estatisticas.Where(e => e.CompeticaoId == 71 && e.Temporada == temporada).Select(e => e.ClubeId).Distinct().ToListAsync(ct)).ToHashSet();
        var nossas = Copas.Values.ToList();
        var partidas = await db.Partidas.Where(p => nossas.Contains(p.CompeticaoId) && p.Temporada == temporada).ToListAsync(ct);

        int jogos = 0, atletas = 0, ligados = 0, semPar = 0;
        var de = $"{temporada}-01-01";
        var ate = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        foreach (var (ligaBz, competicao) in Copas)
        {
            var eventos = (await TodasAsPaginas($"/api/events/?league={ligaBz}&date_from={de}&date_to={ate}", chave, ct))
                .Where(e => Texto(e, "status") == "finished")
                .OrderByDescending(e => Texto(e, "event_date"));
            foreach (var ev in eventos)
            {
                if (jogos >= maxJogos) break;
                if (!DateTime.TryParse(Texto(ev, "event_date"), System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal, out var quando) || Int(ev, "id") is not { } idBz) continue;
                var casa = Texto(ev, "home_team") ?? "";
                var fora = Texto(ev, "away_team") ?? "";
                var partida = partidas.FirstOrDefault(p => p.CompeticaoId == competicao && Math.Abs((p.Data - quando).TotalHours) <= 3
                    && (MesmoClube(clubes.GetValueOrDefault(p.MandanteId, ""), casa, null) || MesmoClube(clubes.GetValueOrDefault(p.VisitanteId, ""), fora, null)));
                if (partida is null) { semPar++; continue; }
                if (partida.BzzoiroImportada || !partida.Encerrada || !(daSerieA.Contains(partida.MandanteId) || daSerieA.Contains(partida.VisitanteId))) continue;

                var stats = await TodasAsPaginas($"/api/player-stats/?event={idBz}", chave, ct);
                if (stats.Count == 0) continue; // jogo sem números por atleta (ou freio da Bzzoiro): tenta de novo amanhã
                var (n, l) = await GravarCopa(partida, stats, clubes, ct);
                partida.BzzoiroId = idBz;
                partida.BzzoiroImportada = true;
                await db.SaveChangesAsync(ct);
                jogos++; atletas += n; ligados += l;
            }
        }
        log.LogInformation("Bzzoiro copas {Temporada}: {Jogos} jogos novos, {Ligados}/{Atletas} atletas ligados, {SemPar} jogos sem par na nossa base",
            temporada, jogos, ligados, atletas, semPar);
        return new(jogos, atletas, ligados, semPar);
    }

    async Task<(int Atletas, int Ligados)> GravarCopa(Domain.Partida partida, List<JsonElement> stats, Dictionary<int, string> clubes, CancellationToken ct)
    {
        int? Clube(string? time) =>
            time is null ? null
            : MesmoClube(clubes.GetValueOrDefault(partida.MandanteId, ""), time, null) ? partida.MandanteId
            : MesmoClube(clubes.GetValueOrDefault(partida.VisitanteId, ""), time, null) ? partida.VisitanteId
            : null;
        var ids = new[] { partida.MandanteId, partida.VisitanteId };
        var candidatos = (await db.Estatisticas.Where(e => ids.Contains(e.ClubeId) && e.Temporada == partida.Temporada)
                .Select(e => new { e.ClubeId, e.AtletaId, e.Atleta.Nome, e.Atleta.NomeCompleto }).ToListAsync(ct))
            .GroupBy(c => c.ClubeId)
            .ToDictionary(g => g.Key, g => g.Select(c => (c.AtletaId, c.Nome, c.NomeCompleto)).Distinct().ToList());

        await db.DesempenhosAvancados.Where(d => d.PartidaId == partida.Id && d.Fonte == "Bzzoiro").ExecuteDeleteAsync(ct);
        int n = 0, ligados = 0;
        var vistos = new HashSet<string>();
        foreach (var s in stats)
        {
            if (!s.TryGetProperty("player", out var p) || Int(p, "id") is not { } pid || !vistos.Add(pid.ToString())) continue;
            if (Clube(Texto(p, "team")) is not { } clubeId) continue;
            var minutos = (int)(Num(s, "minutes_played") ?? 0);
            if (minutos <= 0) continue;
            var nome = Texto(p, "name") ?? "?";
            var atletaId = candidatos.TryGetValue(clubeId, out var lista)
                ? LigarNomes.Ligar(nome, lista) ?? LigarNomes.Ligar(Texto(p, "short_name") ?? "", lista)
                : null;
            int I(string k) => (int)Math.Round(Num(s, k) ?? 0);
            db.DesempenhosAvancados.Add(new Domain.DesempenhoAvancado
            {
                PartidaId = partida.Id, ClubeId = clubeId, AtletaId = atletaId, Fonte = "Bzzoiro", JogadorFonteId = pid.ToString(), Nome = nome,
                Minutos = minutos, Nota = Num(s, "rating"), Gols = I("goals"), Assistencias = I("goal_assist"),
                Xg = Num(s, "expected_goals"), Xa = Num(s, "expected_assists"),
                Finalizacoes = I("total_shots"), FinalizacoesNoAlvo = I("shots_on_target"), Toques = I("touches"),
                Passes = I("total_pass"), PassesCertos = I("accurate_pass"), ChancesCriadas = I("key_pass"),
                BolasLongas = I("total_long_balls"), BolasLongasCertas = I("accurate_long_balls"),
                Cruzamentos = I("total_cross"), CruzamentosCertos = I("accurate_cross"),
                Dribles = I("dribble_attempted"), DriblesCertos = I("dribble_won"),
                Desarmes = I("total_tackle"), Interceptacoes = I("interception"), Recuperacoes = I("ball_recovery"), Cortes = I("total_clearance"),
                DuelosGanhos = I("duel_won"), DuelosPerdidos = I("duel_lost"),
                Aereos = I("aerial_won") + I("aerial_lost"), AereosGanhos = I("aerial_won"),
                FaltasCometidas = I("fouls"), FaltasSofridas = I("was_fouled"), PerdasDePosse = I("dispossessed"), Defesas = I("saves"),
                Bruto = s.GetRawText(),
            });
            n++;
            if (atletaId is not null) ligados++;
        }
        return (n, ligados);
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
    static double? Num(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static int? Int(JsonElement e, string p) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
