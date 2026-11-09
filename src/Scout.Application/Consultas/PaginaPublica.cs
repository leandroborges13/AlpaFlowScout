using Microsoft.EntityFrameworkCore;

namespace Scout.Application.Consultas;

// ---------- Página pública de cada jogo (backlog item 5: link da bio e da descrição dos vídeos) ----------

public record GolNaPagina(string Minuto, string Time, string Jogador, string? Assistencia, string? Detalhe);
public record DestaqueNaPagina(string Nome, string Time, string? Funcao, int Nota, int Gols, int Assistencias);

/// <summary>
/// O que a página aberta (sem login) mostra de um jogo: placar, gols, onde o jogo foi decidido (percentis), os destaques pela nota
/// e o palpite do modelo com "acertou ou errou?". Sem fotos de atleta (direito de imagem) e sem quem tem menos de 18 anos
/// (a análise já troca o nome por "Atleta sub-18" e a nota só existe para quem o motor analisa).
/// </summary>
public record PaginaPublica(ResumoPartida Partida, string? Desfecho, IReadOnlyList<GolNaPagina> Gols, IReadOnlyList<AspectoDaPartida> Aspectos,
    IReadOnlyList<DestaqueNaPagina> Destaques, Palpite? Palpite, string Explicacao);

public record ObterPaginaPublica(int PartidaId) : IQuery<PaginaPublica?>;

public class ObterPaginaPublicaHandler(IScoutDb db, IQueryHandler<AnalisarPartida, AnalisePartida?> analisar, IQueryHandler<ObterPalpite, ResultadoPalpite?> palpite)
    : IQueryHandler<ObterPaginaPublica, PaginaPublica?>
{
    public async Task<PaginaPublica?> Handle(ObterPaginaPublica q, CancellationToken ct)
    {
        var partida = await db.Partidas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == q.PartidaId, ct);
        if (partida is null) return null;
        var r = await palpite.Handle(new ObterPalpite(partida.Id), ct);
        // Jogo por vir: só o palpite. Jogo encerrado: a análise, desde que os detalhes já estejam na base (a página aberta
        // nunca dispara consulta ao provedor).
        if (!partida.Encerrada)
            return r?.Partida is { } futura ? new PaginaPublica(futura, null, [], [], [], r.Palpite, r.Explicacao) : null;
        if (!partida.EscalacoesImportadas) return null;

        var a = await analisar.Handle(new AnalisarPartida(partida.Id), ct);
        if (a is null) return null;
        var p = a.Partida;
        string Time(int clubeId) => clubeId == p.MandanteId ? p.Mandante : p.Visitante;
        var gols = a.Eventos.Where(e => e.Tipo.StartsWith("Gol"))
            .OrderBy(e => e.Minuto).ThenBy(e => e.Acrescimo ?? 0)
            .Select(e => new GolNaPagina(e.Acrescimo is { } x ? $"{e.Minuto}+{x}'" : $"{e.Minuto}'", Time(e.ClubeId), e.Jogador ?? "?", e.Outro,
                e.Tipo == "Gol" ? null : e.Tipo.Replace("Gol ", "").Replace("de ", "")))
            .ToList();
        var destaques = (a.Desempenhos ?? []).Where(d => d.Nota is not null && d.Nome != "Atleta sub-18")
            .OrderByDescending(d => d.Nota).Take(6)
            .Select(d => new DestaqueNaPagina(d.Nome, Time(d.ClubeId), d.Funcao, (int)Math.Round(d.Nota!.Value), d.Gols, d.Assistencias))
            .ToList();
        return new PaginaPublica(p, a.Desfecho, gols, a.Aspectos.Where(x => x.PercentilMandante is not null && x.PercentilVisitante is not null).ToList(),
            destaques, r?.Palpite,
            "Percentil = como o time foi neste jogo comparado com os próprios jogos na temporada (100 = o melhor jogo dele naquele aspecto). " +
            "Nota de 0 a 100 = como o atleta foi comparado com os do mesmo setor na temporada. Análise da IA do AlpaFlow Scout.");
    }
}
