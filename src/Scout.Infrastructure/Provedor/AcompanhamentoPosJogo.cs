using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Scout.Application.Chat;
using Scout.Domain;
using Scout.Infrastructure.Dados;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Pós-jogo automático do canal Futebol em Números: a cada 10 minutos olha os jogos dos times acompanhados (SCOUT_POSJOGO_TIMES,
/// ids do provedor separados por vírgula; o Galo é 1062) que começaram há 2h20 a 8h. Quando o jogo termina, atualiza a partida
/// no provedor, a IA escreve o roteiro e o aviso sai no WhatsApp (SCOUT_WHATSAPP_URL, _CHAVE, _INSTANCIA e _NUMERO, Evolution API).
/// Espera 2h20 do início porque, logo no apito, o provedor ainda não fechou as estatísticas e as notas sairiam incompletas.
/// </summary>
public class AcompanhamentoPosJogo(IServiceScopeFactory escopos, IConfiguration config, IHttpClientFactory http, ILogger<AcompanhamentoPosJogo> log) : BackgroundService
{
    public static readonly TimeSpan DepoisDoInicio = TimeSpan.FromMinutes(140);
    public static readonly TimeSpan Ate = TimeSpan.FromHours(8);

    public static List<int> Times(string? texto) =>
        (texto ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var id) ? id : 0).Where(id => id > 0).ToList();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var times = Times(config["SCOUT_POSJOGO_TIMES"]);
        if (times.Count == 0) return;
        log.LogInformation("Pós-jogo automático para os times {Times}", string.Join(", ", times));
        while (!ct.IsCancellationRequested)
        {
            try { await Verificar(times, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Falha no pós-jogo automático"); }
            await Task.Delay(TimeSpan.FromMinutes(10), ct);
        }
    }

    async Task Verificar(List<int> times, CancellationToken ct)
    {
        using var escopo = escopos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<ScoutDbContext>();
        var agora = DateTime.UtcNow;
        var feitos = db.RoteirosPosJogo.Select(r => r.PartidaId);
        var jogos = await db.Partidas
            .Where(p => (times.Contains(p.MandanteId) || times.Contains(p.VisitanteId))
                && p.Data <= agora - DepoisDoInicio && p.Data >= agora - Ate && !feitos.Contains(p.Id))
            .Select(p => p.Id).ToListAsync(ct);
        foreach (var id in jogos)
        {
            var partida = await escopo.ServiceProvider.GetRequiredService<Importador>().AtualizarPartida(id, ct);
            if (partida is null || !partida.Encerrada) continue; // ainda em andamento (prorrogação, atraso): tenta de novo daqui a 10 min
            var roteiro = await escopo.ServiceProvider.GetRequiredService<RoteiristaPosJogo>().Gerar(id, ct);
            if (roteiro is null) continue;
            log.LogInformation("Roteiro do pós-jogo pronto: partida {Partida}", id);
            if (await Avisar(Mensagem(db, partida, roteiro), ct))
            {
                roteiro.AvisadoEm = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    static string Mensagem(ScoutDbContext db, Partida p, RoteiroPosJogo r)
    {
        var nomes = db.Clubes.Where(c => c.Id == p.MandanteId || c.Id == p.VisitanteId).ToDictionary(c => c.Id, c => c.Nome);
        string Nome(int id) => nomes.GetValueOrDefault(id, "?");
        return $"⚽ *Pós-jogo pronto: {Nome(p.MandanteId)} {p.GolsMandante} x {p.GolsVisitante} {Nome(p.VisitanteId)}*\n\n{r.Texto}\n\n" +
            "Os cartões para o vídeo estão no Scout: abra a partida e toque em \"Para vídeo\".";
    }

    /// <summary>Envia pelo Evolution API; sem a configuração, só deixa o roteiro no Scout.</summary>
    async Task<bool> Avisar(string texto, CancellationToken ct)
    {
        string? url = config["SCOUT_WHATSAPP_URL"], chave = config["SCOUT_WHATSAPP_CHAVE"],
            instancia = config["SCOUT_WHATSAPP_INSTANCIA"], numero = config["SCOUT_WHATSAPP_NUMERO"];
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(chave) || string.IsNullOrWhiteSpace(instancia) || string.IsNullOrWhiteSpace(numero))
            return false;
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{url.TrimEnd('/')}/message/sendText/{instancia}")
            {
                Content = JsonContent.Create(new { number = numero, text = texto }),
            };
            pedido.Headers.Add("apikey", chave);
            var r = await http.CreateClient().SendAsync(pedido, ct);
            if (!r.IsSuccessStatusCode) log.LogWarning("Aviso do pós-jogo no WhatsApp falhou: HTTP {Status}", (int)r.StatusCode);
            return r.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Aviso do pós-jogo no WhatsApp falhou");
            return false;
        }
    }
}
