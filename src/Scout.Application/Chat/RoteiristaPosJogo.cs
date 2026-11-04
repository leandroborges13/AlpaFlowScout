using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Application.Chat;

/// <summary>
/// Roteiro do Short de pós-jogo do canal Futebol em Números: título, gancho, a fala de ~40 s (em primeira pessoa, para o Leandro
/// gravar) e a legenda, escritos pela IA só com a análise da partida. Guarda no banco (um por partida).
/// </summary>
public class RoteiristaPosJogo(IChatClient chat, IScoutDb db, IQueryHandler<AnalisarPartida, AnalisePartida?> analisarPartida, IMapaDaPartida mapas)
{
    public async Task<RoteiroPosJogo?> Gerar(int partidaId, CancellationToken ct)
    {
        var a = await analisarPartida.Handle(new AnalisarPartida(partidaId), ct);
        if (a is null || a.Partida.GolsMandante is null) return null;
        var mapa = await mapas.Obter(a.Partida, ct); // sem mapa (copa, fonte fora do ar): o roteiro sai sem as finalizações

        var resposta = await chat.GetResponseAsync(
        [
            new(ChatRole.System, Instrucoes),
            new(ChatRole.User, Dados(a, mapa)),
        ], new ChatOptions { Temperature = 0.4f }, ct);

        var roteiro = await db.RoteirosPosJogo.FirstOrDefaultAsync(r => r.PartidaId == partidaId, ct);
        if (roteiro is null) db.RoteirosPosJogo.Add(roteiro = new RoteiroPosJogo { PartidaId = partidaId });
        roteiro.Texto = ComFraseDoMapa(resposta.Text.Trim(), FraseDoMapa(a.Partida, mapa));
        roteiro.CriadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return roteiro;
    }

    const string Fecho = "Me segue que no próximo jogo tem mais.";

    /// <summary>A frase das finalizações do mapa, pronta ("O Atletico-MG finalizou 17 vezes, 13 de dentro da área e 9 no alvo; ...").</summary>
    public static string? FraseDoMapa(ResumoPartida p, MapaDaPartida? mapa)
    {
        if (mapa is null || mapa.Finalizacoes.Count == 0) return null;
        static string Vezes(int n) => n == 1 ? "1 vez" : $"{n} vezes";
        string Do(int clubeId, string nome, bool primeiro)
        {
            var f = mapa.Finalizacoes.Where(x => x.ClubeId == clubeId).ToList();
            return primeiro
                ? $"O {nome} finalizou {Vezes(f.Count)}, {f.Count(x => x.DentroDaArea)} de dentro da área e {f.Count(x => x.NoAlvo)} no alvo"
                : $"o {nome}, {Vezes(f.Count)}, {f.Count(x => x.DentroDaArea)} da área e {f.Count(x => x.NoAlvo)} no alvo";
        }
        return $"{Do(p.MandanteId, p.Mandante, true)}; {Do(p.VisitanteId, p.Visitante, false)}. Olha no mapa.";
    }

    /// <summary>
    /// Garante a frase do mapa na FALA: a IA às vezes a esquece mesmo com a regra (aconteceu em produção). Se a fala não traz
    /// o total de finalizações do mandante, a frase entra antes do fecho (ou no fim da fala).
    /// </summary>
    public static string ComFraseDoMapa(string texto, string? frase)
    {
        if (frase is null) return texto;
        var total = System.Text.RegularExpressions.Regex.Match(frase, @"finalizou (\d+) vez").Groups[1].Value;
        var inicio = texto.IndexOf("FALA:", StringComparison.Ordinal);
        if (inicio < 0) return texto;
        var fim = texto.IndexOf("CARTÕES:", inicio, StringComparison.Ordinal);
        var fala = fim < 0 ? texto[inicio..] : texto[inicio..fim];
        if (fala.Contains($"{total} vez") || fala.Contains($"{total} finaliza")) return texto;
        var novaFala = fala.Contains(Fecho) ? fala.Replace(Fecho, $"{frase} {Fecho}") : $"{fala.TrimEnd()} {frase}\n\n";
        return texto[..inicio] + novaFala + (fim < 0 ? "" : texto[fim..]);
    }

