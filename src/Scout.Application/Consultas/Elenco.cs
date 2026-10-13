using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

/// <summary>Jogos, minutos e gols de um atleta numa competição (fatos públicos, sem estatística do provedor).</summary>
public record RegistroNaCompeticao(string Clube, string Competicao, int Temporada, bool Copa, int Jogos, int Titular, int Minutos, int Gols, int Assistencias);

/// <summary>
/// O que a base sabe de um atleta, inclusive de quem não tem nota: o motivo real de estar fora da análise
/// e os fatos de cada competição. Menor de 18 anos não tem detalhe nenhum.
/// </summary>
public record FichaAtleta(int Id, string Nome, int? Idade, bool TemNota, string? Motivo, IReadOnlyList<RegistroNaCompeticao> Registros);

public record ObterFichaAtleta(int AtletaId) : IQuery<FichaAtleta?>;

public class ObterFichaAtletaHandler(IScoutDb db) : IQueryHandler<ObterFichaAtleta, FichaAtleta?>
{
    public async Task<FichaAtleta?> Handle(ObterFichaAtleta q, CancellationToken ct) =>
        (await Fichas(db, [q.AtletaId], ct)).FirstOrDefault();

    public static async Task<List<FichaAtleta>> Fichas(IScoutDb db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var atletas = await db.Atletas.AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        var registros = (await db.Estatisticas.AsNoTracking()
                .Where(e => ids.Contains(e.AtletaId) && e.Minutos > 0)
                .Select(e => new { e.AtletaId, Clube = e.Clube.Nome, Competicao = e.Competicao.Nome, e.Competicao.Tipo, e.Temporada,
                    e.Jogos, e.Titular, e.Minutos, e.Gols, e.Assistencias })
                .ToListAsync(ct))
            .ToLookup(e => e.AtletaId);
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        return atletas.Select(a =>
        {
            var doAtleta = registros[a.Id].OrderByDescending(r => r.Temporada).ThenByDescending(r => r.Minutos).ToList();
            var referencia = MotorDeAnalise.ReferenciaDa(doAtleta.Select(r => r.Temporada).DefaultIfEmpty(hoje.Year).Max(), hoje);
            var idade = a.IdadeEm(referencia);
            if (idade is null)
                return new FichaAtleta(a.Id, a.Nome, null, false, "O provedor não informa a data de nascimento. Os dados de jogo existem, mas ficam ocultos até a data ser cadastrada (comando nascimento, com uma fonte pública), porque sem ela não dá para garantir que tem 18 anos ou mais.", []);
            if (idade < Atleta.IdadeMinimaAnalise)
                return new FichaAtleta(a.Id, a.Nome, null, false, "Menor de 18 anos: fica fora de qualquer análise individual.", []);

            var lista = doAtleta.Select(r => new RegistroNaCompeticao(r.Clube, r.Competicao, r.Temporada, r.Tipo == Competicao.TipoCopa,
                r.Jogos, r.Titular, r.Minutos, r.Gols, r.Assistencias)).ToList();
            var minutosNaLiga = lista.Where(r => !r.Copa).GroupBy(r => (r.Competicao, r.Temporada)).Select(g => g.Sum(r => r.Minutos)).DefaultIfEmpty(0).Max();
            var temNota = minutosNaLiga >= MotorDeAnalise.MinutosMinimos;
            string? motivo = temNota ? null
                : lista.Count == 0 ? "Sem minutos registrados nas competições da base."
                : $"Jogou {minutosNaLiga} minutos na liga; a nota exige pelo menos {MotorDeAnalise.MinutosMinimos}. Abaixo disso, só os fatos (jogos, minutos, gols).";
            return new FichaAtleta(a.Id, a.Nome, idade, temNota, motivo, lista);
        }).ToList();
    }
}

// ---------- Elenco do time ----------

/// <summary>`TitularComo`: em quais funções começou jogando e quantas vezes (ex.: Lateral esquerdo: 4, Ala esquerdo: 2).</summary>
public record AtletaNoElenco(int Id, string Nome, int Idade, string? Funcao, int Jogos, int Titular, int Minutos, int Gols, int Assistencias, double? Nota,
    IReadOnlyDictionary<string, int> TitularComo);

public record ElencoDoTime(string Time, string Competicao, int Temporada, IReadOnlyList<AtletaNoElenco> Atletas, int MenoresOmitidos);

/// <summary>Todos que jogaram pelo time na competição, do mais usado ao menos usado: é aqui que aparecem os reservas.</summary>
/// <remarks>Com `Funcao`, só quem já foi titular nela (o mais usado é o titular; os demais, reservas) e quem é do mesmo setor e ainda não foi titular.</remarks>
public record ObterElenco(int ClubeId, int? CompeticaoId = null, int? Temporada = null, string? Funcao = null) : IQuery<ElencoDoTime?>;

