using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Revelações de copa (o caminho do Arrascaeta: Danubio → Libertadores → Cruzeiro) ----------

/// <summary>
/// Atletas de clubes cuja liga não está na base (Uruguai, Paraguai, Chile, Peru, Bolívia, Venezuela: a liga uruguaia, por exemplo,
/// não tem estatística por atleta em nenhuma das nossas fontes), pela nota na Libertadores e na Sul-Americana. Na copa a régua é
/// boa: a nota é contra todos do mesmo setor no torneio, brasileiros e argentinos inclusive.
/// </summary>
public record BuscarRevelacoesDeCopa(int? Temporada = null, Setor? Setor = null, string? Funcao = null, int? IdadeMax = null,
    int Limite = 15, bool SoClubesSemLiga = true) : IQuery<ResultadoBusca>;

public class BuscarRevelacoesDeCopaHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<BuscarRevelacoesDeCopa, ResultadoBusca>
{
    /// <summary>Libertadores e Sul-Americana (ids da API-Football).</summary>
    public static readonly int[] Copas = [13, 11];

    public async Task<ResultadoBusca> Handle(BuscarRevelacoesDeCopa q, CancellationToken ct)
    {
        var linhas = new List<LinhaAnalise>();
        foreach (var (copa, ano) in await BuscarPorPerfilHandler.CompeticoesAlvo(db, q.Temporada, Copas, ct))
            if (await motor.Analisar(copa, ano, ct) is { } a) linhas.AddRange(a.Linhas);

        // Clubes com liga na base (alguma estatística fora de copa desde a temporada anterior): esses já aparecem na busca de mercado
        var ano0 = linhas.Select(l => l.Temporada).DefaultIfEmpty(DateTime.Today.Year).Max() - 1;
        var clubes = linhas.SelectMany(l => l.ClubeIds).Distinct().ToList();
        var comLiga = (await db.Estatisticas.AsNoTracking()
                .Where(e => clubes.Contains(e.ClubeId) && e.Temporada >= ano0 && e.Competicao.Tipo != Competicao.TipoCopa)
                .Select(e => e.ClubeId).Distinct().ToListAsync(ct)).ToHashSet();

        var candidatos = linhas
            .Where(l => !q.SoClubesSemLiga || !l.ClubeIds.Any(comLiga.Contains))
            .Where(l => q.Setor is null || l.Setor == q.Setor)
            .Where(l => q.Funcao is null || BuscarPorPerfilHandler.FuncaoPrincipal(l, q.Funcao))
            .Where(l => q.IdadeMax is null || l.Idade <= q.IdadeMax)
            .Where(l => l.Nota is not null)
            // jogou as duas copas: fica a de mais minutos
            .GroupBy(l => l.AtletaId).Select(g => g.OrderByDescending(l => l.Minutos).First())
            .OrderByDescending(l => l.Nota)
            .ToList();

        var resultado = candidatos.Take(Math.Clamp(q.Limite, 1, 50)).Select(l => Resumir.De(l)).ToList();
        var observacoes = new List<string>
        {
            "Nota na copa (Libertadores ou Sul-Americana), contra todos do mesmo setor no torneio, brasileiros e argentinos inclusive; " +
            $"exige {MotorDeAnalise.MinutosMinimos} minutos, ou seja, uns 5 jogos completos.",
            "São clubes de ligas sem estatística na base (Uruguai, Paraguai, Chile, Peru, Bolívia, Venezuela): o desempenho no campeonato nacional não entra.",
            "Amostra de copa é curta: é um sinal para acompanhar, não um veredito.",
        };
        return new ResultadoBusca(candidatos.Count, resultado, observacoes);
    }
}
