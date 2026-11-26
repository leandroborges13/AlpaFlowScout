using Microsoft.EntityFrameworkCore;
using Scout.Domain;

namespace Scout.Application;

/// <summary>O que a aplicação precisa do banco. A implementação fica na Infrastructure.</summary>
public interface IScoutDb
{
    DbSet<Competicao> Competicoes { get; }
    DbSet<Clube> Clubes { get; }
    DbSet<Atleta> Atletas { get; }
    DbSet<EstatisticaTemporada> Estatisticas { get; }
    DbSet<Partida> Partidas { get; }
    DbSet<Escalacao> Escalacoes { get; }
    DbSet<EstatisticaPartida> EstatisticasPartida { get; }
    DbSet<Treinador> Treinadores { get; }
    DbSet<TreinadorNaPartida> TreinadoresNaPartida { get; }
    DbSet<CobrancaDisputa> CobrancasDisputa { get; }
    DbSet<DesempenhoAvancado> DesempenhosAvancados { get; }
    DbSet<EstiloNoJogo> EstilosNoJogo { get; }
    DbSet<RoteiroPosJogo> RoteirosPosJogo { get; }
    DbSet<PalpiteDaPartida> Palpites { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Respostas do provedor já guardadas (o JSON, pela chave "endpoint?query"), sem consultar o provedor: para usar o que já
    /// veio (ex.: as transferências do atleta) sem gastar cota.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> RespostasGuardadas(IReadOnlyCollection<string> chaves, CancellationToken ct);
}

// CQRS enxuto: consultas e comandos com handlers próprios, sem biblioteca de mediador.
public interface IQuery<TResultado>;
public interface IQueryHandler<in TQuery, TResultado> where TQuery : IQuery<TResultado>
{
    Task<TResultado> Handle(TQuery query, CancellationToken ct);
}

public interface ICommand<TResultado>;
public interface ICommandHandler<in TCommand, TResultado> where TCommand : ICommand<TResultado>
{
    Task<TResultado> Handle(TCommand command, CancellationToken ct);
}
