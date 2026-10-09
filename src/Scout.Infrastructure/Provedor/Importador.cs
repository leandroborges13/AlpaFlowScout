using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Scout.Domain;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Provedor;

public record ResultadoImportacao(string Competicao, int Temporada, int Clubes, int Atletas, int Estatisticas, int Partidas, int PartidasComEscalacao);

/// <summary>Traz uma competição/temporada do provedor para o banco. Pode rodar de novo: atualiza o que mudou.</summary>
public class Importador(ApiFootballClient api, ScoutDbContext db, ILogger<Importador> log)
{
    public async Task<ResultadoImportacao> Importar(int ligaId, int temporada, bool comEscalacoes, CancellationToken ct)
    {
        // Temporada em andamento muda todo dia; encerrada fica guardada para sempre
        TimeSpan? validade = temporada >= DateTime.UtcNow.Year ? TimeSpan.FromHours(20) : null;
        var p = new Dictionary<string, object> { ["league"] = ligaId, ["season"] = temporada };

        var liga = (await api.Consultar("/leagues", new Dictionary<string, object> { ["id"] = ligaId }, TimeSpan.FromDays(7), ct))
            .GetProperty("response").EnumerateArray().First();
        var competicao = await db.Competicoes.FindAsync([ligaId], ct) ?? db.Competicoes.Add(new Competicao { Id = ligaId }).Entity;
        competicao.Nome = Texto(liga, "league", "name") ?? $"Liga {ligaId}";
        competicao.Tipo = Texto(liga, "league", "type") ?? "";
        competicao.Pais = Texto(liga, "country", "name") ?? "";

        var times = (await api.Consultar("/teams", p, validade, ct)).GetProperty("response").EnumerateArray().ToList();
        var clubes = await db.Clubes.ToDictionaryAsync(c => c.Id, ct);
        foreach (var t in times)
        {
            var id = Int(t, "team", "id")!.Value;
            if (!clubes.TryGetValue(id, out var clube)) clubes[id] = clube = db.Clubes.Add(new Clube { Id = id }).Entity;
            clube.Nome = Texto(t, "team", "name") ?? clube.Nome;
            clube.Escudo = Texto(t, "team", "logo");
        }
        await db.SaveChangesAsync(ct);
        log.LogInformation("{Competicao} {Temporada}: {Times} clubes", competicao.Nome, temporada, times.Count);

        // Jogadores time a time (cada elenco cabe em poucas páginas)
        var itens = new List<JsonElement>();
        foreach (var t in times)
        {
            var nome = Texto(t, "team", "name");
            var doTime = await api.ConsultarTodasPaginas("/players", new Dictionary<string, object> { ["team"] = Int(t, "team", "id")!.Value, ["season"] = temporada }, validade, ct);
            itens.AddRange(doTime);
            log.LogInformation("  {Clube}: {Jogadores} jogadores", nome, doTime.Count);
        }

        var atletas = await db.Atletas.ToDictionaryAsync(a => a.Id, ct);
        var estatisticas = await db.Estatisticas.Where(e => e.CompeticaoId == ligaId && e.Temporada == temporada)
            .ToDictionaryAsync(e => (e.AtletaId, e.ClubeId), ct);
        var agora = DateTime.UtcNow;
        var vistos = new HashSet<(int, int)>();

        foreach (var item in itens)
        {
            var j = item.GetProperty("player");
            var atletaId = Int(j, "id")!.Value;
            if (!atletas.TryGetValue(atletaId, out var atleta)) atletas[atletaId] = atleta = db.Atletas.Add(new Atleta { Id = atletaId }).Entity;
            atleta.Nome = Texto(j, "name") ?? atleta.Nome;
            atleta.NomeCompleto = string.Join(" ", new[] { Texto(j, "firstname"), Texto(j, "lastname") }.Where(s => !string.IsNullOrWhiteSpace(s)));
            atleta.Nascimento = DateOnly.TryParse(Texto(j, "birth", "date"), CultureInfo.InvariantCulture, out var nasc) ? nasc : atleta.Nascimento;
            atleta.Nacionalidade = Texto(j, "nationality");
            atleta.AlturaCm = Medida(Texto(j, "height"));
            atleta.PesoKg = Medida(Texto(j, "weight"));
            atleta.Foto = Texto(j, "photo");
            atleta.AtualizadoEm = agora;

            foreach (var s in item.GetProperty("statistics").EnumerateArray())
            {
                if (Int(s, "league", "id") != ligaId) continue;
                var clubeId = Int(s, "team", "id");
                if (clubeId is null || !clubes.ContainsKey(clubeId.Value)) continue;
                var chave = (atletaId, clubeId.Value);
                if (!vistos.Add(chave)) continue; // atleta repetido em dois elencos
                if (!estatisticas.TryGetValue(chave, out var e))
                    estatisticas[chave] = e = db.Estatisticas.Add(new EstatisticaTemporada { AtletaId = atletaId, ClubeId = clubeId.Value, CompeticaoId = ligaId, Temporada = temporada }).Entity;
                Preencher(e, s);
                e.AtualizadoEm = agora;
            }
        }
        await db.SaveChangesAsync(ct);

        // Partidas
        var jogos = (await api.Consultar("/fixtures", p, validade, ct)).GetProperty("response").EnumerateArray().ToList();
        var partidas = await db.Partidas.Where(x => x.CompeticaoId == ligaId && x.Temporada == temporada).ToDictionaryAsync(x => x.Id, ct);
        foreach (var f in jogos)
        {
            var id = Int(f, "fixture", "id")!.Value;
            if (!partidas.TryGetValue(id, out var partida))
                partidas[id] = partida = db.Partidas.Add(new Partida { Id = id, CompeticaoId = ligaId, Temporada = temporada }).Entity;
            partida.Data = DateTime.Parse(Texto(f, "fixture", "date")!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            partida.Status = Texto(f, "fixture", "status", "short") ?? "";
            partida.Rodada = Texto(f, "league", "round");
            partida.MandanteId = Int(f, "teams", "home", "id") ?? 0;
            partida.VisitanteId = Int(f, "teams", "away", "id") ?? 0;
            partida.GolsMandante = Int(f, "goals", "home");
            partida.GolsVisitante = Int(f, "goals", "away");
        }
        await db.SaveChangesAsync(ct);

        if (comEscalacoes)
            await ImportarEscalacoes(partidas.Values.Where(x => x.Encerrada && !x.EscalacoesImportadas).ToList(), ct);

        return new ResultadoImportacao(competicao.Nome, temporada, times.Count, vistos.Select(v => v.Item1).Distinct().Count(),
            vistos.Count, partidas.Count, partidas.Values.Count(x => x.EscalacoesImportadas));
    }

    async Task<int> ImportarEscalacoes(List<Partida> pendentes, CancellationToken ct)
    {
        var feitas = 0;
        foreach (var partida in pendentes)
        {
            // Partida encerrada não muda: guardada para sempre
            var times = (await api.Consultar("/fixtures/lineups", new Dictionary<string, object> { ["fixture"] = partida.Id }, null, ct))
                .GetProperty("response").EnumerateArray().ToList();
            var jaTem = await db.Escalacoes.Where(e => e.PartidaId == partida.Id).Select(e => e.AtletaId).ToListAsync(ct);
            var existentes = jaTem.ToHashSet();

            foreach (var t in times)
            {
                var esquema = Texto(t, "formation") ?? "";
                var clubeId = Int(t, "team", "id") ?? 0;
                if (!t.TryGetProperty("startXI", out var titulares) || titulares.ValueKind != JsonValueKind.Array) continue;
                foreach (var x in titulares.EnumerateArray())
                {
                    var j = x.GetProperty("player");
                    var atletaId = Int(j, "id");
                    if (atletaId is null || !existentes.Add(atletaId.Value)) continue;
                    var grade = Texto(j, "grid");
                    var posicao = FuncaoTatica.Identificar(esquema, grade);
                    if (posicao is null) continue;
                    db.Escalacoes.Add(new Escalacao
                    {
                        PartidaId = partida.Id, ClubeId = clubeId, AtletaId = atletaId.Value, Esquema = esquema, Grade = grade,
                        Setor = posicao.Setor, Lado = posicao.Lado, Funcao = posicao.Funcao,
                    });
                }
            }
            partida.EscalacoesImportadas = true;
            await db.SaveChangesAsync(ct);
            if (++feitas % 25 == 0) log.LogInformation("  escalações: {Feitas}/{Total}", feitas, pendentes.Count);
        }
        return feitas;
    }

    static void Preencher(EstatisticaTemporada e, JsonElement s)
    {
        e.PosicaoProvedor = Texto(s, "games", "position");
        e.Jogos = Int(s, "games", "appearences") ?? Int(s, "games", "lineups") ?? 0;
        e.Titular = Int(s, "games", "lineups") ?? 0;
        e.Minutos = Int(s, "games", "minutes") ?? 0;
        e.Gols = Int(s, "goals", "total") ?? 0;
        e.Assistencias = Int(s, "goals", "assists") ?? 0;
        e.GolsSofridos = Int(s, "goals", "conceded") ?? 0;
        e.Defesas = Int(s, "goals", "saves") ?? 0;
        e.Finalizacoes = Int(s, "shots", "total") ?? 0;
        e.FinalizacoesNoAlvo = Int(s, "shots", "on") ?? 0;
        e.Passes = Int(s, "passes", "total") ?? 0;
        e.PassesDecisivos = Int(s, "passes", "key") ?? 0;
        e.PrecisaoPasses = Int(s, "passes", "accuracy");
        e.Desarmes = Int(s, "tackles", "total") ?? 0;
        e.Bloqueios = Int(s, "tackles", "blocks") ?? 0;
        e.Interceptacoes = Int(s, "tackles", "interceptions") ?? 0;
        e.Duelos = Int(s, "duels", "total") ?? 0;
        e.DuelosGanhos = Int(s, "duels", "won") ?? 0;
        e.DriblesTentados = Int(s, "dribbles", "attempts") ?? 0;
        e.DriblesCertos = Int(s, "dribbles", "success") ?? 0;
        e.FaltasCometidas = Int(s, "fouls", "committed") ?? 0;
        e.FaltasSofridas = Int(s, "fouls", "drawn") ?? 0;
        e.Amarelos = Int(s, "cards", "yellow") ?? 0;
        e.Vermelhos = (Int(s, "cards", "red") ?? 0) + (Int(s, "cards", "yellowred") ?? 0);
    }

    static JsonElement? Caminho(JsonElement e, string[] caminho)
    {
        foreach (var parte in caminho)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        }
        return e.ValueKind == JsonValueKind.Null ? null : e;
    }

    static string? Texto(JsonElement e, params string[] caminho) => Caminho(e, caminho) is { } v
        ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()
        : null;

    static int? Int(JsonElement e, params string[] caminho) => Caminho(e, caminho) switch
    {
        { ValueKind: JsonValueKind.Number } v => v.TryGetInt32(out var i) ? i : (int)Math.Round(v.GetDouble()),
        { ValueKind: JsonValueKind.String } v => int.TryParse(v.GetString(), out var i) ? i : null,
        _ => null,
    };

    /// <summary>"180 cm" → 180.</summary>
    static int? Medida(string? texto) =>
        int.TryParse(new string((texto ?? "").TakeWhile(char.IsDigit).ToArray()), out var v) && v > 0 ? v : null;
}
