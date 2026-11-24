using Microsoft.EntityFrameworkCore;
using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

// ---------- Competições disponíveis ----------

/// <summary>`Copa` = mata-mata ou copa continental: entra em jogos, times e treinadores, mas não na nota dos atletas.</summary>
public record CompeticaoDisponivel(int Id, string Nome, int Temporada, int Atletas, bool Copa);

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
        var competicoes = await db.Competicoes.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        return grupos
            .Select(g => new CompeticaoDisponivel(g.CompeticaoId, competicoes.GetValueOrDefault(g.CompeticaoId)?.Nome ?? $"Liga {g.CompeticaoId}",
                g.Temporada, g.Atletas, competicoes.GetValueOrDefault(g.CompeticaoId)?.EhCopa ?? false))
            .OrderBy(c => c.Copa).ThenByDescending(c => c.Temporada).ThenBy(c => c.Id)
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
    int Limite = 10,
    // Mapa de mercado: sem competição pedida, junta a Série A e as ligas de fora (na régua da Série A); Série B e C ficam de fora
    // porque não têm conversão para a Série A
    bool Mercado = false,
    int? MinutosMinimos = null,
    // Tira os atletas desse clube (ex.: o próprio Galo, ao procurar reforço)
    int? ExcluirClubeId = null,
    // Só quem continua no clube da linha (ver ElencoAtual); sem isso, quem saiu aparece com a Situacao preenchida
    bool SoElencoAtual = false,
    // Teto de valor de mercado em euros (ex.: 5_000_000)
    long? ValorMaximo = null,
    // Realidade do futebol brasileiro: no máximo o valor dos 10% mais caros da Série A no mesmo setor (TetosDaSerieA)
    bool RealidadeBrasileira = false,
    // false: tira as ligas cuja régua da Série A é só estimativa (MLS, Liga MX...), que podem inflar a nota convertida
    bool IncluirEstimadas = true,
    // Oportunidade de mercado: contrato terminando até essa data (sem contrato conhecido, fica de fora quando pedido)
    DateOnly? ContratoAteMax = null,
    // Só atletas de ligas de fora do Brasil (o reforço "de fora", mais viável que tirar titular de rival)
    bool SoLigasDeFora = false) : IQuery<ResultadoBusca>;

public record ResultadoBusca(int Encontrados, IReadOnlyList<ResumoAtleta> Atletas, IReadOnlyList<string> Observacoes);

