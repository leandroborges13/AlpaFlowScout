namespace Scout.Application.Consultas;

public record ClubeNaCarreira(string Clube, string? Escudo, IReadOnlyList<int> Temporadas);

public record Transferencia(DateOnly? Data, string? De, string? Para, string? Tipo);

public record TrajetoriaAtleta(IReadOnlyList<ClubeNaCarreira> Clubes, IReadOnlyList<Transferencia> Transferencias);

/// <summary>Carreira do atleta (clubes por temporada e transferências), buscada no provedor sob demanda.</summary>
public interface IHistoricoDoAtleta
{
    Task<TrajetoriaAtleta?> Obter(int atletaId, CancellationToken ct);
}

/// <summary>Um jogo do clube na temporada e se o atleta foi titular nele.</summary>
public record JogoNaLinhaDoTempo(DateTime Data, string? Rodada, string Adversario, bool EmCasa, string? Placar, bool Titular, string? Funcao);

/// <summary>Presença como titular: base para regularidade (sem dado de saúde).</summary>
public record Presenca(int JogosDoClube, int ComoTitular, int SequenciaAtual, int MaiorSequencia);

public record Cartoes(int Amarelos, int Vermelhos, double? PorNoventaMinutos);
