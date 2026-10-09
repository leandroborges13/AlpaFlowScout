// Coleta de dados da API-Football para o banco do Scout.
//   dotnet run --project src/Scout.Coleta -- importar <liga> <temporada> [--sem-escalacoes]
//   dotnet run --project src/Scout.Coleta -- cota
//   dotnet run --project src/Scout.Coleta -- migrar
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scout.Infrastructure;
using Scout.Infrastructure.Dados;
using Scout.Infrastructure.Provedor;

ArquivoEnv.Carregar();
var builder = Host.CreateApplicationBuilder();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning).AddFilter("System.Net.Http", LogLevel.Warning).AddFilter("Polly", LogLevel.Warning);
builder.Services.AddScoutInfrastructure(builder.Configuration);
using var host = builder.Build();
using var escopo = host.Services.CreateScope();
var sp = escopo.ServiceProvider;
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    switch (args.FirstOrDefault())
    {
        case "migrar":
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            Console.WriteLine("Banco atualizado.");
            break;

        case "cota":
            Console.WriteLine($"Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "importar" when args.Length >= 3 && int.TryParse(args[1], out var liga) && int.TryParse(args[2], out var temporada):
            await sp.GetRequiredService<ScoutDbContext>().Database.MigrateAsync(cts.Token);
            var r = await sp.GetRequiredService<Importador>().Importar(liga, temporada, !args.Contains("--sem-escalacoes"), cts.Token);
            Console.WriteLine($"{r.Competicao} {r.Temporada}: {r.Clubes} clubes, {r.Atletas} atletas, {r.Partidas} partidas ({r.PartidasComEscalacao} com escalação)");
            Console.WriteLine($"Consultas hoje: {await sp.GetRequiredService<ApiFootballClient>().UsadasHoje(cts.Token)}");
            break;

        case "diagnostico" when args.Length >= 3 && int.TryParse(args[1], out var ligaD) && int.TryParse(args[2], out var temporadaD):
            var dbD = sp.GetRequiredService<ScoutDbContext>();
            await Diagnostico.CompeticoesDosElencos(dbD, ligaD, temporadaD, cts.Token);
            await Diagnostico.ConsistenciaDosLados(dbD, ligaD, temporadaD, cts.Token);
            break;

        default:
            Console.WriteLine("Comandos: migrar | cota | importar <liga> <temporada> [--sem-escalacoes]");
            Console.WriteLine("Ligas: 71 Série A · 72 Série B · 75 Série C · 76 Série D");
            return 1;
    }
    return 0;
}
catch (CotaEsgotadaException e)
{
    Console.Error.WriteLine(e.Message);
    return 2;
}