public class BuscarPorPerfilHandler(IScoutDb db, MotorDeAnalise motor, ForcaDasLigas forca) : IQueryHandler<BuscarPorPerfil, ResultadoBusca>
{
    public async Task<ResultadoBusca> Handle(BuscarPorPerfil q, CancellationToken ct)
    {
        var linhas = await CarregarLinhas(db, motor, q.Temporada, q.Competicoes, ct, incluirExterior: q.Mercado);
        if (q.Mercado && q.Competicoes is not { Length: > 0 })
        {
            var ids = linhas.Select(l => l.CompeticaoId).Distinct().ToList();
            var brasileiras = await db.Competicoes.AsNoTracking()
                .Where(c => ids.Contains(c.Id) && (c.Pais == "Brazil" || c.Pais == "") && (q.SoLigasDeFora || c.Id != ForcaDasLigas.Referencia))
                .Select(c => c.Id).ToListAsync(ct);
            linhas = linhas.Where(l => !brasileiras.Contains(l.CompeticaoId)).ToList();
        }
        var tetos = q.RealidadeBrasileira ? await TetosDaSerieA(db, motor, ct) : null;
        var atributos = (q.Atributos ?? [])
            .Where(a => Catalogo.AtributoPorChave.ContainsKey(a.Key) && a.Value > 0)
            .Select(a => (atributo: Catalogo.AtributoPorChave[a.Key], peso: a.Value))
            .ToList();

        var candidatos = linhas.Where(l =>
            (q.Setor is null || l.Setor == q.Setor) &&
            (q.Funcao is null || (q.Mercado ? FuncaoPrincipal(l, q.Funcao) : JogaNaFuncao(l, q.Funcao))) &&
            (q.Lado is null || l.Lado == q.Lado || l.Funcoes.Keys.Any(f => f.Contains(NomeLado(q.Lado.Value), StringComparison.OrdinalIgnoreCase))) &&
            (q.IdadeMin is null || l.Idade >= q.IdadeMin) &&
            (q.IdadeMax is null || l.Idade <= q.IdadeMax) &&
            (q.ParticipacaoMinimaPercentual is null || l.ParticipacaoMinutos * 100 >= q.ParticipacaoMinimaPercentual) &&
            (q.MinutosMinimos is null || l.Minutos >= q.MinutosMinimos) &&
            (q.ExcluirClubeId is null || !l.ClubeIds.Contains(q.ExcluirClubeId.Value)) &&
            (q.ContratoAteMax is null || (l.ContratoAte is { } fim && fim <= q.ContratoAteMax)) &&
            // o teto é o da posição pedida (um meia que joga de centroavante paga o teto do ataque)
            CabeNoBolso(l.ValorMercado, (q.Funcao is { } f ? SetorDaFuncao(f) : q.Setor) ?? l.Setor, q.ValorMaximo, tetos));

        double? Pontuar(LinhaAnalise l) => atributos.Count == 0 ? l.Nota : NotaPonderada(l, atributos);
        // Com liga de fora na busca, a ordem é pela pontuação convertida para a Série A (as notas de ligas diferentes não se comparam)
        var conversor = await forca.Conversor(linhas, Pontuar, ct);
        var forcas = conversor is null ? null : await forca.Obter(ct);
        // Liga com régua estimada (pouco confronto direto com a Série A) pode inflar a nota convertida: fica de fora se pedido
        var estimadasFora = q.IncluirEstimadas || forcas is null ? [] :
            forcas.Values.Where(f => f.Estimativa && candidatos.Any(l => l.CompeticaoId == f.CompeticaoId)).ToList();
        var pontuados = candidatos
            .Where(l => estimadasFora.All(f => f.CompeticaoId != l.CompeticaoId))
            .Select(l => (linha: l, nota: Pontuar(l)))
            .Where(x => x.nota is not null)
            .Select(x => (x.linha, x.nota, serieA: conversor?.Invoke(x.linha, x.nota)))
            .Where(x => conversor is null || x.serieA is not null)
            .OrderByDescending(x => conversor is null ? x.nota : x.serieA)
            .ToList();

        var observacoes = atributos.Select(a => a.atributo.Observacao).OfType<string>().Distinct().ToList();
        if (estimadasFora.Count > 0)
            observacoes.Add($"Ficaram de fora as ligas com régua só estimada (pouco confronto direto com clubes brasileiros, a nota convertida pode inflar): " +
                $"{string.Join(", ", estimadasFora.Select(f => f.Competicao))}. Peça incluindo as estimadas para vê-las.");
        if (conversor is not null)
        {
            observacoes.Add("Atletas de ligas de fora: ordenados pela nota convertida para a Série A, pelo nível da liga medido nos confrontos de copa (Libertadores, Sul-Americana, Mundial de Clubes).");
            var semRegua = candidatos.Where(l => conversor(l, Pontuar(l)) is null && Pontuar(l) is not null).Select(l => l.Competicao).Distinct().ToList();
            if (semRegua.Count > 0)
                observacoes.Add($"Fora da ordem por ainda não ter confrontos suficientes com clubes brasileiros para comparar: {string.Join(", ", semRegua)}. Peça a liga sozinha para ver os atletas dela.");
            if (forcas!.Values.Where(f => f.Estimativa && pontuados.Any(p => p.linha.CompeticaoId == f.CompeticaoId)).Select(f => f.Competicao).ToList() is { Count: > 0 } estimadas)
                observacoes.Add($"Nota na Série A estimada (poucos jogos diretos com clubes brasileiros): {string.Join(", ", estimadas)}.");
        }
        if (tetos is { Count: > 0 })
            observacoes.Add("Realidade do futebol brasileiro: só entra quem vale até o que valem os 10% mais caros da Série A no mesmo setor (" +
                string.Join(", ", tetos.OrderBy(t => t.Key).Select(t => $"{Resumir.NomeSetor(t.Key).ToLowerInvariant()} até {Euros(t.Value)}")) +
                "). Valor de mercado é estimativa pública (Bzzoiro); sem valor conhecido, o atleta entra e vem sem o número.");
        if (q.ValorMaximo is { } vm) observacoes.Add($"Valor de mercado até {Euros(vm)}.");
        if (q.ContratoAteMax is { } ca)
            observacoes.Add($"Oportunidade: contrato terminando até {ca:MM/yyyy} (atleta sem contrato conhecido fica de fora). Fim de contrato costuma baratear ou zerar a transferência.");
        if (q.SoLigasDeFora) observacoes.Add("Só ligas de fora do Brasil.");
        if (q.Esquema is not null)
            observacoes.Add($"Esquema {q.Esquema}: a busca considera a função em campo; cada atleta traz quantos jogos fez com a mesma linha de defesa.");

        // Vínculo atual só de quem pode entrar na lista (a janela cobre os que saem com SoElencoAtual)
        var limite = Math.Clamp(q.Limite, 1, 30);
        var janela = pontuados.Take(limite * 4).ToList();
        var vinculos = await ElencoAtual.Avaliar(db, janela.GroupBy(x => x.linha.AtletaId).ToDictionary(g => g.Key, g => g.First().linha.ClubeIds), ct);
        var foraDoElenco = janela.Count(x => vinculos.GetValueOrDefault(x.linha.AtletaId) is { NoElenco: false });
        if (q.SoElencoAtual && foraDoElenco > 0)
            observacoes.Add($"{foraDoElenco} atleta(s) que saíram do clube ou não jogam há mais de {ElencoAtual.DiasSemJogar} dias ficaram de fora.");
        else if (foraDoElenco > 0)
            observacoes.Add("Quem saiu do clube ou não joga há mais de 60 dias vem com a Situacao preenchida: conferir antes de citar.");

        var resultado = janela
            .Where(x => !q.SoElencoAtual || vinculos.GetValueOrDefault(x.linha.AtletaId) is not { NoElenco: false })
            .Take(limite)
            .Select(x => Resumir.De(x.linha, x.nota, atributos.Count == 0 ? null : atributos.Select(a => a.atributo), q.Esquema,
                x.serieA is { } s && s != x.nota ? s : null,
                forcas?.GetValueOrDefault(x.linha.CompeticaoId)?.Estimativa ?? false) with { Situacao = vinculos.GetValueOrDefault(x.linha.AtletaId)?.Situacao })
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

    /// <summary>
    /// Teto de valor por setor = percentil 90 dos valores de mercado conhecidos na Série A mais recente (precisa de 10 valores
    /// no setor). É a "realidade financeira do futebol brasileiro": acima disso, nenhum clube daqui costuma pagar.
    /// </summary>
    public static async Task<Dictionary<Setor, long>> TetosDaSerieA(IScoutDb db, MotorDeAnalise motor, CancellationToken ct)
    {
        var alvo = (await CompeticoesAlvo(db, null, [ForcaDasLigas.Referencia], ct)).FirstOrDefault();
        if (alvo == default || await motor.Analisar(alvo.Competicao, alvo.Temporada, ct) is not { } a) return [];
        return Tetos(a.Linhas.Select(l => (l.Setor, l.ValorMercado)));
    }

    public static Dictionary<Setor, long> Tetos(IEnumerable<(Setor Setor, long? Valor)> valores) =>
        valores.Where(v => v.Valor is > 0).GroupBy(v => v.Setor)
            .Select(g => (g.Key, ordenados: g.Select(v => v.Valor!.Value).OrderBy(v => v).ToList()))
            .Where(x => x.ordenados.Count >= 10)
            .ToDictionary(x => x.Key, x => x.ordenados[(int)Math.Ceiling(0.9 * x.ordenados.Count) - 1]);

    /// <summary>Sem valor conhecido, passa (fica sem o número no card); com valor, respeita o teto pedido e o da Série A.</summary>
    public static bool CabeNoBolso(long? valor, Setor setor, long? maximo, IReadOnlyDictionary<Setor, long>? tetos) =>
        valor is null || ((maximo is null || valor <= maximo) && (tetos is null || !tetos.TryGetValue(setor, out var t) || valor <= t));

    /// <summary>Setor de uma função em campo ("Centroavante" → Ataque, "Lateral esquerdo" → Defesa).</summary>
    public static Setor SetorDaFuncao(string funcao) => funcao.ToLowerInvariant() switch
    {
        var f when f.Contains("goleiro") => Setor.Goleiro,
        var f when f.Contains("zagueiro") || f.Contains("lateral") || f.Contains("ala ") || f == "ala" || f.StartsWith("ala ") => Setor.Defesa,
        var f when f.Contains("centroavante") || f.Contains("atacante") || f.Contains("ponta") => Setor.Ataque,
        _ => Setor.Meio,
    };

    public static string Euros(long v) => string.Format(System.Globalization.CultureInfo.GetCultureInfo("pt-BR"),
        v >= 1_000_000 ? "€ {0:0.#} mi" : "€ {1:0} mil", v / 1_000_000.0, v / 1_000.0);

    /// <summary>
    /// Mercado: a função pedida é a dele, não um quebra-galho. Pelo menos 40% das titularidades nela
    /// (um meia que fez 14 de 33 jogos como centroavante não entra como centroavante). Sem escalação, vale a função principal.
    /// </summary>
    public static bool FuncaoPrincipal(LinhaAnalise l, string funcao)
    {
        var total = l.Funcoes.Values.Sum();
        if (total == 0) return l.Funcao?.Contains(funcao, StringComparison.OrdinalIgnoreCase) ?? false;
        var nela = l.Funcoes.Where(f => f.Key.Contains(funcao, StringComparison.OrdinalIgnoreCase)).Sum(f => f.Value);
        return nela >= 3 && nela >= total * 0.4;
    }

    static string NomeLado(Lado lado) => lado == Lado.Esquerda ? "esquerd" : lado == Lado.Direita ? "direit" : "central";

    /// <summary>
    /// Linhas das competições pedidas (ou de todas as ligas). Sem temporada, usa em cada competição a mais recente
    /// que tem estatística: assim a Série C 2025 entra junto com as Séries A e B de 2026.
    /// Copas só entram quando pedidas: poucos jogos e times de divisões diferentes não dão uma nota justa.
    /// Ligas de fora do Brasil (ex.: 128, argentina) também só entram quando pedidas ou com `incluirExterior`: assim os rankings
    /// "do Brasileirão" não mudam por ter outra liga no banco (a nota é calculada dentro de cada liga e não se compara entre elas).
    /// </summary>
    public static async Task<List<LinhaAnalise>> CarregarLinhas(IScoutDb db, MotorDeAnalise motor, int? temporada, int[]? competicoes, CancellationToken ct,
        bool incluirExterior = false)
    {
        var linhas = new List<LinhaAnalise>();
        foreach (var (competicao, ano) in await CompeticoesAlvo(db, temporada, competicoes, ct, incluirCopas: false, incluirExterior: incluirExterior))
            if (await motor.Analisar(competicao, ano, ct) is { } analise) linhas.AddRange(analise.Linhas);
        return linhas;
    }

    /// <summary>
    /// Competição e temporada a analisar: as pedidas, ou a mais recente com estatística de cada competição.
    /// `incluirCopas: false` deixa as copas de fora quando nenhuma competição foi pedida.
    /// </summary>
    public static async Task<List<(int Competicao, int Temporada)>> CompeticoesAlvo(IScoutDb db, int? temporada, int[]? competicoes, CancellationToken ct,
        bool incluirCopas = true, bool incluirExterior = false)
    {
        var consulta = db.Estatisticas.AsNoTracking().Where(e => e.Minutos > 0);
        if (competicoes is { Length: > 0 }) consulta = consulta.Where(e => competicoes.Contains(e.CompeticaoId));
        else
        {
            if (!incluirCopas) consulta = consulta.Where(e => e.Competicao.Tipo != Competicao.TipoCopa);
            // liga de outro país só quando pedida (copa continental tem país "World" e segue a regra das copas)
            if (!incluirExterior) consulta = consulta.Where(e => e.Competicao.Tipo == Competicao.TipoCopa || e.Competicao.Pais == "" || e.Competicao.Pais == "Brazil");
        }
        if (temporada is not null) consulta = consulta.Where(e => e.Temporada == temporada);
        var alvos = await consulta.GroupBy(e => e.CompeticaoId)
            .Select(g => new { Competicao = g.Key, Temporada = g.Max(e => e.Temporada) })
            .ToListAsync(ct);
        return alvos.Select(a => (a.Competicao, a.Temporada)).ToList();
    }
}

// ---------- Detalhe do atleta ----------

public record PercentilIndicador(string Nome, double Percentil);

public record DetalheAtleta(ResumoAtleta Resumo, string? EscudoClube,
    IReadOnlyDictionary<string, int> Funcoes, IReadOnlyDictionary<string, int> Esquemas,
    IReadOnlyList<Destaque> Atributos, IReadOnlyList<PercentilIndicador> Indicadores,
    Cartoes Cartoes, Presenca Presenca, IReadOnlyList<JogoNaLinhaDoTempo> LinhaDoTempo,
    TrajetoriaAtleta? Trajetoria, IReadOnlyList<PontoNoCampo> Posicoes, EstiloDeJogo? Estilo,
    IReadOnlyList<DesempenhoEmCopa> Copas);

/// <summary>
/// O atleta numa copa: fatos (jogos, minutos, gols) e, se jogou o bastante (450 minutos), a nota e os destaques
/// em percentil entre os atletas do mesmo setor naquela copa. Fica separado da nota da liga.
/// </summary>
public record DesempenhoEmCopa(int CompeticaoId, string Competicao, int Temporada, int Jogos, int Minutos, int Gols, int Assistencias,
    double? Nota, IReadOnlyList<Destaque> Destaques);

public record ObterAtleta(int AtletaId, int? Temporada = null, int? CompeticaoId = null, bool ComTrajetoria = true) : IQuery<DetalheAtleta?>;

public class ObterAtletaHandler(IScoutDb db, MotorDeAnalise motor, IHistoricoDoAtleta historico, ForcaDasLigas forca) : IQueryHandler<ObterAtleta, DetalheAtleta?>
{
    public async Task<DetalheAtleta?> Handle(ObterAtleta q, CancellationToken ct)
    {
        var linhas = await BuscarPorPerfilHandler.CarregarLinhas(db, motor, q.Temporada, q.CompeticaoId is { } c ? [c] : null, ct, incluirExterior: true);
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

        // Liga de fora: a nota também na escala da Série A
        var conversor = await forca.Conversor(linhas.Where(x => x.CompeticaoId == l.CompeticaoId).ToList(), x => x.Nota, ct);
        var serieA = conversor?.Invoke(l, l.Nota);
        var estimada = conversor is not null && (await forca.Obter(ct)).GetValueOrDefault(l.CompeticaoId)?.Estimativa == true;
        return new DetalheAtleta(Resumir.De(l, notaSerieA: serieA is { } s && s != l.Nota ? s : null, notaSerieAEstimada: estimada), l.Escudo, l.Funcoes, l.Esquemas, atributos, indicadores,
            cartoes, CalcularPresenca(linhaDoTempo), linhaDoTempo, trajetoria, await Posicoes(l, ct), Estilo(l), await Copas(l, ct));
    }

