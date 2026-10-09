using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Importa as rodadas novas uma vez por dia. Configuração:
///   SCOUT_ATUALIZAR=71:2026,72:2026,75:2026   (liga:temporada)
///   SCOUT_ATUALIZAR_HORA=6                      (hora de Brasília)
/// Sem SCOUT_ATUALIZAR, não faz nada (útil no ambiente local).
/// </summary>
public class AtualizacaoDiaria(IServiceScopeFactory escopos, IConfiguration config, ILogger<AtualizacaoDiaria> log) : BackgroundService
{
    static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static List<(int Liga, int Temporada)> Competicoes(string? texto) =>
        (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(':'))
            .Where(p => p.Length == 2 && int.TryParse(p[0], out _) && int.TryParse(p[1], out _))
            .Select(p => (int.Parse(p[0]), int.Parse(p[1])))
            .ToList();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var competicoes = Competicoes(config["SCOUT_ATUALIZAR"]);
        if (competicoes.Count == 0) return;
        var hora = int.TryParse(config["SCOUT_ATUALIZAR_HORA"], out var h) ? h : 6;

        while (!ct.IsCancellationRequested)
        {
            var agora = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia);
            var proxima = new DateTimeOffset(agora.Date.AddHours(hora), agora.Offset);
            if (proxima <= agora) proxima = proxima.AddDays(1);
            log.LogInformation("Próxima atualização: {Quando:dd/MM HH:mm}", proxima);
            await Task.Delay(proxima - agora, ct);

            foreach (var (liga, temporada) in competicoes)
            {
                try
                {
                    using var escopo = escopos.CreateScope();
                    var r = await escopo.ServiceProvider.GetRequiredService<Importador>().Importar(liga, temporada, true, ct);
                    log.LogInformation("Atualizado: {Competicao} {Temporada} ({Partidas} partidas)", r.Competicao, r.Temporada, r.PartidasComEscalacao);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Falha ao atualizar a liga {Liga}/{Temporada}", liga, temporada);
                }
            }
        }
    }
}