    /// <summary>O que a IA precisa da análise, sem escalações nem indicadores de cada atleta (deixa o pedido curto e focado).</summary>
    public static string Dados(AnalisePartida a, MapaDaPartida? mapa = null)
    {
        var p = a.Partida;
        string Time(int clubeId) => clubeId == p.MandanteId ? p.Mandante : p.Visitante;
        // Minuto com acréscimo ("90+3") e em ordem: dois gols "aos 90" sem o acréscimo deixavam a IA errar quem virou o jogo
        static string Minuto(EventoDaPartida e) => e.Acrescimo is > 0 ? $"{e.Minuto}+{e.Acrescimo}" : $"{e.Minuto}";
        var lances = a.Eventos.OrderBy(e => e.Minuto).ThenBy(e => e.Acrescimo ?? 0).ToList();
        // Placar depois de cada gol, já contado: a IA não precisa deduzir qual gol empatou e qual virou
        int casa = 0, fora = 0;
        var gols = lances.Where(e => e.Tipo.StartsWith("Gol")).Select(e =>
        {
            // gol contra conta para o outro time (o provedor marca o time de quem fez)
            var paraMandante = (e.ClubeId == p.MandanteId) != (e.Tipo == "Gol contra");
            if (paraMandante) casa++; else fora++;
            return new { Minuto = Minuto(e), Time = Time(e.ClubeId), e.Tipo, e.Jogador, Assistencia = e.Outro, PlacarDepois = $"{casa} x {fora}" };
        }).ToList();
        string Dia(DateTime d) => d.ToString("dddd, dd/MM", new System.Globalization.CultureInfo("pt-BR"));
        return JsonSerializer.Serialize(new
        {
            Jogo = $"{p.Mandante} {p.GolsMandante} x {p.GolsVisitante} {p.Visitante}",
            p.Competicao, p.Rodada, Data = Dia(p.Data), a.Desfecho,
            Esquemas = new { Mandante = a.Mandante.Esquema, Visitante = a.Visitante.Esquema },
            Treinadores = new { Mandante = a.Mandante.Treinador, Visitante = a.Visitante.Treinador },
            Gols = gols,
            Expulsoes = lances.Where(e => e.Tipo == "Cartão vermelho").Select(e => new { Minuto = Minuto(e), Time = Time(e.ClubeId), e.Jogador }),
            // Percentil do aspecto já como frase ("pior que em 89% dos próprios jogos"): com o número cru a IA escreveu
            // "só 11% do seu melhor desempenho". Com o mapa, finalização se conta pelo mapa e sai daqui (as duas fontes divergem).
            Aspectos = a.Aspectos
                .Where(x => mapa is null || !x.Aspecto.Contains("inaliza", StringComparison.OrdinalIgnoreCase))
                .Select(x => new { x.Aspecto, x.Vantagem, Mandante = Leitura(x.PercentilMandante), Visitante = Leitura(x.PercentilVisitante) }),
            Destaques = (a.Desempenhos ?? []).Where(d => d.Nota is not null).OrderByDescending(d => d.Nota).Take(6)
                .Select(d => new { d.Nome, Time = Time(d.ClubeId), d.Funcao, Nota = Math.Round(d.Nota!.Value), d.Gols, d.Assistencias }),
            a.Observacoes,
            // do mapa de finalizações: contagens por time (o cartão "mapa" do vídeo mostra onde foi cada uma)
            FraseDoMapa = FraseDoMapa(p, mapa),
            Finalizacoes = mapa is null ? null : new[] { (p.MandanteId, p.Mandante), (p.VisitanteId, p.Visitante) }.ToDictionary(t => t.Item2, t =>
            {
                var doTime = mapa.Finalizacoes.Where(f => f.ClubeId == t.Item1).ToList();
                return new { Total = doTime.Count, DentroDaArea = doTime.Count(f => f.DentroDaArea), NoAlvo = doTime.Count(f => f.NoAlvo) };
            }),
            // de qual time é cada próximo jogo, para a IA não anunciar o jogo do adversário como se fosse do Galo
            ProximoJogo = new[] { (p.MandanteId, p.Mandante), (p.VisitanteId, p.Visitante) }.ToDictionary(t => t.Item2, t =>
                a.JogosSeguintes?.Where(j => j.MandanteId == t.Item1 || j.VisitanteId == t.Item1)
                    .Select(j => $"{j.Mandante} x {j.Visitante} ({j.Competicao}, {Dia(j.Data)})").FirstOrDefault()),
        });
    }

