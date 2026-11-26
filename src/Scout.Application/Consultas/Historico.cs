namespace Scout.Application.Consultas;

/// <summary>O que o atleta fez por um clube numa temporada, somando liga e copas (seleção fica à parte, em ClubeNaCarreira.Selecao).</summary>
public record NumerosNaTemporada(int Temporada, int Jogos, int Minutos, int Gols, int Assistencias);

/// <summary>
/// Um clube da carreira. `Numeros`: jogos, gols e assistências por temporada (das temporadas que o provedor tem); `Selecao` marca
/// a seleção do país, que não entra nos totais de clube.
/// </summary>
public record ClubeNaCarreira(string Clube, string? Escudo, IReadOnlyList<int> Temporadas,
    IReadOnlyList<NumerosNaTemporada>? Numeros = null, bool Selecao = false);

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

/// <summary>Um ponto do desenho tático onde o atleta começou jogos (0–100; Y = 100 é o gol adversário).</summary>
public record PontoNoCampo(double X, double Y, int Jogos, string Funcao);

/// <summary>
/// Estilo pelos atributos, comparado ao próprio setor: de −100 (muito marcador) a +100 (bem ofensivo).
/// </summary>
public record EstiloDeJogo(int Indice, string Rotulo, double Ofensivo, double Defensivo, string Explicacao);

public record PassagemNaCarreira(string Clube, string? Escudo, DateOnly? Inicio, DateOnly? Fim);

public record CarreiraTreinador(int? Idade, string? Nacionalidade, IReadOnlyList<PassagemNaCarreira> Passagens);

/// <summary>Carreira do treinador (clubes e períodos), buscada no provedor sob demanda.</summary>
public interface ICarreiraDoTreinador
{
    Task<CarreiraTreinador?> Obter(int treinadorId, CancellationToken ct);
}
