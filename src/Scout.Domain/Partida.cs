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
    /// <summary>Treinadores dos dois times já lidos das escalações.</summary>
    public bool TreinadoresImportados { get; set; }
    /// <summary>Cobranças da disputa de pênaltis já lidas dos lances (só jogos decididos nos pênaltis).</summary>
    public bool DisputaImportada { get; set; }
    /// <summary>xG já procurado nas estatísticas guardadas (jogos importados antes de lermos o xG).</summary>
    public bool XgLido { get; set; }

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
    /// <summary>Gols esperados (xG) como o provedor informa; nulo onde ele não calcula. Só aparece como percentil.</summary>
    public double? Xg { get; set; }
}

/// <summary>Uma cobrança na disputa de pênaltis (fato público: quem bateu e se converteu).</summary>
public class CobrancaDisputa
{
    public long Id { get; set; }
    public int PartidaId { get; set; }
    public int ClubeId { get; set; }
    public int? AtletaId { get; set; }
    public string Jogador { get; set; } = "";
    /// <summary>Ordem da cobrança na disputa (1, 2, 3...), na sequência dos lances.</summary>
    public int Ordem { get; set; }
    public bool Convertida { get; set; }
}

/// <summary>Treinador como vem nas escalações do provedor (o Id é o do provedor).</summary>
public class Treinador
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string? Foto { get; set; }
}

/// <summary>Quem comandou cada time em cada partida.</summary>
public class TreinadorNaPartida
{
    public long Id { get; set; }
    public int PartidaId { get; set; }
    public int ClubeId { get; set; }
    public int TreinadorId { get; set; }
}
