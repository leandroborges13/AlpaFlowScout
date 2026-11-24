using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Sul-americanos sem espaço (item 24 do backlog: "brasileiros sem espaço na Europa que caberiam no Galo") ----------

/// <summary>
/// Um atleta encostado: o que joga agora (`ClubeAtual`, `LigaAtual`, minutos e % dos minutos do clube) e a última liga em que
/// jogou de verdade (450+ min), com a nota daquela liga em `Referencia`. `Referencia` nulo: nunca jogou 450 min numa liga da base.
/// </summary>
public record AtletaSemEspaco(int Id, string Nome, int Idade, string? Nacionalidade, string? Posicao, string ClubeAtual, string LigaAtual,
    int TemporadaAtual, int JogosAtual, int MinutosAtual, int ParticipacaoAtual, ResumoAtleta? Referencia,
    long? ValorMercado = null, DateOnly? ContratoAte = null);

public record ResultadoSemEspaco(IReadOnlyList<AtletaSemEspaco> Atletas, IReadOnlyList<string> Observacoes);

/// <summary>
/// `Ligas`: onde procurar (padrão: as 8 da Europa); `Nacionalidades`: padrão, as da América do Sul.
/// `ParticipacaoMaxima`: % dos minutos do clube na temporada atual (padrão 30). `Posicao`: Goleiro, Defesa, Meio ou Ataque.
/// </summary>
/// `RealidadeBrasileira` (padrão sim): só quem vale até os 10% mais caros da Série A no setor; `ValorMaximo`: teto em euros.
public record BuscarSemEspaco(int[]? Ligas = null, string[]? Nacionalidades = null, int ParticipacaoMaxima = 30, int? IdadeMax = null,
    Setor? Posicao = null, int Limite = 20, bool RealidadeBrasileira = true, long? ValorMaximo = null,
    // Só quem já mostrou nível: nota mínima na liga de referência (sem referência = nunca jogou 450 min numa liga da base: fica fora)
    double NotaMinima = 55) : IQuery<ResultadoSemEspaco>;

public class BuscarSemEspacoHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<BuscarSemEspaco, ResultadoSemEspaco>
{
    /// <summary>Inglaterra, Espanha, Itália, Alemanha, França, Turquia, Holanda e Bélgica (ids da API-Football).</summary>
    public static readonly int[] LigasDaEuropa = [39, 140, 135, 78, 61, 203, 88, 144];

    public static readonly string[] SulAmericanos =
        ["Brazil", "Argentina", "Uruguay", "Colombia", "Chile", "Paraguay", "Peru", "Ecuador", "Venezuela", "Bolivia"];

    static readonly Dictionary<string, Setor> SetorDoProvedor = new()
    {
        ["Goalkeeper"] = Setor.Goleiro, ["Defender"] = Setor.Defesa, ["Midfielder"] = Setor.Meio, ["Attacker"] = Setor.Ataque,
    };

