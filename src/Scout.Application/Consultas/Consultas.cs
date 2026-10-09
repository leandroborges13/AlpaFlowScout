using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Competições disponíveis ----------

public record CompeticaoDisponivel(int Id, string Nome, int Temporada, int Atletas);

public record ListarCompeticoes : IQuery<IReadOnlyList<CompeticaoDisponivel>>;

public class ListarCompeticoesHandler(IScoutDb db) : IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>>
{
    public async Task<IReadOnlyList<CompeticaoDisponivel>> Handle(ListarCompeticoes q, CancellationToken ct)
    {
        var grupos = await db.Estatisticas.AsNoTracking()
            .Where(e => e.Minutos > 0)
            .GroupBy(e => new { e.CompeticaoId, e.Temporada })
            .Select(g => new { g.Key.CompeticaoId, g.Key.Temporada, Atletas = g.Select(e => e.AtletaId).Distinct().Count() })
            .ToListAsync(ct);
        var nomes = await db.Competicoes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);
        return grupos
            .Select(g => new CompeticaoDisponivel(g.CompeticaoId, nomes.GetValueOrDefault(g.CompeticaoId, $"Liga {g.CompeticaoId}"), g.Temporada, g.Atletas))
            .OrderByDescending(c => c.Temporada).ThenBy(c => c.Id)
            .ToList();
    }
}

// ---------- Busca por perfil ----------

/// <summary>
/// "Zagueiro pela esquerda, bom no jogo aéreo e na saída de bola, até 23 anos".
/// `Atributos` = chave do atributo → peso (1 = importante, 2 = muito importante).
/// </summary>
public record BuscarPorPerfil(
    int? Temporada = null,
    int[]? Competicoes = null,
    Setor? Setor = null,
    string? Funcao = null,
    Lado? Lado = null,
    string? Esquema = null,
    int? IdadeMin = null,
    int? IdadeMax = null,
    int? ParticipacaoMinimaPercentual = null,
    Dictionary<string, double>? Atributos = null,
    int Limite = 10) : IQuery<ResultadoBusca>;

public record ResultadoBusca(int Encontrados, IReadOnlyList<ResumoAtleta> Atletas, IReadOnlyList<string> Observacoes);

public class BuscarPorPerfilHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<BuscarPorPerfil, ResultadoBusca>
{
    public async Task<ResultadoBusca> Handle(BuscarPorPerfil q, CancellationToken ct)
    {
        var linhas = await CarregarLinhas(db, motor, q.Temporada, q.Competicoes, ct);
        var atributos = (q.Atributos ?? [])
            .Where(a => Catalogo.AtributoPorChave.ContainsKey(a.Key) && a.Value > 0)
            .Select(a => (atributo: Catalogo.AtributoPorChave[a.Key], peso: a.Value))
            .ToList();

        var candidatos = linhas.Where(l =>
            (q.Setor is null || l.Setor == q.Setor) &&
            (q.Funcao is null || JogaNaFuncao(l, q.Funcao)) &&
            (q.Lado is null || l.Lado == q.Lado || l.Funcoes.Keys.Any(f => f.Contains(NomeLado(q.Lado.Value), StringComparison.OrdinalIgnoreCase))) &&
            (q.IdadeMin is null || l.Idade >= q.IdadeMin) &&
            (q.IdadeMax is null || l.Idade <= q.IdadeMax) &&
            (q.ParticipacaoMinimaPercentual is null || l.ParticipacaoMinutos * 100 >= q.ParticipacaoMinimaPercentual));

        var pontuados = candidatos
            .Select(l => (linha: l, nota: atributos.Count == 0 ? l.Nota : NotaPonderada(l, atributos)))
            .Where(x => x.nota is not null)
            .OrderByDescending(x => x.nota)
            .ToList();

        var observacoes = atributos.Select(a => a.atributo.Observacao).OfType<string>().Distinct().ToList();
        if (q.Esquema is not null)
            observacoes.Add($"Esquema {q.Esquema}: a busca considera a função em campo; cada atleta traz quantos jogos fez com a mesma linha de defesa.");

        var resultado = pontuados.Take(Math.Clamp(q.Limite, 1, 30))
            .Select(x => Resumir.De(x.linha, x.nota, atributos.Count == 0 ? null : atributos.Select(a => a.atributo), q.Esquema))
            .ToList();
        return new ResultadoBusca(pontuados.Count, resultado, observacoes);
    }

