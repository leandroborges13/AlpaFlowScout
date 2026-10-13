using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;

namespace Scout.Infrastructure.Provedor;

/// <summary>Clubes por onde o treinador passou (/coachs). Consulta sob demanda e guarda por 7 dias.</summary>
public class CarreiraDoTreinador(ApiFootballClient api, ILogger<CarreiraDoTreinador> log) : ICarreiraDoTreinador
{
    static readonly TimeSpan Validade = TimeSpan.FromDays(7);

    public async Task<CarreiraTreinador?> Obter(int treinadorId, CancellationToken ct)
    {
        try
        {
            var r = (await api.Consultar("/coachs", new Dictionary<string, object> { ["id"] = treinadorId }, Validade, ct))
                .GetProperty("response").EnumerateArray().FirstOrDefault();
            if (r.ValueKind != JsonValueKind.Object) return null;
            return Ler(r);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Sem carreira o perfil abre do mesmo jeito
            log.LogWarning(e, "Carreira do treinador {Treinador} indisponível", treinadorId);
            return null;
        }
    }

    public static CarreiraTreinador Ler(JsonElement r)
    {
        var passagens = r.TryGetProperty("career", out var carreira) && carreira.ValueKind == JsonValueKind.Array
            ? carreira.EnumerateArray()
                .Select(c => new PassagemNaCarreira(Texto(c, "team", "name") ?? "?", Texto(c, "team", "logo"), Data(Texto(c, "start")), Data(Texto(c, "end"))))
                .OrderByDescending(p => p.Inicio ?? DateOnly.MinValue)
                .ToList()
            : [];
        int? idade = r.TryGetProperty("age", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : null;
        return new CarreiraTreinador(idade, Texto(r, "nationality"), passagens);
    }

    static DateOnly? Data(string? texto) => DateOnly.TryParse(texto, CultureInfo.InvariantCulture, out var d) ? d : null;

    static string? Texto(JsonElement e, params string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        return e.ValueKind == JsonValueKind.String ? e.GetString() : null;
    }
}
