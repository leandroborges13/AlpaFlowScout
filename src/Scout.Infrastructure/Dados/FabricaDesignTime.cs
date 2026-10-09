using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scout.Infrastructure.Dados;

/// <summary>Usada só pelo `dotnet ef` para gerar migrações (não conecta no banco).</summary>
public class FabricaDesignTime : IDesignTimeDbContextFactory<ScoutDbContext>
{
    public ScoutDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ScoutDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("SCOUT_DB") ?? "Host=localhost;Database=alpaflow_scout")
            .UseSnakeCaseNamingConvention()
            .Options);
}
