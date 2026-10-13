namespace Scout.Domain;

public class Partida
{
    public int Id { get; set; }
    public int CompeticaoId { get; set; }
    public int Temporada { get; set; }
    public string? Rodada { get; set; }
    public DateTime Data { get; set; }
    public string Status { get; set; } = "";
    public int MandanteId { get; set; }
    public int VisitanteId { get; set; }
    public int? GolsMandante { get; set; }
    public int? GolsVisitante { get; set; }
    /// <summary>Escalações já baixadas (evita consultar o provedor de novo).</summary>
    public bool EscalacoesImportadas { get; set; }
    /// <summary>Estatísticas dos dois times já baixadas.</summary>
    public bool EstatisticasImportadas { get; set; }

    public bool Encerrada => Status is "FT" or "AET" or "PEN";
}

/// <summary>Um atleta titular numa partida, com o lugar dele no desenho tático.</summary>
public class Escalacao
{
    public long Id { get; set; }
    public int PartidaId { get; set; }
    public int ClubeId { get; set; }
    public int AtletaId { get; set; }
    public string Esquema { get; set; } = "";
    /// <summary>Posição na grade do provedor, "linha:coluna" (linha 1 = goleiro).</summary>
    public string? Grade { get; set; }
    public Setor Setor { get; set; }
    public Lado Lado { get; set; }
    public string Funcao { get; set; } = "";
}

/// <summary>
/// Números de um time numa partida, como o provedor informa. Uso interno: a plataforma mostra só
/// médias convertidas em percentis, nunca estes valores brutos.
/// </summary>
public class EstatisticaPartida
{
    public long Id { get; set; }
    public int PartidaId { get; set; }
    public int ClubeId { get; set; }
    /// <summary>Posse de bola (%).</summary>
    public int? Posse { get; set; }
    public int? Finalizacoes { get; set; }
    public int? FinalizacoesNoAlvo { get; set; }
    public int? FinalizacoesNaArea { get; set; }
    public int? Escanteios { get; set; }
    public int? Faltas { get; set; }
    public int? Impedimentos { get; set; }
    public int? Amarelos { get; set; }
    public int? Vermelhos { get; set; }
    public int? Defesas { get; set; }
    public int? Passes { get; set; }
    public int? PassesCertos { get; set; }
}
