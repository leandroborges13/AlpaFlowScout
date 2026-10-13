using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Atletas do treinador ----------

/// <summary>Um atleta que o treinador escalou: quantas vezes e em que função.</summary>
public record AtletaDoTreinador(ResumoAtleta Atleta, int JogosComEle, string? FuncaoComEle);

/// <summary>Atletas mais escalados pelo treinador nas passagens que estão na base (só os analisáveis: 18 anos ou mais).</summary>
public record AtletasDoTreinador(int TreinadorId, int Limite = 15) : IQuery<IReadOnlyList<AtletaDoTreinador>>;

public class AtletasDoTreinadorHandler(IScoutDb db, AnaliseDeTimes analise, MotorDeAnalise motor) : IQueryHandler<AtletasDoTreinador, IReadOnlyList<AtletaDoTreinador>>
{
    public async Task<IReadOnlyList<AtletaDoTreinador>> Handle(AtletasDoTreinador q, CancellationToken ct)
    {
        var analises = await ListarTimesHandler.CarregarAnalises(db, analise, null, null, ct);
        var resultado = new List<AtletaDoTreinador>();
        foreach (var a in analises)
            foreach (var p in a.Treinadores.Where(p => p.TreinadorId == q.TreinadorId))
            {
                var linhas = (await motor.Analisar(a.CompeticaoId, a.Temporada, ct))?.Linhas.ToDictionary(l => l.AtletaId) ?? [];
                foreach (var e in await Escalados(db, p, ct))
                    if (linhas.TryGetValue(e.AtletaId, out var l)) resultado.Add(new AtletaDoTreinador(Resumir.De(l), e.Jogos, e.Funcao));
            }
        return resultado.OrderByDescending(r => r.JogosComEle).Take(Math.Clamp(q.Limite, 1, 40)).ToList();
    }

    /// <summary>Quem começou jogando nos jogos da passagem: jogos como titular e função mais frequente.</summary>
    public static async Task<List<(int AtletaId, int Jogos, string? Funcao)>> Escalados(IScoutDb db, PassagemNaTemporada p, CancellationToken ct)
    {
        var ids = p.Partidas.ToList();
        var clube = p.Linha.ClubeId;
        var escalacoes = await db.Escalacoes.AsNoTracking().Where(e => ids.Contains(e.PartidaId) && e.ClubeId == clube)
            .Select(e => new { e.AtletaId, e.Funcao }).ToListAsync(ct);
        return escalacoes.GroupBy(e => e.AtletaId)
            .Select(g => (g.Key, g.Count(), (string?)g.GroupBy(e => e.Funcao).OrderByDescending(f => f.Count()).First().Key))
            .ToList();
    }
}

// ---------- Atletas com o perfil do treinador ----------

/// <summary>Uma função do esquema do treinador: o perfil de quem ele costuma escalar ali e atletas parecidos com esse perfil.</summary>
public record PerfilNaFuncao(string Funcao, int Jogos, double? NotaTitulares, double? PercentilNaCompeticao,
    IReadOnlyList<Destaque> Perfil, IReadOnlyList<string> QuemEleEscala, IReadOnlyList<AtletaParecido> Sugestoes);

public record ResultadoAtletasParaTreinador(ResumoTreinador Treinador, IReadOnlyList<PerfilNaFuncao> Funcoes, IReadOnlyList<string> Observacoes);

public record AtletasParaTreinador(int TreinadorId, string? Funcao = null, int? IdadeMax = null, int[]? Competicoes = null, int PorFuncao = 3)
    : IQuery<ResultadoAtletasParaTreinador?>;

public class AtletasParaTreinadorHandler(IScoutDb db, AnaliseDeTimes analise, MotorDeAnalise motor) : IQueryHandler<AtletasParaTreinador, ResultadoAtletasParaTreinador?>
{
    public async Task<ResultadoAtletasParaTreinador?> Handle(AtletasParaTreinador q, CancellationToken ct)
    {
        var analises = await ListarTimesHandler.CarregarAnalises(db, analise, null, null, ct);
        var atual = analises.SelectMany(x => x.Treinadores.Select(t => (analise: x, passagem: t)))
            .Where(x => x.passagem.TreinadorId == q.TreinadorId)
            .OrderByDescending(x => x.passagem.Fim).FirstOrDefault();
        if (atual.passagem is null) return null;
        var (a, p) = atual;

        var daCompeticao = (await motor.Analisar(a.CompeticaoId, a.Temporada, ct))?.Linhas.ToDictionary(l => l.AtletaId) ?? [];
        var ids = p.Partidas.ToList();
        var escalacoes = await db.Escalacoes.AsNoTracking().Where(e => ids.Contains(e.PartidaId) && e.ClubeId == p.Linha.ClubeId)
            .Select(e => new { e.PartidaId, e.AtletaId, e.Funcao, e.Setor }).ToListAsync(ct);
        var esquema = p.Linha.EsquemasPorJogo.GroupBy(e => e.Esquema).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        var carencias = a.Times.FirstOrDefault(t => t.ClubeId == p.Linha.ClubeId)?.Funcoes.ToDictionary(f => f.Funcao, f => f.PercentilNaCompeticao) ?? [];
        var candidatos = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, null, q.Competicoes, ct);

