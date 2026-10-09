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

            return new TrajetoriaAtleta(times, transferencias.OrderByDescending(t => t.Data).ToList());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem trajetória o perfil abre do mesmo jeito
            log.LogWarning(e, "Trajetória do atleta {Atleta} indisponível", atletaId);
            return null;
        }
    }

    static string? Texto(JsonElement e, params string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
