using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Placar (/fixtures?id=) e números dos times (/fixtures/statistics) na hora, sem cópia guardada: o cartão de intervalo
/// precisa do número daquele momento, e o de fim de jogo do número já fechado. Cada pedido custa 2 consultas da cota.
/// </summary>
public class PlacarAoVivoDoProvedor(ApiFootballClient api, ILogger<PlacarAoVivoDoProvedor> log) : IPlacarAoVivo
{
    public async Task<PlacarAoVivo?> Obter(int partidaId, CancellationToken ct)
    {
        try
        {
            var f = (await api.Consultar("/fixtures", new Dictionary<string, object> { ["id"] = partidaId }, TimeSpan.Zero, ct, guardar: false))
                .GetProperty("response").EnumerateArray().FirstOrDefault();
            if (f.ValueKind != JsonValueKind.Object) return null;
            var stats = (await api.Consultar("/fixtures/statistics", new Dictionary<string, object> { ["fixture"] = partidaId }, TimeSpan.Zero, ct, guardar: false))
                .GetProperty("response").EnumerateArray().ToList();
            return Ler(partidaId, f, stats);
        }
        catch (Exception e) when (e is not OperationCanceledException and not CotaEsgotadaException)
        {
            log.LogWarning(e, "Placar ao vivo da partida {Partida} indisponível", partidaId);
            return null;
        }
    }

    public static PlacarAoVivo Ler(int partidaId, JsonElement f, List<JsonElement> stats)
    {
        LadoAoVivo Lado(string lado, string gols)
        {
            var t = f.GetProperty("teams").GetProperty(lado);
            var id = t.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : 0;
            var nome = t.TryGetProperty("name", out var n) ? n.GetString() ?? "?" : "?";
            int? g = f.GetProperty("goals").TryGetProperty(gols, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : null;
            var doTime = stats.FirstOrDefault(s => s.TryGetProperty("team", out var st) && st.TryGetProperty("id", out var sid)
                && sid.ValueKind == JsonValueKind.Number && sid.GetInt32() == id);
            if (doTime.ValueKind != JsonValueKind.Object || !doTime.TryGetProperty("statistics", out var lista) || lista.ValueKind != JsonValueKind.Array)
                return new LadoAoVivo(id, nome, g, null, null, null, null, null);
            var v = Importador.LerEstatisticas(lista);
            int? V(string tipo) => v.TryGetValue(tipo, out var n2) ? n2 : null;
            return new LadoAoVivo(id, nome, g, V("Ball Possession"), V("Total Shots"), V("Shots on Goal"), V("Corner Kicks"), Importador.LerXg(lista));
        }
        var status = f.GetProperty("fixture").GetProperty("status");
        var curto = status.TryGetProperty("short", out var c) ? c.GetString() ?? "" : "";
        int? minuto = status.TryGetProperty("elapsed", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;
        return new PlacarAoVivo(partidaId, curto, minuto, Lado("home", "home"), Lado("away", "away"));
    }
}
