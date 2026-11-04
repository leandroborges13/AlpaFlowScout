namespace Scout.Domain;

/// <summary>
/// Roteiro do vídeo de pós-jogo (canal Futebol em Números), escrito pela IA a partir da análise da partida.
/// Um por partida: gerar de novo substitui o anterior.
/// </summary>
public class RoteiroPosJogo
{
    public int PartidaId { get; set; }
    public string Texto { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    /// <summary>Quando o aviso saiu no WhatsApp (nulo: ainda não avisou, ou o aviso não está configurado).</summary>
    public DateTime? AvisadoEm { get; set; }
}
