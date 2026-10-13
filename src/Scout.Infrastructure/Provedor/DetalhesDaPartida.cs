using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Escalações completas (/fixtures/lineups) e eventos (/fixtures/events) de uma partida, sob demanda.
/// Jogo encerrado fica guardado para sempre; jogo em andamento, por 2 minutos.
/// </summary>
public class DetalhesDaPartida(ApiFootballClient api, ILogger<DetalhesDaPartida> log) : IDetalhesDaPartida
{
    public async Task<DetalhesDoProvedor?> Obter(int partidaId, bool encerrada, CancellationToken ct)
    {
        try
        {
            TimeSpan? validade = encerrada ? null : TimeSpan.FromMinutes(2);
            var p = new Dictionary<string, object> { ["fixture"] = partidaId };
            var escalacoes = (await api.Consultar("/fixtures/lineups", p, validade, ct)).GetProperty("response").EnumerateArray().ToList();
            var eventos = (await api.Consultar("/fixtures/events", p, validade, ct)).GetProperty("response").EnumerateArray().ToList();
            return Ler(escalacoes, eventos);
        }
        catch (Exception e) when (e is not OperationCanceledException and not CotaEsgotadaException)
        {
            log.LogWarning(e, "Detalhes da partida {Partida} indisponíveis", partidaId);
            return null;
        }
    }

    public static DetalhesDoProvedor Ler(List<JsonElement> escalacoes, List<JsonElement> eventos)
    {
        var lados = escalacoes.Select(t =>
        {
            var esquema = Texto(t, "formation");
            List<JogadorNaPartida> Jogadores(string lista, bool titulares) =>
                t.TryGetProperty(lista, out var l) && l.ValueKind == JsonValueKind.Array
                    ? l.EnumerateArray().Select(x => x.GetProperty("player"))
                        .Select(j => new JogadorNaPartida(Int(j, "id"), Texto(j, "name") ?? "?",
                            titulares ? FuncaoTatica.Identificar(esquema, Texto(j, "grid"))?.Funcao : null))
                        .ToList()
                    : [];
            return new LadoDaPartida(Int(t, "team", "id") ?? 0, Texto(t, "team", "name") ?? "?", esquema,
                Int(t, "coach", "id"), Texto(t, "coach", "name"), Jogadores("startXI", true), Jogadores("substitutes", false));
        }).ToList();

        // Quem está em campo, para saber em cada substituição quem saiu e quem entrou
        var emCampo = lados.SelectMany(l => l.Titulares).Select(j => j.Id).OfType<int>().ToHashSet();
        var lista = new List<EventoDaPartida>();
        foreach (var e in eventos.OrderBy(e => Int(e, "time", "elapsed") ?? 0).ThenBy(e => Int(e, "time", "extra") ?? 0))
        {
            var tipo = Texto(e, "type") ?? "";
            var detalhe = Texto(e, "detail") ?? "";
            int? jogadorId = Int(e, "player", "id"), outroId = Int(e, "assist", "id");
            string? jogador = Texto(e, "player", "name"), outro = Texto(e, "assist", "name");

            string? nome = tipo.ToLowerInvariant() switch
            {
                "goal" => detalhe switch { "Own Goal" => "Gol contra", "Penalty" => "Gol de pênalti", "Missed Penalty" => "Pênalti perdido", _ => "Gol" },
                "card" => detalhe.Contains("Yellow", StringComparison.OrdinalIgnoreCase) && !detalhe.Contains("Second", StringComparison.OrdinalIgnoreCase)
                    ? "Cartão amarelo" : "Cartão vermelho",
                "subst" => "Substituição",
                "var" => "VAR",
                _ => null,
            };
            if (nome is null) continue;

            if (nome == "Substituição")
            {
                // O provedor não é consistente sobre a ordem: quem estava em campo é quem saiu
                if (outroId is { } a && emCampo.Contains(a) && (jogadorId is null || !emCampo.Contains(jogadorId.Value)))
                    (jogador, outro, jogadorId, outroId) = (outro, jogador, outroId, jogadorId);
                if (jogadorId is { } saiu) emCampo.Remove(saiu);
                if (outroId is { } entrou) emCampo.Add(entrou);
            }
            if (nome.StartsWith("Cartão") || nome.StartsWith("Gol contra") || nome == "Pênalti perdido") (outro, outroId) = (null, null);

            var traduzido = detalhe switch
            {
                "Goal cancelled" => "gol anulado", "Penalty confirmed" => "pênalti confirmado", "Penalty cancelled" => "pênalti anulado",
                "Goal Disallowed - offside" => "gol anulado por impedimento", "Card upgrade" => "cartão revisto",
                "Second Yellow card" => "segundo amarelo",
                _ => nome == "VAR" ? detalhe : null,
            };
            lista.Add(new EventoDaPartida(Int(e, "time", "elapsed") ?? 0, Int(e, "time", "extra"), Int(e, "team", "id") ?? 0,
                nome, jogador, outro, traduzido, jogadorId, outroId));
        }
        return new DetalhesDoProvedor(lados, lista);
    }

    static JsonElement? Caminho(JsonElement e, string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        return e.ValueKind == JsonValueKind.Null ? null : e;
    }

    static string? Texto(JsonElement e, params string[] caminho) => Caminho(e, caminho) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    static int? Int(JsonElement e, params string[] caminho) => Caminho(e, caminho) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;
}