    static double? NotaPonderada(LinhaAnalise l, List<(Atributo atributo, double peso)> atributos)
    {
        double soma = 0, pesos = 0;
        foreach (var (a, peso) in atributos)
        {
            if (Resumir.NotaDoAtributo(l, a) is not { } nota) continue;
            soma += nota * peso;
            pesos += peso;
        }
        return pesos == 0 ? null : soma / pesos;
    }

    static bool JogaNaFuncao(LinhaAnalise l, string funcao) =>
        (l.Funcao?.Contains(funcao, StringComparison.OrdinalIgnoreCase) ?? false) ||
        // Também vale quem fez pelo menos 3 jogos na função pedida, mesmo não sendo a principal
        l.Funcoes.Any(f => f.Key.Contains(funcao, StringComparison.OrdinalIgnoreCase) && f.Value >= 3);

    static string NomeLado(Lado lado) => lado == Lado.Esquerda ? "esquerd" : lado == Lado.Direita ? "direit" : "central";

    /// <summary>
    /// Linhas das competições pedidas (ou de todas). Sem temporada, usa em cada competição a mais recente
    /// que tem estatística: assim a Série C 2025 entra junto com as Séries A e B de 2026.
    /// </summary>
    public static async Task<List<LinhaAnalise>> CarregarLinhas(IScoutDb db, MotorDeAnalise motor, int? temporada, int[]? competicoes, CancellationToken ct)
    {
        var consulta = db.Estatisticas.AsNoTracking().Where(e => e.Minutos > 0);
        if (competicoes is { Length: > 0 }) consulta = consulta.Where(e => competicoes.Contains(e.CompeticaoId));
        if (temporada is not null) consulta = consulta.Where(e => e.Temporada == temporada);
        var alvos = await consulta.GroupBy(e => e.CompeticaoId)
            .Select(g => new { Competicao = g.Key, Temporada = g.Max(e => e.Temporada) })
            .ToListAsync(ct);

        var linhas = new List<LinhaAnalise>();
        foreach (var a in alvos)
            if (await motor.Analisar(a.Competicao, a.Temporada, ct) is { } analise) linhas.AddRange(analise.Linhas);
        return linhas;
    }
}

// ---------- Detalhe do atleta ----------

public record PercentilIndicador(string Nome, double Percentil);

public record DetalheAtleta(ResumoAtleta Resumo, string? EscudoClube,
    IReadOnlyDictionary<string, int> Funcoes, IReadOnlyDictionary<string, int> Esquemas,
    IReadOnlyList<Destaque> Atributos, IReadOnlyList<PercentilIndicador> Indicadores,
    Cartoes Cartoes, Presenca Presenca, IReadOnlyList<JogoNaLinhaDoTempo> LinhaDoTempo,
    TrajetoriaAtleta? Trajetoria, IReadOnlyList<PontoNoCampo> Posicoes, EstiloDeJogo? Estilo);

public record ObterAtleta(int AtletaId, int? Temporada = null, int? CompeticaoId = null, bool ComTrajetoria = true) : IQuery<DetalheAtleta?>;

public class ObterAtletaHandler(IScoutDb db, MotorDeAnalise motor, IHistoricoDoAtleta historico) : IQueryHandler<ObterAtleta, DetalheAtleta?>
{
    public async Task<DetalheAtleta?> Handle(ObterAtleta q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, q.CompeticaoId is { } c ? [c] : null, ct);
        // Se jogou mais de uma competição na temporada, fica a de mais minutos
        var l = linhas.Where(x => x.AtletaId == q.AtletaId).OrderByDescending(x => x.Minutos).FirstOrDefault();
        if (l is null) return null;

        var atributos = Catalogo.Atributos
            .Where(a => a.Chave != "goleiro" || l.Setor == Setor.Goleiro)
            .Select(a => (a, nota: Resumir.NotaDoAtributo(l, a)))
            .Where(x => x.nota is not null)
            .Select(x => new Destaque(x.a.Nome, Math.Round(x.nota!.Value), x.a.Observacao))
            .ToList();
        var indicadores = Catalogo.IndicadoresDoSetor(l.Setor)
            .Where(k => l.Percentis.GetValueOrDefault(k) is not null)
            .Select(k => new PercentilIndicador(Catalogo.PorChave[k].Nome, Math.Round(l.Percentis[k]!.Value)))
            .ToList();

