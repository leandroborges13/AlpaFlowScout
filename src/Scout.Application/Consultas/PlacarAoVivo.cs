namespace Scout.Application.Consultas;

// ---------- Placar e números do jogo na hora (cartões de intervalo e fim de jogo, backlog item 16) ----------

/// <summary>Um time no placar ao vivo: gols e os números do provedor naquele momento (nulo = não informado ainda).</summary>
public record LadoAoVivo(int ClubeId, string Time, int? Gols, int? Posse, int? Finalizacoes, int? NoAlvo, int? Escanteios, double? Xg);

/// <summary>
/// O jogo agora, direto do provedor e sem guardar nada (número parcial não pode ficar no lugar do final).
/// `Status`: o código do provedor (1H, HT, 2H, FT, AET, PEN...); `Minuto`: o tempo de jogo, quando estiver rolando.
/// </summary>
public record PlacarAoVivo(int PartidaId, string Status, int? Minuto, LadoAoVivo Mandante, LadoAoVivo Visitante)
{
    public bool Intervalo => Status == "HT";
    public bool Encerrado => Status is "FT" or "AET" or "PEN";
}

public interface IPlacarAoVivo
{
    Task<PlacarAoVivo?> Obter(int partidaId, CancellationToken ct);
}
