namespace Scout.Domain;

/// <summary>
/// Como o time jogou numa partida, pelas métricas de time da PitchAPI (/v1/matches/{id}/advanced): pressão, onde recupera a bola,
/// altura da defesa, domínio territorial e estilo de ataque. "Onde o time perde a bola" é o <see cref="RecuperacoesAltas"/> do
/// adversário no mesmo jogo. Só ligas cobertas pela PitchAPI e só pós-jogo.
/// </summary>
public class EstiloNoJogo
{
    public long Id { get; set; }
    public int PartidaId { get; set; }
    public int ClubeId { get; set; }
    public string Fonte { get; set; } = "PitchAPI";

    public double? Posse { get; set; }
    /// <summary>Fatia das ações no último terço que é do time (0 a 100).</summary>
    public double? FieldTilt { get; set; }
    /// <summary>Passes que o rival troca por ação defensiva do time: quanto menor, mais pressão.</summary>
    public double? Ppda { get; set; }
    /// <summary>Bolas recuperadas no campo de ataque (high turnovers).</summary>
    public int RecuperacoesAltas { get; set; }
    /// <summary>Bolas recuperadas em até 5 s depois de perder (contrapressão).</summary>
    public int RecuperacoesEm5s { get; set; }
    /// <summary>Altura média das ações defensivas, em metros a partir do próprio gol (campo de 105 m).</summary>
    public double? AlturaDefesa { get; set; }
    public int AtaquesDiretos { get; set; }
    public int AtaquesConstruidos { get; set; }
    public int PassesProgressivos { get; set; }
    public int EntradasTercoFinal { get; set; }
    public int EntradasArea { get; set; }
    public double? Xt { get; set; }
    /// <summary>JSON do time como veio da fonte, para métricas que ainda não têm coluna.</summary>
    public string? Bruto { get; set; }
}
