namespace Scout.Domain;

public class Competicao
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Pais { get; set; } = "";
}

public class Clube
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Escudo { get; set; }
}

/// <summary>Números de um atleta por um clube numa competição e temporada.</summary>
public class EstatisticaTemporada
{
    public long Id { get; set; }
    public int AtletaId { get; set; }
    public Atleta Atleta { get; set; } = null!;
    public int ClubeId { get; set; }
    public Clube Clube { get; set; } = null!;
    public int CompeticaoId { get; set; }
    public Competicao Competicao { get; set; } = null!;
    public int Temporada { get; set; }

    /// <summary>Posição genérica do provedor: Goalkeeper, Defender, Midfielder, Attacker.</summary>
    public string? PosicaoProvedor { get; set; }
    public int Jogos { get; set; }
    public int Titular { get; set; }
    public int Minutos { get; set; }
    public int Gols { get; set; }
    public int Assistencias { get; set; }
    public int GolsSofridos { get; set; }
    public int Defesas { get; set; }
    public int Finalizacoes { get; set; }
    public int FinalizacoesNoAlvo { get; set; }
    public int Passes { get; set; }
    public int PassesDecisivos { get; set; }
    /// <summary>Precisão média de passe informada pelo provedor (%), quando houver.</summary>
    public decimal? PrecisaoPasses { get; set; }
    public int Desarmes { get; set; }
    public int Bloqueios { get; set; }
    public int Interceptacoes { get; set; }
    public int Duelos { get; set; }
    public int DuelosGanhos { get; set; }
    public int DriblesTentados { get; set; }
    public int DriblesCertos { get; set; }
    public int FaltasCometidas { get; set; }
    public int FaltasSofridas { get; set; }
    public int Amarelos { get; set; }
    public int Vermelhos { get; set; }
    public DateTime AtualizadoEm { get; set; }
}
