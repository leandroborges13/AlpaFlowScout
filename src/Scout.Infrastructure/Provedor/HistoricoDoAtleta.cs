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

            return new TrajetoriaAtleta(CompletarAnos(times, transferencias), transferencias.OrderByDescending(t => t.Data).ToList());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem trajetória o perfil abre do mesmo jeito
            log.LogWarning(e, "Trajetória do atleta {Atleta} indisponível", atletaId);
            return null;
        }
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
