using Microsoft.Extensions.DependencyInjection;
using Scout.Application.Analise;
using Scout.Application.Chat;
using Scout.Application.Consultas;

namespace Scout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddScoutApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddScoped<MotorDeAnalise>();
        services.AddScoped<IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>>, ListarCompeticoesHandler>();
        services.AddScoped<IQueryHandler<BuscarPorPerfil, ResultadoBusca>, BuscarPorPerfilHandler>();
        services.AddScoped<IQueryHandler<ObterAtleta, DetalheAtleta?>, ObterAtletaHandler>();
        services.AddScoped<IQueryHandler<AtletasParecidos, ResultadoParecidos?>, AtletasParecidosHandler>();
        services.AddScoped<IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>>, BuscarOportunidadesHandler>();
        services.AddScoped<AssistenteScout>();
        return services;
    }
}
