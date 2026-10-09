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
    Task<int> SaveChangesAsync(CancellationToken ct = default);
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
