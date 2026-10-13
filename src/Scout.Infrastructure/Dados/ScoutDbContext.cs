using Microsoft.EntityFrameworkCore;
using Scout.Application;
using Scout.Domain;

namespace Scout.Infrastructure.Dados;

/// <summary>Resposta bruta do provedor guardada no banco: a mesma consulta nunca é paga duas vezes.</summary>
public class RespostaProvedor
{
    public string Chave { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTime ObtidaEm { get; set; }
}

/// <summary>Quem pode entrar na plataforma (uso interno: criado pela linha de comando, sem cadastro aberto).</summary>
public class Usuario
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Nome { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    public DateTime? UltimoAcesso { get; set; }
}

public class ConsultaDiaria
{
    public DateOnly Dia { get; set; }
    public int Usadas { get; set; }
}

public class ScoutDbContext(DbContextOptions<ScoutDbContext> options) : DbContext(options), IScoutDb
{
    public DbSet<Competicao> Competicoes => Set<Competicao>();
    public DbSet<Clube> Clubes => Set<Clube>();
    public DbSet<Atleta> Atletas => Set<Atleta>();
    public DbSet<EstatisticaTemporada> Estatisticas => Set<EstatisticaTemporada>();
    public DbSet<Partida> Partidas => Set<Partida>();
    public DbSet<Escalacao> Escalacoes => Set<Escalacao>();
    public DbSet<EstatisticaPartida> EstatisticasPartida => Set<EstatisticaPartida>();
    public DbSet<Treinador> Treinadores => Set<Treinador>();
    public DbSet<TreinadorNaPartida> TreinadoresNaPartida => Set<TreinadorNaPartida>();
    public DbSet<RespostaProvedor> RespostasProvedor => Set<RespostaProvedor>();
    public DbSet<ConsultaDiaria> ConsultasDiarias => Set<ConsultaDiaria>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Ids de competição, clube, atleta e partida são os do provedor
        b.Entity<Competicao>().Property(c => c.Id).ValueGeneratedNever();
        b.Entity<Clube>().Property(c => c.Id).ValueGeneratedNever();
        b.Entity<Atleta>().Property(a => a.Id).ValueGeneratedNever();
        b.Entity<Atleta>().HasIndex(a => a.Nome);

        b.Entity<Partida>().Property(p => p.Id).ValueGeneratedNever();
        b.Entity<Partida>().HasIndex(p => new { p.CompeticaoId, p.Temporada });
        b.Entity<Partida>().Ignore(p => p.Encerrada);

        b.Entity<EstatisticaTemporada>().HasIndex(e => new { e.AtletaId, e.ClubeId, e.CompeticaoId, e.Temporada }).IsUnique();
        b.Entity<EstatisticaTemporada>().HasIndex(e => new { e.CompeticaoId, e.Temporada });
        b.Entity<EstatisticaTemporada>().Property(e => e.PrecisaoPasses).HasPrecision(5, 2);

        b.Entity<Escalacao>().HasIndex(e => new { e.PartidaId, e.AtletaId }).IsUnique();
        b.Entity<Escalacao>().HasIndex(e => e.AtletaId);
        b.Entity<Escalacao>().Property(e => e.Setor).HasConversion<string>().HasMaxLength(10);
        b.Entity<Escalacao>().Property(e => e.Lado).HasConversion<string>().HasMaxLength(10);

        b.Entity<EstatisticaPartida>().HasIndex(e => new { e.PartidaId, e.ClubeId }).IsUnique();
        b.Entity<EstatisticaPartida>().HasIndex(e => e.ClubeId);

        b.Entity<Treinador>().Property(t => t.Id).ValueGeneratedNever();
        b.Entity<TreinadorNaPartida>().HasIndex(t => new { t.PartidaId, t.ClubeId }).IsUnique();
        b.Entity<TreinadorNaPartida>().HasIndex(t => t.TreinadorId);

        b.Entity<RespostaProvedor>().HasKey(r => r.Chave);
        b.Entity<RespostaProvedor>().Property(r => r.Json).HasColumnType("jsonb");
        b.Entity<ConsultaDiaria>().HasKey(c => c.Dia);
        b.Entity<Usuario>().HasIndex(u => u.Email).IsUnique();
    }
}
