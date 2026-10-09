namespace Scout.Domain;

/// <summary>Atleta como vem do provedor de dados (o Id é o do provedor).</summary>
public class Atleta
{
    /// <summary>Idade mínima para qualquer análise individual (proteção de menores, LGPD art. 14).</summary>
    public const int IdadeMinimaAnalise = 18;

    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? NomeCompleto { get; set; }
    public DateOnly? Nascimento { get; set; }
    public string? Nacionalidade { get; set; }
    public int? AlturaCm { get; set; }
    public int? PesoKg { get; set; }
    public string? Foto { get; set; }
    public DateTime AtualizadoEm { get; set; }

    public int? IdadeEm(DateOnly referencia)
    {
        if (Nascimento is not { } n) return null;
        var idade = referencia.Year - n.Year;
        return referencia < n.AddYears(idade) ? idade - 1 : idade;
    }

    /// <summary>
    /// Só analisa quem tinha 18 anos ou mais na data de referência. Sem data de nascimento,
    /// não dá para garantir: fica de fora.
    /// </summary>
    public bool PodeSerAnalisado(DateOnly referencia) => IdadeEm(referencia) >= IdadeMinimaAnalise;
}
