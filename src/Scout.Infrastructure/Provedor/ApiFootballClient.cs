using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Provedor;

public class ApiFootballOptions
{
    public string Chave { get; set; } = "";
    public int LimiteDia { get; set; } = 95;
    /// <summary>Intervalo entre chamadas reais (o plano Pro permite 300 por minuto).</summary>
    public int IntervaloMs { get; set; } = 250;
}

public class CotaEsgotadaException(string mensagem) : Exception(mensagem);

/// <summary>
/// Cliente da API-Football v3. Toda resposta fica em `respostas_provedor`; uma consulta repetida
/// volta do banco. A cota do dia é contada no banco e a chamada para antes de estourar.
/// </summary>
public class ApiFootballClient(HttpClient http, IDbContextFactory<ScoutDbContext> fabrica, IOptions<ApiFootballOptions> opcoes, ILogger<ApiFootballClient> log)
{
    static DateTime ultimaChamada = DateTime.MinValue;
    static readonly SemaphoreSlim fila = new(1, 1);

    /// <param name="validade">null = guarda para sempre (temporada encerrada não muda).</param>
    public async Task<JsonElement> Consultar(string endpoint, IDictionary<string, object> parametros, TimeSpan? validade, CancellationToken ct)
    {
        var query = string.Join("&", parametros.OrderBy(p => p.Key).Select(p => $"{p.Key}={Uri.EscapeDataString(Convert.ToString(p.Value)!)}"));
        var chave = $"{endpoint}?{query}";

        await using var db = await fabrica.CreateDbContextAsync(ct);
        var guardada = await db.RespostasProvedor.AsNoTracking().FirstOrDefaultAsync(r => r.Chave == chave, ct);
        if (guardada is not null && (validade is null || DateTime.UtcNow - guardada.ObtidaEm < validade))
            return JsonDocument.Parse(guardada.Json).RootElement;

        if (string.IsNullOrWhiteSpace(opcoes.Value.Chave))
            throw new InvalidOperationException("Defina APIFOOTBALL_KEY no arquivo .env da raiz do projeto.");

        await fila.WaitAsync(ct);
        try
        {
            var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
            var cota = await db.ConsultasDiarias.FirstOrDefaultAsync(c => c.Dia == hoje, ct);
            if (cota is null) db.ConsultasDiarias.Add(cota = new ConsultaDiaria { Dia = hoje });
            if (cota.Usadas >= opcoes.Value.LimiteDia)
                throw new CotaEsgotadaException($"Cota do dia atingida ({cota.Usadas}/{opcoes.Value.LimiteDia}). O que já veio está guardado; rode de novo amanhã.");

            var espera = ultimaChamada.AddMilliseconds(opcoes.Value.IntervaloMs) - DateTime.UtcNow;
            if (espera > TimeSpan.Zero) await Task.Delay(espera, ct);
            ultimaChamada = DateTime.UtcNow;

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}?{query}");
            req.Headers.Add("x-apisports-key", opcoes.Value.Chave);
            using var resp = await http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            cota.Usadas++;

            var raiz = JsonDocument.Parse(json).RootElement;
            // A API responde 200 com o problema em "errors" (chave inválida, temporada fora do plano...)
            if (raiz.TryGetProperty("errors", out var erros) && TemErro(erros))
            {
                await db.SaveChangesAsync(ct);
                throw new InvalidOperationException($"API-Football: {erros.GetRawText()}");
            }

            if (guardada is null) db.RespostasProvedor.Add(new RespostaProvedor { Chave = chave, Json = json, ObtidaEm = DateTime.UtcNow });
            else db.RespostasProvedor.Update(new RespostaProvedor { Chave = chave, Json = json, ObtidaEm = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
            log.LogDebug("API-Football {Chave} ({Usadas} hoje)", chave, cota.Usadas);
            return raiz;
        }
        finally
        {
            fila.Release();
        }
    }

    /// <summary>Todas as páginas de um endpoint paginado.</summary>
    public async Task<List<JsonElement>> ConsultarTodasPaginas(string endpoint, IDictionary<string, object> parametros, TimeSpan? validade, CancellationToken ct)
    {
        var itens = new List<JsonElement>();
        for (int pagina = 1, total = 1; pagina <= total; pagina++)
        {
            var raiz = await Consultar(endpoint, new Dictionary<string, object>(parametros) { ["page"] = pagina }, validade, ct);
            itens.AddRange(raiz.GetProperty("response").EnumerateArray());
            total = raiz.TryGetProperty("paging", out var p) && p.TryGetProperty("total", out var t) ? t.GetInt32() : 1;
        }
        return itens;
    }

    public async Task<int> UsadasHoje(CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var hoje = DateOnly.FromDateTime(DateTime.UtcNow);
        return await db.ConsultasDiarias.Where(c => c.Dia == hoje).Select(c => c.Usadas).FirstOrDefaultAsync(ct);
    }

    static bool TemErro(JsonElement erros) => erros.ValueKind switch
    {
        JsonValueKind.Array => erros.GetArrayLength() > 0,
        JsonValueKind.Object => erros.EnumerateObject().Any(),
        _ => false,
    };
}