    public async Task<ResultadoSemEspaco> Handle(BuscarSemEspaco q, CancellationToken ct)
    {
        var ligas = q.Ligas is { Length: > 0 } l ? l : LigasDaEuropa;
        var nacionalidades = q.Nacionalidades is { Length: > 0 } n ? n : SulAmericanos;
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        // Temporada atual de cada liga = a mais recente com estatística
        var atuais = await db.Estatisticas.AsNoTracking().Where(e => ligas.Contains(e.CompeticaoId))
            .GroupBy(e => e.CompeticaoId).Select(g => new { Liga = g.Key, Temporada = g.Max(e => e.Temporada) }).ToListAsync(ct);
        if (atuais.Count == 0) return new ResultadoSemEspaco([], ["Nenhuma das ligas pedidas tem dados na base ainda."]);

        var registros = new List<(EstatisticaTemporada e, int jogosDoClube)>();
        foreach (var a in atuais)
        {
            var daLiga = await db.Estatisticas.AsNoTracking().Include(e => e.Atleta).Include(e => e.Clube).Include(e => e.Competicao)
                .Where(e => e.CompeticaoId == a.Liga && e.Temporada == a.Temporada).ToListAsync(ct);
            // Jogos do clube na liga ≈ o máximo de jogos de um atleta dele (sem escalação importada, não há a lista de partidas)
            var jogosDoClube = daLiga.GroupBy(e => e.ClubeId).ToDictionary(g => g.Key, g => g.Max(e => e.Jogos));
            registros.AddRange(daLiga.Select(e => (e, jogosDoClube[e.ClubeId])));
        }

        var tetos = q.RealidadeBrasileira ? await BuscarPorPerfilHandler.TetosDaSerieA(db, motor, ct) : null;
        // Encostado: da nacionalidade pedida, 18+, e com menos que a participação máxima. Atleta em dois clubes da liga soma os minutos.
        var candidatos = registros
            .Where(r => BuscarPorPerfilHandler.CabeNoBolso(r.e.Atleta.ValorMercado,
                SetorDoProvedor.GetValueOrDefault(r.e.PosicaoProvedor ?? "", Setor.Meio), q.ValorMaximo, tetos))
            .Where(r => r.e.Atleta.Nacionalidade is { } nac && nacionalidades.Contains(nac, StringComparer.OrdinalIgnoreCase))
            .Where(r => r.e.Atleta.PodeSerAnalisado(hoje) && (q.IdadeMax is null || r.e.Atleta.IdadeEm(hoje) <= q.IdadeMax))
            .Where(r => q.Posicao is null || SetorDoProvedor.GetValueOrDefault(r.e.PosicaoProvedor ?? "") == q.Posicao)
            .GroupBy(r => r.e.AtletaId)
            .Select(g =>
            {
                var principal = g.OrderByDescending(r => r.e.Minutos).First();
                var minutos = g.Sum(r => r.e.Minutos);
                var possivel = Math.Max(1, principal.jogosDoClube) * 90;
                return (r: principal, jogos: g.Sum(r => r.e.Jogos), minutos, participacao: (int)Math.Round(100.0 * minutos / possivel));
            })
            // clube que ainda jogou pouco (início de temporada) não diz nada
            .Where(x => x.r.jogosDoClube >= 5 && x.participacao < q.ParticipacaoMaxima)
            .ToList();

        // Elenco europeu desatualizado: quem entrou em campo por outro clube nos últimos 60 dias não está encostado lá
        // (ex.: o goleiro listado no Juventus que joga pelo Botafogo)
        var ids = candidatos.Select(c => c.r.e.AtletaId).ToList();
        var desde = DateTime.UtcNow.AddDays(-ElencoAtual.DiasSemJogar);
        var recentes = (await (from e in db.Escalacoes.AsNoTracking()
                               join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                               where ids.Contains(e.AtletaId) && p.Data >= desde
                               select new { e.AtletaId, e.ClubeId })
                .Concat(from d in db.DesempenhosAvancados.AsNoTracking()
                        join p in db.Partidas.AsNoTracking() on d.PartidaId equals p.Id
                        where d.AtletaId != null && ids.Contains(d.AtletaId.Value) && d.Minutos > 0 && p.Data >= desde
                        select new { AtletaId = d.AtletaId!.Value, d.ClubeId })
                .ToListAsync(ct))
            .ToLookup(x => x.AtletaId, x => x.ClubeId);
        var foraDaEuropa = candidatos.Count(c => recentes[c.r.e.AtletaId].Any(clube => clube != c.r.e.ClubeId));
        candidatos = candidatos.Where(c => !recentes[c.r.e.AtletaId].Any(clube => clube != c.r.e.ClubeId)).ToList();
        ids = candidatos.Select(c => c.r.e.AtletaId).ToList();

        // A referência: a liga mais recente (fora a atual) em que jogou 450+ min, com a nota daquela liga
        var anteriores = (await db.Estatisticas.AsNoTracking()
                .Where(e => ids.Contains(e.AtletaId) && e.Minutos >= MotorDeAnalise.MinutosMinimos && e.Competicao.Tipo != Competicao.TipoCopa)
                .Select(e => new { e.AtletaId, e.CompeticaoId, e.Temporada, e.Minutos }).ToListAsync(ct))
            .ToLookup(e => e.AtletaId);

        var lista = new List<AtletaSemEspaco>();
        foreach (var c in candidatos)
        {
            var e = c.r.e;
            ResumoAtleta? referencia = null;
            foreach (var ant in anteriores[e.AtletaId].Where(x => !(x.CompeticaoId == e.CompeticaoId && x.Temporada == e.Temporada))
                         .OrderByDescending(x => x.Temporada).ThenByDescending(x => x.Minutos))
            {
                var linha = (await motor.Analisar(ant.CompeticaoId, ant.Temporada, ct))?.Linhas.FirstOrDefault(x => x.AtletaId == e.AtletaId);
                if (linha is null) continue;
                referencia = Resumir.De(linha);
                break;
            }
            lista.Add(new AtletaSemEspaco(e.AtletaId, e.Atleta.Nome, e.Atleta.IdadeEm(hoje)!.Value, e.Atleta.Nacionalidade, e.PosicaoProvedor,
                e.Clube.Nome, e.Competicao.Nome, e.Temporada, c.jogos, c.minutos, c.participacao, referencia,
                e.Atleta.ValorMercado, e.Atleta.ContratoAte));
        }

        // Primeiro quem já mostrou nível (maior nota na referência), depois quem nunca teve 450 min
        var abaixo = lista.Count(x => x.Referencia?.Nota is not { } n || n < q.NotaMinima);
        var ordenados = lista.Where(x => x.Referencia?.Nota >= q.NotaMinima)
            .OrderByDescending(x => x.Referencia!.Nota).ThenBy(x => x.Idade).Take(Math.Clamp(q.Limite, 1, 50)).ToList();
        var observacoes = new List<string>
        {
            $"Encostado = menos de {q.ParticipacaoMaxima}% dos minutos do clube na temporada atual (clubes com 5+ jogos). " +
            "A nota é da última liga em que ele jogou 450+ min, comparado com o setor daquela liga; não é nota na Série A.",
            "Pouco minuto pode ser lesão ou chegada recente: conferir antes de citar em vídeo.",
        };
        if (abaixo > 0)
            observacoes.Add($"{abaixo} encostado(s) sem referência ou com nota abaixo de {q.NotaMinima:0} na última liga em que jogaram ficaram de fora.");
        if (foraDaEuropa > 0)
            observacoes.Add($"{foraDaEuropa} atleta(s) listados num clube europeu, mas com jogo recente por outro clube, ficaram de fora (elenco do provedor desatualizado).");
        if (tetos is { Count: > 0 })
            observacoes.Add("Só quem cabe na realidade do futebol brasileiro: valor de mercado até o dos 10% mais caros da Série A no setor (" +
                string.Join(", ", tetos.OrderBy(t => t.Key).Select(t => $"{Resumir.NomeSetor(t.Key).ToLowerInvariant()} até {BuscarPorPerfilHandler.Euros(t.Value)}")) +
                "). Sem valor conhecido, entra sem o número.");
        return new ResultadoSemEspaco(ordenados, observacoes);
    }
}