        var funcoes = new List<PerfilNaFuncao>();
        foreach (var g in escalacoes.GroupBy(e => e.Funcao))
        {
            var jogos = g.Select(e => e.PartidaId).Distinct().Count();
            if (jogos < Math.Max(3, p.Partidas.Count * 0.4)) continue; // fora do plano de jogo principal dele
            if (q.Funcao is not null && !CatalogoTimes.FuncoesEquivalentes(g.Key).Any(f => f.Contains(q.Funcao, StringComparison.OrdinalIgnoreCase))) continue;

            if (g.First().Setor == Setor.Goleiro) continue; // dois indicadores só: não dá para comparar estilo
            var titulares = g.GroupBy(e => e.AtletaId).Where(x => daCompeticao.ContainsKey(x.Key))
                .Select(x => (linha: daCompeticao[x.Key], peso: x.Count())).ToList();
            if (titulares.Count == 0) continue;
            var perfil = Perfil(g.First().Setor, titulares);
            var chaves = Catalogo.IndicadoresDoSetor(perfil.Setor);
            var equivalentes = CatalogoTimes.FuncoesEquivalentes(g.Key).ToHashSet();
            var jaEscalados = titulares.Select(t => t.linha.AtletaId).ToHashSet();

            var sugestoes = candidatos
                .Where(l => !jaEscalados.Contains(l.AtletaId) && !l.ClubeIds.Contains(p.Linha.ClubeId)
                            && (equivalentes.Contains(l.Funcao ?? "") || l.Funcoes.Any(f => equivalentes.Contains(f.Key) && f.Value >= 3))
                            && (q.IdadeMax is null || l.Idade <= q.IdadeMax) && l.ParticipacaoMinutos >= 0.3
                            // Parecido com quem ele escala, no mesmo nível ou acima e acima da mediana da própria competição
                            && l.Nota >= Math.Max(50, (perfil.Nota ?? 0) - 5))
                .Select(l => (l, s: AtletasParecidosHandler.Semelhanca(perfil, l, chaves)))
                .Where(x => x.s is not null)
                .OrderByDescending(x => x.s)
                .Take(Math.Clamp(q.PorFuncao, 1, 10))
                .Select(x => new AtletaParecido(Resumir.De(x.l, esquema: esquema), Math.Round(x.s!.Value, 1)))
                .ToList();

            funcoes.Add(new PerfilNaFuncao(g.Key, jogos, perfil.Nota is { } n ? Math.Round(n) : null,
                carencias.GetValueOrDefault(g.Key) is { } pc ? Math.Round(pc) : null,
                Catalogo.Atributos.Where(at => at.Chave != "goleiro" || perfil.Setor == Setor.Goleiro)
                    .Select(at => (at, nota: Resumir.NotaDoAtributo(perfil, at))).Where(x => x.nota is not null)
                    .OrderByDescending(x => x.nota).Take(3)
                    .Select(x => new Destaque(x.at.Nome, Math.Round(x.nota!.Value), x.at.Observacao)).ToList(),
                titulares.OrderByDescending(t => t.peso).Take(3).Select(t => $"{t.linha.Nome} ({t.peso})").ToList(),
                sugestoes));
        }

        // Primeiro as funções em que o time dele está pior servido
        var ordenadas = funcoes.OrderBy(f => f.PercentilNaCompeticao ?? 50).ToList();
        var observacoes = new List<string>
        {
            "Perfil = média dos percentis de quem ele escalou na função, pesada pelos jogos. As sugestões são atletas parecidos com esse perfil, no mesmo nível ou acima, e acima da mediana da própria competição. Goleiros ficam de fora.",
            "Sugestões de todas as competições da base: as notas são relativas a cada competição.",
        };
        return new ResultadoAtletasParaTreinador(ResumirTreinador.De(p), ordenadas, observacoes);
    }

    /// <summary>O "atleta típico" que o treinador escala na função: média dos percentis pesada pelos jogos.</summary>
    public static LinhaAnalise Perfil(Setor setor, List<(LinhaAnalise linha, int peso)> titulares)
    {
        var perfil = new LinhaAnalise { Nome = "Perfil do treinador", Setor = setor };
        foreach (var ind in Catalogo.Indicadores)
        {
            var com = titulares.Where(t => t.linha.Percentis.GetValueOrDefault(ind.Chave) is not null).ToList();
            perfil.Percentis[ind.Chave] = com.Count == 0 ? null : com.Sum(t => t.linha.Percentis[ind.Chave]!.Value * t.peso) / com.Sum(t => t.peso);
        }
        var comNota = titulares.Where(t => t.linha.Nota is not null).ToList();
        perfil.Nota = comNota.Count == 0 ? null : comNota.Sum(t => t.linha.Nota!.Value * t.peso) / comNota.Sum(t => t.peso);
        return perfil;
    }
}