        var linhaDoTempo = await LinhaDoTempo(l, ct);
        var cartoes = new Cartoes(l.Amarelos, l.Vermelhos,
            l.Minutos > 0 ? Math.Round((l.Amarelos + l.Vermelhos) * 90.0 / l.Minutos, 2) : null);
        var trajetoria = q.ComTrajetoria ? await historico.Obter(l.AtletaId, ct) : null;

        return new DetalheAtleta(Resumir.De(l), l.Escudo, l.Funcoes, l.Esquemas, atributos, indicadores,
            cartoes, CalcularPresenca(linhaDoTempo), linhaDoTempo, trajetoria, await Posicoes(l, ct), Estilo(l));
    }

    async Task<List<PontoNoCampo>> Posicoes(LinhaAnalise l, CancellationToken ct)
    {
        var escalacoes = await (from e in db.Escalacoes.AsNoTracking()
                                join p in db.Partidas.AsNoTracking() on e.PartidaId equals p.Id
                                where e.AtletaId == l.AtletaId && p.CompeticaoId == l.CompeticaoId && p.Temporada == l.Temporada
                                select new { e.Esquema, e.Grade, e.Funcao })
            .ToListAsync(ct);
        return escalacoes
            .Select(e => (ponto: FuncaoTatica.Coordenadas(e.Esquema, e.Grade), e.Funcao))
            .Where(x => x.ponto is not null)
            .GroupBy(x => x.ponto!.Value)
            .Select(g => new PontoNoCampo(g.Key.X, g.Key.Y, g.Count(),
                g.GroupBy(x => x.Funcao).OrderByDescending(f => f.Count()).First().Key))
            .OrderByDescending(p => p.Jogos)
            .ToList();
    }

    public static EstiloDeJogo? Estilo(LinhaAnalise l)
    {
        if (l.Setor == Setor.Goleiro) return null;
        double? Nota(string chave) => Resumir.NotaDoAtributo(l, Catalogo.AtributoPorChave[chave]);
        var ofensivo = MotorDeAnalise.Media([Nota("criacao"), Nota("drible"), Nota("finalizacao")]);
        // Só a marcação: jogo aéreo também é arma de ataque (centroavante de área), então fica fora
        var defensivo = Nota("marcacao");
        if (ofensivo is null || defensivo is null) return null;
        var indice = (int)Math.Round(ofensivo.Value - defensivo.Value);
        var rotulo = indice switch { >= 25 => "Bem ofensivo", >= 10 => "Mais ofensivo", <= -25 => "Muito marcador", <= -10 => "Mais marcador", _ => "Equilibrado" };
        var setor = Resumir.NomeSetor(l.Setor).ToLowerInvariant();
        return new EstiloDeJogo(indice, rotulo, Math.Round(ofensivo.Value), Math.Round(defensivo.Value),
            $"Compara criação, drible e finalização com a marcação (desarmes, interceptações e bloqueios), em relação a outros atletas de {setor}.");
    }

    /// <summary>Jogos encerrados dos clubes do atleta na competição, marcando onde ele foi titular.</summary>
    async Task<List<JogoNaLinhaDoTempo>> LinhaDoTempo(LinhaAnalise l, CancellationToken ct)
    {
        var clubes = l.ClubeIds;
        var partidas = await db.Partidas.AsNoTracking()
            .Where(p => p.CompeticaoId == l.CompeticaoId && p.Temporada == l.Temporada
                        && (p.Status == "FT" || p.Status == "AET" || p.Status == "PEN")
                        && (clubes.Contains(p.MandanteId) || clubes.Contains(p.VisitanteId)))
            .OrderBy(p => p.Data)
            .ToListAsync(ct);
        var ids = partidas.Select(p => p.Id).ToList();
        var titularidades = await db.Escalacoes.AsNoTracking()
            .Where(e => e.AtletaId == l.AtletaId && ids.Contains(e.PartidaId))
            .ToDictionaryAsync(e => e.PartidaId, e => e.Funcao, ct);
        var nomes = await db.Clubes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Nome, ct);

        return partidas.Select(p =>
        {
            var emCasa = clubes.Contains(p.MandanteId);
            var adversario = emCasa ? p.VisitanteId : p.MandanteId;
            var placar = p.GolsMandante is null ? null : $"{p.GolsMandante} x {p.GolsVisitante}";
            var titular = titularidades.TryGetValue(p.Id, out var funcao);
            return new JogoNaLinhaDoTempo(p.Data, p.Rodada, nomes.GetValueOrDefault(adversario, "?"), emCasa, placar, titular, funcao);
        }).ToList();
    }

    public static Presenca CalcularPresenca(IReadOnlyList<JogoNaLinhaDoTempo> jogos)
    {
        int atual = 0, maior = 0, corrida = 0;
        foreach (var j in jogos)
        {
            corrida = j.Titular ? corrida + 1 : 0;
            maior = Math.Max(maior, corrida);
        }
        for (var i = jogos.Count - 1; i >= 0 && jogos[i].Titular; i--) atual++;
        return new Presenca(jogos.Count, jogos.Count(j => j.Titular), atual, maior);
    }
}

