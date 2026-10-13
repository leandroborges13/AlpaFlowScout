using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scout.Application;
using Scout.Infrastructure.Dados;
using Scout.Infrastructure.Provedor;

namespace Scout.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Configuração vem do .env da raiz do repositório (SCOUT_DB, APIFOOTBALL_KEY, APIFOOTBALL_LIMITE_DIA, OPENAI_API_KEY).
    /// </summary>
    public static IServiceCollection AddScoutInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var conexao = config["SCOUT_DB"]
            ?? throw new InvalidOperationException("Defina SCOUT_DB no .env (ex.: Host=localhost;Database=alpaflow_scout;Username=...;Password=...)");

        void Banco(DbContextOptionsBuilder o) => o.UseNpgsql(conexao).UseSnakeCaseNamingConvention();
        services.AddDbContext<ScoutDbContext>(Banco);
        services.AddDbContextFactory<ScoutDbContext>(Banco, ServiceLifetime.Scoped);
        services.AddScoped<IScoutDb>(sp => sp.GetRequiredService<ScoutDbContext>());

        services.Configure<ApiFootballOptions>(o =>
        {
            o.Chave = config["APIFOOTBALL_KEY"] ?? "";
            if (int.TryParse(config["APIFOOTBALL_LIMITE_DIA"], out var limite)) o.LimiteDia = limite;
        });
        services.AddHttpClient<ApiFootballClient>(c => c.BaseAddress = new Uri("https://v3.football.api-sports.io"))
            .AddStandardResilienceHandler(); // novas tentativas e circuit breaker (Polly)
        services.AddScoped<Importador>();
        services.AddScoped<Scout.Application.Consultas.IHistoricoDoAtleta, HistoricoDoAtleta>();
        services.AddScoped<Scout.Application.Consultas.ICarreiraDoTreinador, CarreiraDoTreinador>();
        services.AddScoped<Acesso.Usuarios>();
        return services;
    }
}

/// <summary>Carrega o .env da raiz para as variáveis de ambiente (sem sobrescrever as que já existem).</summary>
public static class ArquivoEnv
{
    public static void Carregar()
    {
        var pasta = new DirectoryInfo(AppContext.BaseDirectory);
        while (pasta is not null && !File.Exists(Path.Combine(pasta.FullName, ".env"))) pasta = pasta.Parent;
        if (pasta is null) return;
        foreach (var linha in File.ReadAllLines(Path.Combine(pasta.FullName, ".env")))
        {
            var l = linha.Trim();
            if (l.Length == 0 || l.StartsWith('#') || !l.Contains('=')) continue;
            var i = l.IndexOf('=');
            var chave = l[..i].Trim();
            var valor = l[(i + 1)..].Trim().Trim('"');
            if (Environment.GetEnvironmentVariable(chave) is null) Environment.SetEnvironmentVariable(chave, valor);
        }
    }
}