    /// <summary>Percentil (0 a 100) como frase: 79 → "melhor que em 79% dos próprios jogos", 11 → "pior que em 89% dos próprios jogos".</summary>
    public static string? Leitura(double? percentil) => percentil is not { } v ? null
        : v >= 50 ? $"melhor que em {Math.Round(v)}% dos próprios jogos da temporada"
        : $"pior que em {Math.Round(100 - v)}% dos próprios jogos da temporada";

    const string Instrucoes = """
        Você escreve o roteiro do Short de pós-jogo do canal "Futebol em Números" (YouTube e TikTok), do Leandro, torcedor do Galo
        (Atlético-MG) que analisa futebol com a IA do AlpaFlow Scout. Escreva em português do Brasil, tom de conversa de torcedor que
        entende de números, sem exagero e sem inventar nada além dos dados recebidos.

        Formato exato (títulos em maiúsculas, nesta ordem):
        TÍTULO: até 70 caracteres, com o confronto e o placar e um gancho (ex.: "Galo 2 x 1 Grêmio: onde o Galo ganhou o jogo").
        GANCHO (primeiros 2 segundos): uma frase curta e forte, que faça a pessoa ficar. Pode ser uma pergunta.
        FALA (cerca de 40 segundos, 90 a 110 palavras): em primeira pessoa, para o Leandro ler. Conte o placar e o contexto em uma
        frase, explique onde o jogo foi ganho ou perdido com 2 ou 3 aspectos, cite 1 ou 2 destaques pela nota da IA e termine com
        "Me segue que no próximo jogo tem mais." Antes dessa frase, cite o próximo jogo do Galo (ou, se o Galo não jogou, do time
        que venceu), pelo ProximoJogo daquele time. Nunca anuncie o próximo jogo do outro time.
        CARTÕES: linhas curtas com o que mostrar na tela, na ordem da fala (placar, os aspectos, os destaques e, se houver
        Finalizacoes, o mapa de finalizações).
        LEGENDA: 2 ou 3 frases para o post, mais 4 ou 5 hashtags (#Galo #Atlético quando o jogo for do Galo, a competição, o adversário).

        Regras dos números:
        - Cada aspecto já vem escrito para cada time ("melhor que em 79% dos próprios jogos da temporada", "pior que em 89%...").
          Use essas frases como estão, nunca transforme em outra porcentagem ("só 11% do melhor desempenho" está errado).
          Não cite números brutos de posse (%) nem de passes.
        - "Chances criadas (xG)" também vem como frase: nunca diga um valor de xG.
        - Quando vier FraseDoMapa, use-a na FALA (pode ajustar a ordem das palavras, nunca os números). É a frase das finalizações.
        - Quando vier Finalizacoes (contagens reais do jogo, do mapa): a FALA tem de ter uma frase com elas, comparando os dois times
          ("o Galo finalizou 17 vezes, 13 de dentro da área, e acertou o alvo 9 vezes, igual ao Cruzeiro") e chamando o mapa
          ("olha no mapa"). Finalização se fala só por essas contagens, nunca por percentil.
        - Percentil não é quantidade: nunca compare os dois times em "o dobro" ou "mais escanteios que o rival" pelos percentis.
        - Nota do jogo vai de 0 a 100 e é da IA: "nota 78 da IA".
        - Se o jogo foi decidido nos pênaltis ou na prorrogação (Desfecho), diga.
        - Os gols vêm em ordem cronológica, com o acréscimo ("90+3") e o placar depois de cada um (PlacarDepois). Use o PlacarDepois
          para dizer qual gol empatou e qual virou ou decidiu: nunca deduza pela ordem dos nomes.
        - Datas já vêm com o dia da semana: use exatamente esse dia, nunca calcule outro.
        """;
}
