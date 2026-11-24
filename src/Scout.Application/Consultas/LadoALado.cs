using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Lado a lado (cartão para vídeo: candidato x quem joga hoje; item 23.4 do backlog) ----------

/// <summary>
/// Uma linha da comparação: o número de cada um (por 90 min ou %) e o percentil no setor da liga dele.
/// `Vence`: "a", "b" ou nulo (empate ou falta o número). Decide pelo número (o que o vídeo mostra), invertido quando menos é melhor.
/// </summary>
public record LinhaLadoALado(string Chave, string Nome, bool Porcentagem, double? ValorA, double? PercentilA, double? ValorB, double? PercentilB, string? Vence);

/// <summary>`NotaA`/`NotaB`: na régua da Série A (atleta de liga de fora convertido; `EstimadaX`: liga com pouco confronto com a Série A).</summary>
public record LadoALado(ResumoAtleta A, ResumoAtleta B, double? NotaA, double? NotaB, bool EstimadaA, bool EstimadaB,
    IReadOnlyList<LinhaLadoALado> Linhas, string Explicacao);

public record ObterLadoALado(int AtletaA, int AtletaB, int? Temporada = null) : IQuery<LadoALado?>;

public class ObterLadoALadoHandler(IScoutDb db, MotorDeAnalise motor, ForcaDasLigas forca) : IQueryHandler<ObterLadoALado, LadoALado?>
{
    public async Task<LadoALado?> Handle(ObterLadoALado q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, null, ct, incluirExterior: true);
        // Como no painel e no cartão do atleta: se jogou mais de uma liga, a de mais minutos
        LinhaAnalise? Linha(int id) => linhas.Where(x => x.AtletaId == id).OrderByDescending(x => x.Minutos).FirstOrDefault();
        if (Linha(q.AtletaA) is not { } a || Linha(q.AtletaB) is not { } b) return null;

        var dasLigas = linhas.Where(x => x.CompeticaoId == a.CompeticaoId || x.CompeticaoId == b.CompeticaoId).ToList();
        var conversor = await forca.Conversor(dasLigas, x => x.Nota, ct);
        var forcas = conversor is null ? null : await forca.Obter(ct);
        double? Nota(LinhaAnalise l) => l.Nota is null ? null : Math.Round((conversor is null ? l.Nota : conversor(l, l.Nota)) ?? l.Nota.Value);
        bool Estimada(LinhaAnalise l) => forcas?.GetValueOrDefault(l.CompeticaoId)?.Estimativa ?? false;

        var vinculos = await ElencoAtual.Avaliar(db, new Dictionary<int, int[]> { [a.AtletaId] = a.ClubeIds, [b.AtletaId] = b.ClubeIds }, ct);
        var resumoA = Resumir.De(a) with { Situacao = vinculos.GetValueOrDefault(a.AtletaId)?.Situacao };
        var resumoB = Resumir.De(b) with { Situacao = vinculos.GetValueOrDefault(b.AtletaId)?.Situacao };

        var mesmaLiga = a.CompeticaoId == b.CompeticaoId && a.Temporada == b.Temporada;
        return new LadoALado(resumoA, resumoB, Nota(a), Nota(b), Estimada(a), Estimada(b), Comparar(a, b),
            mesmaLiga
                ? "Números por 90 minutos na mesma liga. A barra é o percentil entre os do mesmo setor com 450+ min."
                : $"Ligas diferentes ({a.Competicao} e {b.Competicao}): os números por 90 min são de cada liga, e a barra é o percentil no setor da liga de cada um. " +
                  "A nota no topo está na régua da Série A.");
    }

    /// <summary>As linhas do setor do primeiro atleta (o candidato), as mais importantes primeiro.</summary>
    public static List<LinhaLadoALado> Comparar(LinhaAnalise a, LinhaAnalise b) =>
        Chaves(a.Setor).Select(k => Linha(a, b, Catalogo.PorChave[k])).ToList();

    public static string[] Chaves(Setor setor) => setor switch
    {
        Setor.Goleiro => ["defesas90", "golsSofridos90", "precisaoPasses", "passes90"],
        Setor.Defesa => ["desarmes90", "interceptacoes90", "duelosGanhos90", "aproveitamentoDuelos", "passes90", "precisaoPasses", "passesDecisivos90", "faltasCometidas90"],
        Setor.Meio => ["passesDecisivos90", "assistencias90", "precisaoPasses", "dribles90", "desarmes90", "interceptacoes90", "duelosGanhos90", "gols90"],
        _ => ["gols90", "finalizacoesNoAlvo90", "aproveitamentoFinalizacoes", "assistencias90", "passesDecisivos90", "dribles90", "duelosGanhos90", "aproveitamentoDuelos"],
    };

    static LinhaLadoALado Linha(LinhaAnalise a, LinhaAnalise b, Indicador ind)
    {
        var porcentagem = ind.Chave.StartsWith("aproveitamento") || ind.Chave == "precisaoPasses";
        double? Valor(LinhaAnalise l) => (l.Valores.GetValueOrDefault(ind.Chave) ?? ind.Valor(l)) is { } v ? Math.Round(v, porcentagem ? 0 : 2) : null;
        double? Percentil(LinhaAnalise l) => l.Percentis.GetValueOrDefault(ind.Chave) is { } p ? Math.Round(p) : null;
        var va = Valor(a);
        var vb = Valor(b);
        string? vence = va is null || vb is null || va == vb ? null : (va > vb) != ind.Inverso ? "a" : "b";
        return new LinhaLadoALado(ind.Chave, ObterCartaoDoAtletaHandler.Rotulo(ind.Chave), porcentagem, va, Percentil(a), vb, Percentil(b), vence);
    }
}