    /// <summary>Copas da mesma temporada da liga em diante (Copa do Brasil, Libertadores, Sul-Americana...).</summary>
    async Task<List<DesempenhoEmCopa>> Copas(LinhaAnalise l, CancellationToken ct)
    {
        var registros = await db.Estatisticas.AsNoTracking()
            .Where(e => e.AtletaId == l.AtletaId && e.Temporada >= l.Temporada && e.Minutos > 0 && e.Competicao.Tipo == Competicao.TipoCopa)
            .Select(e => new { e.CompeticaoId, Nome = e.Competicao.Nome, e.Temporada, e.Jogos, e.Minutos, e.Gols, e.Assistencias })
            .ToListAsync(ct);
        var copas = new List<DesempenhoEmCopa>();
        foreach (var g in registros.GroupBy(r => (r.CompeticaoId, r.Nome, r.Temporada)))
        {
            // Na copa o atleta pode não ter minutos suficientes para a nota: aí ficam só os fatos
            var linha = (await motor.Analisar(g.Key.CompeticaoId, g.Key.Temporada, ct))?.Linhas.FirstOrDefault(x => x.AtletaId == l.AtletaId);
            var destaques = linha is null ? [] : Resumir.De(linha).Destaques;
            copas.Add(new DesempenhoEmCopa(g.Key.CompeticaoId, g.Key.Nome, g.Key.Temporada, g.Sum(r => r.Jogos), g.Sum(r => r.Minutos),
                g.Sum(r => r.Gols), g.Sum(r => r.Assistencias), linha?.Nota is { } n ? Math.Round(n, 1) : null, destaques));
        }
        return copas.OrderByDescending(c => c.Temporada).ThenByDescending(c => c.Minutos).ToList();
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