// ---------- Parecidos com ----------

public record AtletasParecidos(int AtletaId, int? Temporada = null, int? IdadeMax = null, int Limite = 5) : IQuery<ResultadoParecidos?>;

public record AtletaParecido(ResumoAtleta Atleta, double Semelhanca);

public record ResultadoParecidos(ResumoAtleta Referencia, IReadOnlyList<AtletaParecido> Parecidos);

public class AtletasParecidosHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<AtletasParecidos, ResultadoParecidos?>
{
    public async Task<ResultadoParecidos?> Handle(AtletasParecidos q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, null, ct);
        var referencia = linhas.Where(l => l.AtletaId == q.AtletaId).OrderByDescending(l => l.Minutos).FirstOrDefault();
        if (referencia is null) return null;

        // Semelhança = 100 − diferença média dos percentis nos indicadores do setor
        var chaves = Catalogo.IndicadoresDoSetor(referencia.Setor);
        var parecidos = linhas
            .Where(l => l.AtletaId != referencia.AtletaId && l.Setor == referencia.Setor && (q.IdadeMax is null || l.Idade <= q.IdadeMax))
            .Select(l => (linha: l, semelhanca: Semelhanca(referencia, l, chaves)))
            .Where(x => x.semelhanca is not null)
            .OrderByDescending(x => x.semelhanca)
            .Take(Math.Clamp(q.Limite, 1, 20))
            .Select(x => new AtletaParecido(Resumir.De(x.linha), Math.Round(x.semelhanca!.Value, 1)))
            .ToList();
        return new ResultadoParecidos(Resumir.De(referencia), parecidos);
    }

    public static double? Semelhanca(LinhaAnalise a, LinhaAnalise b, string[] chaves)
    {
        var diferencas = chaves
            .Select(k => (a.Percentis.GetValueOrDefault(k), b.Percentis.GetValueOrDefault(k)))
            .Where(p => p.Item1 is not null && p.Item2 is not null)
            .Select(p => Math.Abs(p.Item1!.Value - p.Item2!.Value))
            .ToList();
        return diferencas.Count < 3 ? null : 100 - diferencas.Average();
    }
}

// ---------- Oportunidades ----------

/// <summary>Jovens que já são peça importante no clube e estão entre os melhores da posição.</summary>
public record BuscarOportunidades(int? Temporada = null, int[]? Competicoes = null, int IdadeMax = 23,
    int ParticipacaoMinimaPercentual = 40, double NotaMinima = 65, int Limite = 15) : IQuery<IReadOnlyList<ResumoAtleta>>;

public class BuscarOportunidadesHandler(IScoutDb db, MotorDeAnalise motor) : IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>>
{
    public async Task<IReadOnlyList<ResumoAtleta>> Handle(BuscarOportunidades q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, q.Competicoes, ct);
        return linhas
            .Where(l => l.Idade <= q.IdadeMax && l.ParticipacaoMinutos * 100 >= q.ParticipacaoMinimaPercentual && l.Nota >= q.NotaMinima)
            .OrderByDescending(l => l.Nota)
            .Take(Math.Clamp(q.Limite, 1, 50))
            .Select(l => Resumir.De(l))
            .ToList();
    }
}
