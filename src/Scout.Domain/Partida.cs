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
