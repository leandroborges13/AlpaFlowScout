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
        services.AddScoped<AnaliseDeTimes>();
        services.AddScoped<IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>>, ListarCompeticoesHandler>();
        services.AddScoped<IQueryHandler<BuscarPorPerfil, ResultadoBusca>, BuscarPorPerfilHandler>();
        services.AddScoped<IQueryHandler<ObterAtleta, DetalheAtleta?>, ObterAtletaHandler>();
        services.AddScoped<IQueryHandler<AtletasParecidos, ResultadoParecidos?>, AtletasParecidosHandler>();
        services.AddScoped<IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>>, BuscarOportunidadesHandler>();
        services.AddScoped<IQueryHandler<ListarTimes, IReadOnlyList<ResumoTime>>, ListarTimesHandler>();
        services.AddScoped<IQueryHandler<ObterTime, DetalheTime?>, ObterTimeHandler>();
        services.AddScoped<IQueryHandler<BuscarTimesComCarencia, IReadOnlyList<TimeComCarencia>>, BuscarTimesComCarenciaHandler>();
        services.AddScoped<IQueryHandler<TimesParaAtleta, ResultadoTimesParaAtleta?>, TimesParaAtletaHandler>();
        services.AddScoped<IQueryHandler<ListarTreinadores, IReadOnlyList<ResumoTreinador>>, ListarTreinadoresHandler>();
        services.AddScoped<IQueryHandler<ObterTreinador, DetalheTreinador?>, ObterTreinadorHandler>();
        services.AddScoped<IQueryHandler<ProcurarPartidas, IReadOnlyList<ResumoPartida>>, ProcurarPartidasHandler>();
        services.AddScoped<IQueryHandler<AnalisarPartida, AnalisePartida?>, AnalisarPartidaHandler>();
        services.AddScoped<IQueryHandler<AtletasDoTreinador, IReadOnlyList<AtletaDoTreinador>>, AtletasDoTreinadorHandler>();
        services.AddScoped<IQueryHandler<AtletasParaTreinador, ResultadoAtletasParaTreinador?>, AtletasParaTreinadorHandler>();
        services.AddScoped<IQueryHandler<ObterFichaAtleta, FichaAtleta?>, ObterFichaAtletaHandler>();
        services.AddScoped<IQueryHandler<ObterElenco, ElencoDoTime?>, ObterElencoHandler>();
        services.AddScoped<IQueryHandler<ObterCopasDoTime, CopasDoTime?>, ObterCopasDoTimeHandler>();
        services.AddScoped<IQueryHandler<ObterChancesNaLiga, ChancesNaLiga?>, ObterChancesNaLigaHandler>();
        services.AddScoped<IQueryHandler<ObterRankingPenaltis, RankingPenaltis>, ObterRankingPenaltisHandler>();
        services.AddScoped<AssistenteScout>();
        return services;
    }
}
