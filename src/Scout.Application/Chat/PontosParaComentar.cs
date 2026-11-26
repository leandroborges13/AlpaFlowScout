using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Application.Chat;

/// <summary>Um número do jogo com a frase pronta para falar. `Tema`: placar, finalizações, destaque, gols sofridos, estilo, modelo, próximo.</summary>
public record PontoParaComentar(string Tema, string Frase);

public record ComentarioDoJogo(ResumoPartida Partida, string Time, IReadOnlyList<PontoParaComentar> Pontos);

/// <summary>
/// "Comentar o jogo" (backlog item 19): 4 a 7 números do jogo com a frase pronta, do ponto de vista do time do canal (ou do
/// time pedido), para o Leandro comentar com o rosto logo depois do apito. Sem IA: tudo sai da análise, do mapa de finalizações,
/// das métricas de time e do palpite, então os números são exatamente os da base.
/// </summary>
public class PontosParaComentar(IQueryHandler<AnalisarPartida, AnalisePartida?> analisarPartida, IMapaDaPartida mapas, IScoutDb db,
    IEstiloDaPartida estilos, IQueryHandler<ObterEstiloNaLiga, EstiloNaLiga?> estiloNaLiga, IQueryHandler<ObterPalpite, ResultadoPalpite?> palpites)
{
    /// <summary>O time do canal Galo na Lupa (Atlético-MG): quando joga, é o foco dos pontos.</summary>
    public const int TimeDoCanal = 1062;

    public async Task<ComentarioDoJogo?> Gerar(int partidaId, int? timeId, CancellationToken ct)
    {
        var a = await analisarPartida.Handle(new AnalisarPartida(partidaId), ct);
        if (a is null || a.Partida.GolsMandante is null) return null;
        var p = a.Partida;
        var foco = timeId is { } t && (t == p.MandanteId || t == p.VisitanteId) ? t
            : p.VisitanteId == TimeDoCanal ? p.VisitanteId : p.MandanteId;
        var mapa = await mapas.Obter(p, ct);

        IReadOnlyList<EstiloNoJogo>? noJogo = await db.EstilosNoJogo.Where(e => e.PartidaId == p.Id).ToListAsync(ct);
        if (noJogo.Count != 2) noJogo = await estilos.ObterEstilo(p, ct);
        var naLiga = await estiloNaLiga.Handle(new ObterEstiloNaLiga(foco, p.CompeticaoId, p.Temporada), ct);
        var palpite = (await palpites.Handle(new ObterPalpite(PartidaId: partidaId), ct))?.Palpite;

        return new ComentarioDoJogo(p, Nome(p, foco), Montar(a, foco, mapa, noJogo is { Count: 2 } ? noJogo : null, naLiga, palpite));
    }

    /// <summary>Os pontos, na ordem da fala. Público para os testes.</summary>
    public static IReadOnlyList<PontoParaComentar> Montar(AnalisePartida a, int foco, MapaDaPartida? mapa, IReadOnlyList<EstiloNoJogo>? noJogo,
        EstiloNaLiga? naLiga, Palpite? palpite)
    {
        var p = a.Partida;
        var rival = foco == p.MandanteId ? p.VisitanteId : p.MandanteId;
        string N(int id) => Nome(p, id);
        var r = new List<PontoParaComentar>();
        var lances = a.Eventos.OrderBy(e => e.Minuto).ThenBy(e => e.Acrescimo ?? 0).ToList();

        // 1. placar, com quem fez os gols e a expulsão (o contexto que muda a leitura do jogo)
        var placar = new StringBuilder($"{N(p.MandanteId)} {p.GolsMandante} x {p.GolsVisitante} {N(p.VisitanteId)}");
        if (a.Desfecho is { } d) placar.Append($" ({d.ToLowerInvariant()})");
        var gols = lances.Where(e => e.Tipo.StartsWith("Gol")).Select(e => $"{e.Jogador} {Minuto(e)}'" + (e.Tipo == "Gol contra" ? " (contra)" : e.Tipo == "Gol de pênalti" ? " (pênalti)" : "")).ToList();
        if (gols.Count > 0) placar.Append($". Gols: {string.Join(", ", gols)}");
        foreach (var v in lances.Where(e => e.Tipo == "Cartão vermelho"))
            placar.Append($". {(v.ClubeId == foco ? $"O {N(foco)} ficou com um a menos" : $"O {N(rival)} ficou com um a menos")} desde os {Minuto(v)}' ({v.Jogador} expulso)");
        r.Add(new("placar", placar + "."));

        // 2. finalizações (do mapa): quantas e quantas no alvo, dos dois lados
        if (mapa is { Finalizacoes.Count: > 0 })
        {
            (int Total, int Alvo, int Area) Conta(int id)
            {
                var f = mapa.Finalizacoes.Where(x => x.ClubeId == id).ToList();
                return (f.Count, f.Count(x => x.NoAlvo), f.Count(x => x.DentroDaArea));
            }
            var (tf, af, df) = Conta(foco);
            var (tr, ar, _) = Conta(rival);
            r.Add(new("finalizações", $"O {N(foco)} finalizou {(tf == 1 ? "1 vez" : $"{tf} vezes")} ({df} de dentro da área) e acertou o alvo {af}; " +
                $"o {N(rival)}, {(tr == 1 ? "1 finalização" : $"{tr} finalizações")} e {ar} no alvo."));
        }

        // 3. destaque do time pela nota da IA, com o que ele fez (gols, assistências e chutes no alvo do mapa)
        var melhor = (a.Desempenhos ?? []).Where(x => x.ClubeId == foco && x.Nota is not null).MaxBy(x => x.Nota);
        if (melhor is not null)
        {
            var feitos = new List<string>();
            if (melhor.Gols > 0) feitos.Add(melhor.Gols == 1 ? "1 gol" : $"{melhor.Gols} gols");
            if (melhor.Assistencias > 0) feitos.Add(melhor.Assistencias == 1 ? "1 assistência" : $"{melhor.Assistencias} assistências");
            if (mapa is { Finalizacoes.Count: > 0 })
            {
                var doTime = mapa.Finalizacoes.Where(x => x.ClubeId == foco).ToList();
                var dele = doTime.Where(x => MesmoJogador(x.Jogador, melhor.Nome)).ToList();
                var alvoTime = doTime.Count(x => x.NoAlvo);
                if (dele.Count(x => x.NoAlvo) is var alvo and > 0 && alvoTime > 1)
                    feitos.Add($"{alvo} dos {alvoTime} chutes no alvo do {N(foco)}");
                else if (dele.Count > 0) feitos.Add(dele.Count == 1 ? "1 finalização" : $"{dele.Count} finalizações");
            }
            r.Add(new("destaque", $"O melhor do {N(foco)} pela IA foi {melhor.Nome}, nota {Math.Round(melhor.Nota!.Value)}" +
                (feitos.Count > 0 ? $": {string.Join(", ", feitos)}." : ".")));
        }

        // 4. de que lado saíram os gols sofridos: o lado do campo de quem deu o passe (escalação do rival)
        var ladoRival = rival == p.MandanteId ? a.Mandante : a.Visitante;
        var sofridos = lances.Where(e => e.Tipo.StartsWith("Gol") && e.Tipo != "Gol contra" && e.ClubeId == rival).ToList();
        var frasesGol = sofridos.Select(g =>
        {
            var quem = g.OutroId is { } oid ? ladoRival.Titulares.FirstOrDefault(j => j.Id == oid) : null;
            var lado = LadoDaDefesa(quem?.X);
            if (g.Outro is null) return $"o de {g.Jogador} ({Minuto(g)}') saiu sem assistência";
            return lado is null ? $"o de {g.Jogador} ({Minuto(g)}') teve passe de {g.Outro}"
                : $"o de {g.Jogador} ({Minuto(g)}') saiu de passe de {g.Outro}, pela {lado} da defesa do {N(foco)}";
        }).ToList();
        if (frasesGol.Count > 0)
            r.Add(new("gols sofridos", $"{(frasesGol.Count == 1 ? "O gol sofrido" : "Os gols sofridos")}: {string.Join("; ", frasesGol)}."));

        // 5. onde o jogo foi disputado (métricas de time), comparado com a média do time na liga quando houver
        if (noJogo is { Count: 2 })
        {
            var doFoco = noJogo.First(e => e.ClubeId == foco);
            var doRival = noJogo.First(e => e.ClubeId != foco);
            var media = naLiga?.Metricas.FirstOrDefault(m => m.Chave == "perdasNoProprioCampo");
            r.Add(new("estilo", $"O {N(foco)} perdeu a bola {doRival.RecuperacoesAltas} vezes no próprio campo" +
                (media is null ? "" : $" (na {naLiga!.Competicao}, a média dele é {media.Valor.ToString("0.#", Pt)} por jogo: é {media.Frase})") +
                $" e roubou {doFoco.RecuperacoesAltas} no campo de ataque."));
        }

        // 6. o modelo: cravou, acertou ou errou
        if (palpite is not null)
        {
            var fav = palpite.Favorito switch
            {
                "mandante" => $"{N(p.MandanteId)} ({Math.Round(palpite.ChanceMandante)}%)",
                "visitante" => $"{N(p.VisitanteId)} ({Math.Round(palpite.ChanceVisitante)}%)",
                _ => $"empate ({Math.Round(palpite.ChanceEmpate)}%)",
            };
            var placarJogo = $"{p.GolsMandante} x {p.GolsVisitante}";
            r.Add(new("modelo", placarJogo == palpite.PlacarProvavel ? $"O modelo cravou o placar: {palpite.PlacarProvavel} era o mais provável."
                : palpite.Acertou == true ? $"O modelo acertou: deu {fav}."
                : $"O modelo errou: deu {fav}, com {palpite.PlacarProvavel} como placar mais provável."));
        }

        // 7. o próximo jogo do time
        if (a.JogosSeguintes?.FirstOrDefault(j => j.MandanteId == foco || j.VisitanteId == foco) is { } prox)
            r.Add(new("próximo", $"Próximo: {Nome(prox, prox.MandanteId)} x {Nome(prox, prox.VisitanteId)}, {prox.Data.ToString("dddd, dd/MM", Pt)} ({prox.Competicao})."));
        return r;
    }

    static readonly CultureInfo Pt = new("pt-BR");

    static string Nome(ResumoPartida p, int id) => id == TimeDoCanal ? "Galo" : id == p.MandanteId ? p.Mandante : p.Visitante;

    static string Minuto(EventoDaPartida e) => e.Acrescimo is > 0 ? $"{e.Minuto}+{e.Acrescimo}" : $"{e.Minuto}";

    /// <summary>
    /// X do desenho tático do rival (0 = esquerda dele, 100 = direita dele): a esquerda do rival ataca a direita da nossa defesa.
    /// Meio do campo (35 a 65) não tem lado.
    /// </summary>
    public static string? LadoDaDefesa(double? x) => x switch { null => null, < 35 => "direita", > 65 => "esquerda", _ => null };

    /// <summary>O nome do mapa (fonte externa) e o nosso podem vir diferentes ("T. Cuello" x "Tomás Cuello"): compara o sobrenome.</summary>
    public static bool MesmoJogador(string a, string b)
    {
        static string Sobrenome(string n) => new string(n.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Split([' ', '.'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? "";
        return Sobrenome(a) != "" && Sobrenome(a) == Sobrenome(b);
    }
}