public class ObterElencoHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<ObterElenco, ElencoDoTime?>
{
    public async Task<ElencoDoTime?> Handle(ObterElenco q, CancellationToken ct)
    {
        // Sem competição pedida, vale a liga mais recente do clube
        var consulta = db.Estatisticas.AsNoTracking().Where(e => e.ClubeId == q.ClubeId && e.Minutos > 0);
        if (q.CompeticaoId is { } c) consulta = consulta.Where(e => e.CompeticaoId == c);
        else consulta = consulta.Where(e => e.Competicao.Tipo != Competicao.TipoCopa);
        if (q.Temporada is { } t) consulta = consulta.Where(e => e.Temporada == t);
        var alvo = await consulta.OrderByDescending(e => e.Temporada).Select(e => new { e.CompeticaoId, e.Temporada, Competicao = e.Competicao.Nome, Clube = e.Clube.Nome })
            .FirstOrDefaultAsync(ct);
        if (alvo is null) return null;

        var registros = await db.Estatisticas.AsNoTracking()
            .Where(e => e.ClubeId == q.ClubeId && e.CompeticaoId == alvo.CompeticaoId && e.Temporada == alvo.Temporada && e.Minutos > 0)
            .Include(e => e.Atleta)
            .ToListAsync(ct);
        var titularComo = (await (from e in db.Escalacoes.AsNoTracking()
                              join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                              where e.ClubeId == q.ClubeId && p.CompeticaoId == alvo.CompeticaoId && p.Temporada == alvo.Temporada
                              select new { e.AtletaId, e.Funcao }).ToListAsync(ct))
            .GroupBy(e => e.AtletaId)
            .ToDictionary(g => g.Key, g => g.GroupBy(e => e.Funcao).OrderByDescending(f => f.Count()).ToDictionary(f => f.Key, f => f.Count()));
        var notas = (await motor.Analisar(alvo.CompeticaoId, alvo.Temporada, ct))?.Linhas.ToDictionary(l => l.AtletaId, l => l.Nota) ?? [];
        var referencia = MotorDeAnalise.ReferenciaDa(alvo.Temporada, DateOnly.FromDateTime(DateTime.Today));

        var adultos = registros.Where(r => r.Atleta.PodeSerAnalisado(referencia)).ToList();
        var atletas = adultos
            .Select(r => new AtletaNoElenco(r.AtletaId, r.Atleta.Nome, r.Atleta.IdadeEm(referencia)!.Value,
                titularComo.GetValueOrDefault(r.AtletaId)?.Keys.First() ?? PosicaoDoProvedor(r.PosicaoProvedor),
                r.Jogos, r.Titular, r.Minutos, r.Gols, r.Assistencias,
                notas.GetValueOrDefault(r.AtletaId) is { } n ? Math.Round(n, 1) : null,
                titularComo.GetValueOrDefault(r.AtletaId) ?? new Dictionary<string, int>()))
            .Where(a => q.Funcao is null || NaFuncao(a, q.Funcao))
            .OrderByDescending(a => q.Funcao is null ? a.Minutos : TitularidadesNa(a, q.Funcao)).ThenByDescending(a => a.Minutos)
            .ToList();
        return new ElencoDoTime(alvo.Clube, alvo.Competicao, alvo.Temporada, atletas, registros.Count - adultos.Count);
    }

    static bool Combina(string funcaoDoAtleta, string pedida) =>
        CatalogoTimes.FuncoesEquivalentes(funcaoDoAtleta).Any(f => f.Contains(pedida, StringComparison.OrdinalIgnoreCase));

    static int TitularidadesNa(AtletaNoElenco a, string funcao) => a.TitularComo.Where(f => Combina(f.Key, funcao)).Sum(f => f.Value);

    /// <summary>Já foi titular na função, ou é do mesmo setor e ainda não foi titular em nada (o reserva que quase não joga).</summary>
    static bool NaFuncao(AtletaNoElenco a, string funcao) =>
        TitularidadesNa(a, funcao) > 0 || (a.TitularComo.Count == 0 && a.Funcao == SetorDa(funcao));

    static string SetorDa(string funcao) => funcao.ToLowerInvariant() switch
    {
        var f when f.Contains("goleiro") => "Goleiro",
        var f when f.Contains("lateral") || f.Contains("zagueiro") || f.Contains("ala") => "Defensor",
        var f when f.Contains("ponta") || f.Contains("centroavante") || f.Contains("atacante") => "Atacante",
        _ => "Meio-campista",
    };

    static string? PosicaoDoProvedor(string? p) => p switch
    {
        "Goalkeeper" => "Goleiro", "Defender" => "Defensor", "Midfielder" => "Meio-campista", "Attacker" => "Atacante", _ => null,
    };
}
