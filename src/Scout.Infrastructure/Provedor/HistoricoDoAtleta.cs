using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Clubes por temporada (/players/teams) e transferências (/transfers) do atleta.
/// Consulta sob demanda, quando o perfil é aberto, e guarda por 7 dias.
/// </summary>
public class HistoricoDoAtleta(ApiFootballClient api, ILogger<HistoricoDoAtleta> log) : IHistoricoDoAtleta
{
    static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    public async Task<TrajetoriaAtleta?> Obter(int atletaId, CancellationToken ct)
    {
        try
        {
            var p = new Dictionary<string, object> { ["player"] = atletaId };
            var times = (await api.Consultar("/players/teams", p, Validade, ct)).GetProperty("response").EnumerateArray()
                .Select(t => new ClubeNaCarreira(
                    t.GetProperty("team").GetProperty("name").GetString() ?? "?",
                    t.GetProperty("team").TryGetProperty("logo", out var logo) ? logo.GetString() : null,
                    t.GetProperty("seasons").EnumerateArray().Select(s => s.GetInt32()).OrderDescending().ToList()))
                .OrderByDescending(c => c.Temporadas.FirstOrDefault())
                .ToList();

            var transferencias = new List<Transferencia>();
            foreach (var item in (await api.Consultar("/transfers", p, Validade, ct)).GetProperty("response").EnumerateArray())
                foreach (var t in item.GetProperty("transfers").EnumerateArray())
                    transferencias.Add(new Transferencia(
                        DateOnly.TryParse(Texto(t, "date"), CultureInfo.InvariantCulture, out var d) ? d : null,
                        Texto(t, "teams", "out", "name"), Texto(t, "teams", "in", "name"), Texto(t, "type")));

            var clubes = CompletarAnos(times, transferencias);
            var numeros = await NumerosPorTemporada(atletaId, clubes, ct);
            clubes = clubes.Select(c => numeros.TryGetValue(c.Clube, out var n)
                ? c with { Numeros = n.Linhas.OrderByDescending(x => x.Temporada).ToList(), Selecao = n.Selecao } : c).ToList();
            return new TrajetoriaAtleta(clubes, transferencias.OrderByDescending(t => t.Data).ToList());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem trajetória o perfil abre do mesmo jeito
            log.LogWarning(e, "Trajetória do atleta {Atleta} indisponível", atletaId);
            return null;
        }
    }

    /// <summary>Quantas temporadas para trás buscar os números (1 consulta por temporada).</summary>
    public const int TemporadasComNumeros = 8;

    /// <summary>
    /// Jogos, gols e assistências por clube e temporada (/players?id=&amp;season=, que traz cada time e competição da temporada).
    /// Temporada passada fica guardada para sempre (não muda mais); a atual vale 1 dia. Temporada que falhar (cota, plano) fica
    /// de fora sem derrubar a trajetória.
    /// </summary>
    async Task<Dictionary<string, (List<NumerosNaTemporada> Linhas, bool Selecao)>> NumerosPorTemporada(int atletaId, List<ClubeNaCarreira> clubes, CancellationToken ct)
    {
        var anoAtual = DateTime.UtcNow.Year;
        var temporadas = clubes.SelectMany(c => c.Temporadas).Where(t => t > anoAtual - TemporadasComNumeros).Distinct().OrderDescending().ToList();
        var porClube = new Dictionary<string, (List<NumerosNaTemporada> Linhas, bool Selecao)>(StringComparer.OrdinalIgnoreCase);
        foreach (var temporada in temporadas)
        {
            JsonElement raiz;
            try
            {
                raiz = await api.Consultar("/players", new Dictionary<string, object> { ["id"] = atletaId, ["season"] = temporada },
                    temporada >= anoAtual - 1 ? TimeSpan.FromDays(1) : null, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogInformation("Números de {Atleta} em {Temporada} indisponíveis: {Erro}", atletaId, temporada, e.Message);
                continue;
            }
            foreach (var item in raiz.GetProperty("response").EnumerateArray())
            {
                var nacionalidade = Texto(item, "player", "nationality");
                foreach (var (clube, n) in Somar(item.GetProperty("statistics"), temporada))
                {
                    if (!porClube.TryGetValue(clube, out var v)) porClube[clube] = v = ([], EhSelecao(clube, nacionalidade));
                    v.Linhas.Add(n);
                }
            }
        }
        return porClube;
    }

    /// <summary>Soma as competições de cada time na temporada (liga + copas). Público para os testes.</summary>
    public static IEnumerable<(string Clube, NumerosNaTemporada Numeros)> Somar(JsonElement estatisticas, int temporada) =>
        estatisticas.EnumerateArray()
            .GroupBy(s => Texto(s, "team", "name") ?? "?")
            .Select(g => (g.Key, new NumerosNaTemporada(temporada,
                g.Sum(s => Inteiro(s, "games", "appearences")), g.Sum(s => Inteiro(s, "games", "minutes")),
                g.Sum(s => Inteiro(s, "goals", "total")), g.Sum(s => Inteiro(s, "goals", "assists")))))
            .Where(x => x.Item2.Jogos > 0);

    /// <summary>Seleção: o nome do time é o país do atleta (inclui as de base, "Argentina U20").</summary>
    public static bool EhSelecao(string clube, string? nacionalidade) =>
        !string.IsNullOrWhiteSpace(nacionalidade) && (Mesmo(clube, nacionalidade) || clube.StartsWith(nacionalidade + " U", StringComparison.OrdinalIgnoreCase));

    static int Inteiro(JsonElement e, params string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return 0;
        return e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;
    }

    /// <summary>
    /// O provedor às vezes não informa as temporadas de um clube (comum em times B e de base).
    /// Nesses casos, usa os anos das transferências de entrada e saída do clube.
    /// </summary>
    public static List<ClubeNaCarreira> CompletarAnos(List<ClubeNaCarreira> clubes, List<Transferencia> transferencias) =>
        clubes.Select(c =>
            {
                if (c.Temporadas.Count > 0) return c;
                var anos = transferencias
                    .Where(t => t.Data is not null && (Mesmo(t.Para, c.Clube) || Mesmo(t.De, c.Clube)))
                    .Select(t => t.Data!.Value.Year)
                    .Distinct().OrderDescending().ToList();
                return c with { Temporadas = anos };
            })
            .OrderByDescending(c => c.Temporadas.DefaultIfEmpty(0).Max())
            .ToList();

    static bool Mesmo(string? a, string b) => string.Equals(a?.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    static string? Texto(JsonElement e, params string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
