using System.Text.Json;
using Microsoft.Extensions.Logging;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Infrastructure.Provedor;

/// <summary>
/// Escalações completas (/fixtures/lineups), eventos (/fixtures/events) e números de cada atleta (/fixtures/players)
/// de uma partida, sob demanda.
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
            // Números por atleta: sem eles a partida ainda abre (campinho e lances), só sem as notas do jogo
            List<JsonElement> jogadores = [];
            try { jogadores = (await api.Consultar("/fixtures/players", p, validade, ct)).GetProperty("response").EnumerateArray().ToList(); }
            catch (Exception e) when (e is not OperationCanceledException and not CotaEsgotadaException)
            {
                log.LogWarning(e, "Números dos atletas da partida {Partida} indisponíveis", partidaId);
            }
            return Ler(escalacoes, eventos, jogadores);
        }
        catch (Exception e) when (e is not OperationCanceledException and not CotaEsgotadaException)
        {
            log.LogWarning(e, "Detalhes da partida {Partida} indisponíveis", partidaId);
            return null;
        }
    }

    public static DetalhesDoProvedor Ler(List<JsonElement> escalacoes, List<JsonElement> eventos, List<JsonElement>? jogadores = null)
    {
        var lados = escalacoes.Select(t =>
        {
            var esquema = Texto(t, "formation");
            List<JogadorNaPartida> Jogadores(string lista, bool titulares) =>
                t.TryGetProperty(lista, out var l) && l.ValueKind == JsonValueKind.Array
                    ? l.EnumerateArray().Select(x => x.GetProperty("player"))
                        .Select(j =>
                        {
                            var grade = titulares ? Texto(j, "grid") : null;
                            var ponto = FuncaoTatica.Coordenadas(esquema, grade);
                            return new JogadorNaPartida(Int(j, "id"), Texto(j, "name") ?? "?",
                                FuncaoTatica.Identificar(esquema, grade)?.Funcao, ponto?.X, ponto?.Y);
                        })
                        .ToList()
                    : [];
            return new LadoDaPartida(Int(t, "team", "id") ?? 0, Texto(t, "team", "name") ?? "?", esquema,
                Int(t, "coach", "id") is > 0 ? Int(t, "coach", "id") : null, Int(t, "coach", "id") is > 0 ? Texto(t, "coach", "name") : null, Jogadores("startXI", true), Jogadores("substitutes", false));
        }).ToList();

        // Quem está em campo, para saber em cada substituição quem saiu e quem entrou
        var emCampo = lados.SelectMany(l => l.Titulares).Select(j => j.Id).OfType<int>().ToHashSet();
        var lista = new List<EventoDaPartida>();
        foreach (var e in eventos.OrderBy(e => Int(e, "time", "elapsed") ?? 0).ThenBy(e => Int(e, "time", "extra") ?? 0))
        {
            var tipo = Texto(e, "type") ?? "";
            var detalhe = Texto(e, "detail") ?? "";
            // Cobranças da disputa de pênaltis vêm como gols com o comentário "Penalty Shootout": não são gols do jogo
            var disputa = (Texto(e, "comments") ?? "").Contains("Shootout", StringComparison.OrdinalIgnoreCase);
            int? jogadorId = Int(e, "player", "id"), outroId = Int(e, "assist", "id");
            string? jogador = Texto(e, "player", "name"), outro = Texto(e, "assist", "name");

            string? nome = tipo.ToLowerInvariant() switch
            {
                "goal" when disputa => detalhe == "Missed Penalty" ? "Pênalti perdido na disputa" : "Pênalti convertido na disputa",
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
            if (nome.StartsWith("Cartão") || nome.StartsWith("Gol contra") || nome.StartsWith("Pênalti")) (outro, outroId) = (null, null);

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
        return new DetalhesDoProvedor(lados, lista, LerJogadores(jogadores ?? []));
    }

    /// <summary>Números de cada atleta que entrou em campo (quem ficou no banco vem com 0 minuto e é descartado).</summary>
    public static List<EstatisticaDoJogador> LerJogadores(List<JsonElement> times)
    {
        var lista = new List<EstatisticaDoJogador>();
        foreach (var t in times)
        {
            var clube = Int(t, "team", "id") ?? 0;
            if (!t.TryGetProperty("players", out var atletas) || atletas.ValueKind != JsonValueKind.Array) continue;
            foreach (var a in atletas.EnumerateArray())
            {
                if (Int(a, "player", "id") is not { } id) continue;
                if (!a.TryGetProperty("statistics", out var st) || st.ValueKind != JsonValueKind.Array || st.GetArrayLength() == 0) continue;
                var e = st[0];
                var minutos = Num(e, "games", "minutes") ?? 0;
                if (minutos <= 0) continue;
                int N(params string[] c) => Num(e, c) ?? 0;
                var passes = N("passes", "total");
                // Em /fixtures/players a "accuracy" é a quantidade de passes certos; se vier maior que o total, é porcentagem
                int? certos = Num(e, "passes", "accuracy") is { } acc ? (acc <= passes ? acc : (int)Math.Round(acc * passes / 100.0)) : null;
                lista.Add(new EstatisticaDoJogador(id, clube, minutos, Texto(e, "games", "position"),
                    N("goals", "total"), N("goals", "assists"), N("goals", "conceded"), N("goals", "saves"),
                    N("shots", "total"), N("shots", "on"), passes, N("passes", "key"), certos,
                    N("tackles", "total"), N("tackles", "blocks"), N("tackles", "interceptions"),
                    N("duels", "total"), N("duels", "won"), N("dribbles", "attempts"), N("dribbles", "success"),
                    N("fouls", "committed"), N("fouls", "drawn"), N("cards", "yellow"), N("cards", "red")));
            }
        }
        return lista;
    }

    /// <summary>Número inteiro que o provedor manda ora como número, ora como texto ("80").</summary>
    static int? Num(JsonElement e, params string[] caminho) => Caminho(e, caminho) switch
    {
        { ValueKind: JsonValueKind.Number } v => v.TryGetInt32(out var i) ? i : (int)Math.Round(v.GetDouble()),
        { ValueKind: JsonValueKind.String } v => int.TryParse(v.GetString()?.TrimEnd('%'), out var i) ? i : null,
        _ => null,
    };

    static JsonElement? Caminho(JsonElement e, string[] caminho)
    {
        foreach (var parte in caminho)
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(parte, out e)) return null;
        return e.ValueKind == JsonValueKind.Null ? null : e;
    }

    static string? Texto(JsonElement e, params string[] caminho) => Caminho(e, caminho) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    static int? Int(JsonElement e, params string[] caminho) => Caminho(e, caminho) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;
}
