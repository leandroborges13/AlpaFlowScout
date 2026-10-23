using System.ComponentModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Scout.Application.Analise;
using Scout.Application.Consultas;
using Scout.Domain;

namespace Scout.Application.Chat;

public record MensagemChat(string Papel, string Texto);

/// <summary>Resposta do chat: o texto e os atletas citados e os times citados, para a tela mostrar os cartões.</summary>
public record RespostaChat(string Texto, IReadOnlyList<ResumoAtleta> Atletas, IReadOnlyList<ResumoTime> Times, IReadOnlyList<ResumoTreinador> Treinadores,
    IReadOnlyList<ResumoPartida> Partidas);

/// <summary>
/// Agente de scouting: entende o pedido em linguagem natural e consulta o banco pelas ferramentas.
/// A IA nunca inventa número: tudo o que ela diz vem das consultas.
/// </summary>
public class AssistenteScout(
    IChatClient chat, IScoutDb db,
    IQueryHandler<BuscarPorPerfil, ResultadoBusca> buscar,
    IQueryHandler<ObterAtleta, DetalheAtleta?> detalhar,
    IQueryHandler<AtletasParecidos, ResultadoParecidos?> parecidos,
    IQueryHandler<BuscarOportunidades, IReadOnlyList<ResumoAtleta>> oportunidades,
    IQueryHandler<ListarCompeticoes, IReadOnlyList<CompeticaoDisponivel>> competicoes,
    IQueryHandler<ListarTimes, IReadOnlyList<ResumoTime>> listarTimes,
    IQueryHandler<ObterTime, DetalheTime?> detalharTime,
    IQueryHandler<BuscarTimesComCarencia, IReadOnlyList<TimeComCarencia>> carencias,
    IQueryHandler<TimesParaAtleta, ResultadoTimesParaAtleta?> timesParaAtleta,
    IQueryHandler<ListarTreinadores, IReadOnlyList<ResumoTreinador>> listarTreinadores,
    IQueryHandler<ObterTreinador, DetalheTreinador?> detalharTreinador,
    IQueryHandler<AtletasDoTreinador, IReadOnlyList<AtletaDoTreinador>> atletasDoTreinador,
    IQueryHandler<AtletasParaTreinador, ResultadoAtletasParaTreinador?> atletasParaTreinador,
    IQueryHandler<ProcurarPartidas, IReadOnlyList<ResumoPartida>> procurarPartidas,
    IQueryHandler<AnalisarPartida, AnalisePartida?> analisarPartida,
    IQueryHandler<ObterFichaAtleta, FichaAtleta?> fichaAtleta,
    IQueryHandler<ObterElenco, ElencoDoTime?> elenco,
    IQueryHandler<ObterCopasDoTime, CopasDoTime?> copasDoTime,
    IQueryHandler<ObterRankingPenaltis, RankingPenaltis> penaltis,
    IMemoryCache cache,
    ILogger<AssistenteScout> log)
{
    public async Task<RespostaChat> Responder(IReadOnlyList<MensagemChat> conversa, CancellationToken ct)
    {
        var disponiveis = await competicoes.Handle(new ListarCompeticoes(), ct);
        var temporadaPadrao = disponiveis.Select(c => c.Temporada).DefaultIfEmpty(DateTime.Today.Year).Max();
        var citados = new Dictionary<int, ResumoAtleta>();
        void Citar(IEnumerable<ResumoAtleta> atletas) { foreach (var a in atletas) citados[a.Id] = a; }
        var timesCitados = new Dictionary<int, ResumoTime>();
        void CitarTimes(IEnumerable<ResumoTime> times) { foreach (var t in times) timesCitados[t.Id] = t; }
        var treinadoresCitados = new Dictionary<int, ResumoTreinador>();
        void CitarTreinadores(IEnumerable<ResumoTreinador> lista) { foreach (var t in lista) treinadoresCitados.TryAdd(t.Id, t); }
        var partidasCitadas = new Dictionary<int, ResumoPartida>();

        var ferramentas = new List<AITool>
        {
            AIFunctionFactory.Create(
                async ([Description("Ano da temporada; omita para usar a mais recente de cada competição")] int? temporada = null,
                       [Description("Goleiro, Defesa, Meio ou Ataque")] string? setor = null,
                       [Description("Função em campo, ex.: 'Zagueiro esquerdo', 'Lateral direito', 'Volante', 'Ponta esquerda', 'Centroavante', 'Ala esquerdo'")] string? funcao = null,
                       [Description("Esquerda, Direita ou Centro")] string? lado = null,
                       [Description("Esquema tático pedido, ex.: '3-4-3'")] string? esquema = null,
                       int? idadeMinima = null, int? idadeMaxima = null,
                       [Description("% mínimo dos minutos do clube que o atleta jogou (ex.: 50)")] int? participacaoMinima = null,
                       [Description("Chaves dos atributos importantes (peso 1), ex.: jogo_aereo, saida_de_bola, marcacao")] string[]? atributos = null,
                       [Description("Chaves dos atributos muito importantes (peso 2)")] string[]? atributosMuitoImportantes = null,
                       [Description("IDs de competição para restringir; omita para todas")] int[]? competicoes = null,
                       int? limite = null) =>
                {
                    log.LogInformation("buscar_atletas setor={Setor} funcao={Funcao} lado={Lado} esquema={Esquema} idade={Min}-{Max} part={Part} atributos={A} fortes={F} comp={C} temporada={T}",
                        setor, funcao, lado, esquema, idadeMinima, idadeMaxima, participacaoMinima, string.Join(",", atributos ?? []), string.Join(",", atributosMuitoImportantes ?? []), string.Join(",", competicoes ?? []), temporada);
                    var pesos = new Dictionary<string, double>();
                    foreach (var a in atributos ?? []) pesos[a] = 1;
                    foreach (var a in atributosMuitoImportantes ?? []) pesos[a] = 2;
                    var r = await buscar.Handle(new BuscarPorPerfil(
                        temporada, competicoes,
                        Enum.TryParse<Setor>(setor, true, out var s) ? s : null, funcao,
                        Enum.TryParse<Lado>(lado, true, out var l) ? l : null, esquema,
                        idadeMinima, idadeMaxima, participacaoMinima, pesos, limite ?? 5), ct);
                    Citar(r.Atletas);
                    return r;
                },
                "buscar_atletas",
                "Busca atletas por perfil (função, lado, idade, atributos) e devolve os mais aderentes com nota de encaixe 0–100."),
            AIFunctionFactory.Create(
                async (int atletaId, int? temporada = null) =>
                {
                    var d = await detalhar.Handle(new ObterAtleta(atletaId, temporada, ComTrajetoria: false), ct);
                    // Sem nota: devolve o motivo real (idade, minutos) e os fatos, para a IA não chutar o porquê
                    if (d is null) return (object?)await fichaAtleta.Handle(new ObterFichaAtleta(atletaId), ct) ?? "Atleta não encontrado na base.";
                    Citar([d.Resumo]);
                    // Sem a linha do tempo jogo a jogo: o resumo da presença basta para a conversa
                    return (object)new { d.Resumo, d.Funcoes, d.Esquemas, d.Atributos, d.Cartoes, d.Presenca, d.Estilo, DesempenhoEmCopas = d.Copas };
                },
                "detalhar_atleta",
                "Perfil completo de um atleta: funções que já fez, esquemas, nota de cada atributo e percentis."),
            AIFunctionFactory.Create(
                async (int atletaId, int? idadeMaxima = null, int? temporada = null, int? limite = null) =>
                {
                    var r = await parecidos.Handle(new AtletasParecidos(atletaId, temporada, idadeMaxima, limite ?? 5), ct);
                    if (r is not null) Citar(r.Parecidos.Select(p => p.Atleta));
                    return (object?)r ?? "Atleta de referência não encontrado.";
                },
                "atletas_parecidos",
                "Atletas com perfil estatístico parecido com o de um atleta de referência (semelhança 0–100)."),
            AIFunctionFactory.Create(
                async (int? temporada = null, int? idadeMaxima = null, int? limite = null) =>
                {
                    var r = await oportunidades.Handle(new BuscarOportunidades(temporada, IdadeMax: idadeMaxima ?? 23, Limite: limite ?? 8), ct);
                    Citar(r);
                    return r;
                },
                "oportunidades",
                "Jovens que já jogam bastante no clube e estão entre os melhores da posição."),
            AIFunctionFactory.Create(
                async ([Description("Nome ou parte do nome do atleta")] string nome) =>
                {
                    // Poucos milhares de nomes: a busca tolerante (sem acento, com erro de digitação) roda em memória
                    var atletas = await db.Atletas.AsNoTracking().Select(a => new { a.Id, a.Nome, a.NomeCompleto }).ToListAsync(ct);
                    var achados = BuscaPorNome.Filtrar(atletas, nome, a => $"{a.Nome} {a.NomeCompleto}").Take(8).ToList();
                    // Clube e competição de cada um, para escolher o certo entre homônimos ("Fernando do Bragantino")
                    var fichas = (await ObterFichaAtletaHandler.Fichas(db, achados.Select(a => a.Id).ToList(), ct)).ToDictionary(f => f.Id);
                    return achados.Select(a => fichas.GetValueOrDefault(a.Id) is { } f
                        ? (object)new { a.Id, a.Nome, a.NomeCompleto, f.Idade, f.TemNota, f.Motivo, Clubes = f.Registros.Take(3).Select(r => $"{r.Clube} ({r.Competicao} {r.Temporada}, {r.Minutos} min)") }
                        : new { a.Id, a.Nome, a.NomeCompleto }).ToList();
                },
                "procurar_atleta_por_nome",
                "Encontra o ID de um atleta pelo nome, com o clube e a competição de cada resultado (para escolher o certo entre homônimos)."),
            AIFunctionFactory.Create(
                async (int timeId, [Description("ID da competição; omita para a liga do time")] int? competicao = null, int? temporada = null,
                       [Description("Função para filtrar, ex.: 'Lateral esquerdo': o primeiro da lista é o titular, os demais são reservas")] string? funcao = null) =>
                {
                    var e = await elenco.Handle(new ObterElenco(timeId, competicao, temporada, funcao), ct);
                    if (e is null) return (object)"Time sem atletas registrados nessa competição.";
                    if (funcao is null) return e;
                    // Por função: titular e reservas separados, para a resposta citar os reservas pelo nome
                    object Atleta(AtletaNoElenco a) => new { a.Nome, a.Idade, a.Jogos, a.Titular, TitularNaFuncao = a.TitularComo, a.Minutos, a.Gols, Nota = a.Nota?.ToString() ?? "sem nota: menos de 450 minutos" };
                    return new { e.Time, e.Competicao, e.Temporada, Funcao = funcao, Titular = e.Atletas.Take(1).Select(Atleta), Reservas = e.Atletas.Skip(1).Select(Atleta),
                        Observacao = "Reserva = quem já foi titular na função ou é do mesmo setor sem titularidade. Quem só entrou no segundo tempo nessa posição não aparece: o provedor não informa a posição de quem sai do banco." };
                },
                "elenco_do_time",
                "Todos os atletas que jogaram pelo time na competição, do mais usado ao menos usado, com função, jogos, titularidades, minutos e nota (quando tem). Use para reservas, quem joga pouco e o elenco completo."),
            AIFunctionFactory.Create(
                async ([Description("ID da competição; omita para todas")] int? competicao = null,
                       [Description("Parte do nome do time, para encontrar o ID")] string? nome = null,
                       int? temporada = null) =>
                {
                    // Buscando pelo nome, procura em todas as competições: a IA às vezes chuta a série errada ("Cruzeiro na Série B")
                    var r = await listarTimes.Handle(new ListarTimes(competicao, nome is null ? temporada : null, nome), ct);
                    // Nome com competição: procura nela; se não achar (a IA chutou a série errada), procura em todas
                    if (nome is not null && competicao is not null && r.Count == 0) r = await listarTimes.Handle(new ListarTimes(Nome: nome), ct);
                    if (nome is not null && temporada is not null && r.Any(t => t.Temporada == temporada)) r = r.Where(t => t.Temporada == temporada).ToList();
                    if (nome is not null) CitarTimes(r.Take(1)); // busca por nome: só o mais parecido vira cartão
                    return r.Select(t => new { t.Id, t.Nome, t.Competicao, t.Temporada, t.Posicao, t.Pontos, t.Jogos, t.EsquemaMaisUsado, t.Estilo, t.Carencias });
                },
                "listar_times",
                "Times de uma competição com classificação, esquema mais usado, estilo e carências; ou encontra o ID de um time pelo nome."),
            AIFunctionFactory.Create(
                async (int timeId, int? competicao = null, int? temporada = null) =>
                {
                    // Competição ou temporada erradas não podem esconder o time: tenta de novo sem elas
                    var d = await detalharTime.Handle(new ObterTime(timeId, competicao, temporada), ct) ?? await detalharTime.Handle(new ObterTime(timeId), ct);
                    if (d is null) return (object)"Time não encontrado.";
                    CitarTimes([d.Resumo]);
                    return (object)new
                    {
                        d.Resumo, Esquemas = Esquemas(d.Esquemas), d.Dimensoes, d.Indicadores, d.Estilo, d.Elenco,
                        Funcoes = d.Funcoes.Select(f => new { f.Funcao, f.Jogos, f.NotaTitulares, f.Percentil, f.Carencia, Titulares = f.Titulares.Select(a => $"{a.Nome} ({a.Jogos} jogos)") }),
                        Principais = d.Principais.Select(a => new { a.Id, a.Nome, a.Idade, a.Funcao, a.Nota }),
                        Treinadores = d.Treinadores.Select(t => new { t.Id, t.Nome, t.NoCargo, t.Jogos, AproveitamentoPercentual = t.Aproveitamento }),
                        d.Observacoes,
                    };
                },
                "analisar_time",
                "Análise de um time: esquemas (em casa e fora, com aproveitamento), estilo de jogo, percentis em posse, ataque, defesa e disciplina, perfil do elenco e como está servido em cada função (carências)."),
            AIFunctionFactory.Create(
                async (int timeId,
                       [Description("true só quando o usuário perguntar chance, probabilidade ou favoritismo de título")] bool chanceDeTitulo = false,
                       int? temporada = null) =>
                {
                    var c = await copasDoTime.Handle(new ObterCopasDoTime(timeId, temporada), ct);
                    if (c is null) return (object)"Time não encontrado.";
                    if (c.Copas.Count == 0) return (object)$"{c.Time} não tem jogos de Copa do Brasil, Libertadores ou Sul-Americana na base em {c.Temporada}.";
                    return (object)new
                    {
                        c.Time, c.Temporada, ChanceDeAlgumTituloPercentual = chanceDeTitulo ? c.ChanceDeAlgumTituloPercentual : null,
                        Copas = c.Copas.Select(x => new
                        {
                            x.Competicao, x.Situacao, x.Jogos, x.Vitorias, x.Empates, x.Derrotas, x.GolsPro, x.GolsContra,
                            Fases = x.Fases.Select(f => new
                            {
                                f.Fase, f.Resultado,
                                Jogos = f.Jogos.Select(j => j.GolsPro is null
                                    ? $"{j.Data.AddHours(-3):dd/MM} {(j.EmCasa ? "em casa" : "fora")} contra {j.Adversario} (a jogar)"
                                    : $"{j.Data.AddHours(-3):dd/MM} {(j.EmCasa ? "em casa" : "fora")} {j.GolsPro} x {j.GolsContra} {j.Adversario}{(j.Penaltis ? " (pênaltis)" : "")}"),
                            }),
                            ProximoJogo = x.ProximoJogo is { } p ? $"{p.Data.AddHours(-3):dd/MM HH:mm} {(p.EmCasa ? "em casa" : "fora")} contra {p.Adversario} ({p.Fase})" : null,
                            Chance = chanceDeTitulo && x.Chance is { } ch ? new
                            {
                                ch.TituloPercentual, ChegarAFinalPercentual = ch.FinalPercentual,
                                PassarDaFaseAtualPercentual = ch.PassarDaFasePercentual, ch.FaseAtual, ch.AdversarioDaFase,
                                Favoritos = ch.Favoritos.Select(f => $"{f.Time}: {f.TituloPercentual}%"), ch.Metodo,
                            } : null,
                        }),
                    };
                },
                "copas_do_time",
                "Campanha do time nas copas da temporada (Copa do Brasil, Libertadores, Sul-Americana): situação (vivo, eliminado, campeão), V/E/D, gols, resultados de cada fase, próximo jogo e a chance de título estimada pelo modelo AlpaFlow."),
            AIFunctionFactory.Create(
                async ([Description("Função em campo, ex.: 'Lateral esquerdo', 'Volante', 'Centroavante', 'Zagueiro'")] string funcao,
                       [Description("IDs de competição para restringir; omita para todas")] int[]? competicoes = null,
                       int? temporada = null, int? limite = null) =>
                {
                    var r = await carencias.Handle(new BuscarTimesComCarencia(funcao, temporada, competicoes, limite ?? 6), ct);
                    CitarTimes(r.Select(x => x.Time));
                    return r.Select(x => new { Time = x.Time.Nome, TimeId = x.Time.Id, x.Time.Competicao, x.Time.EsquemaMaisUsado, x.Necessidade });
                },
                "times_com_carencia",
                "Times com titulares mais fracos numa função (percentil baixo entre os times da competição): mercado para quem oferece atletas daquela posição."),
            AIFunctionFactory.Create(
                async (int atletaId, [Description("IDs de competição; omita para usar só a competição do atleta")] int[]? competicoes = null,
                       int? temporada = null, int? limite = null) =>
                {
                    var r = await timesParaAtleta.Handle(new TimesParaAtleta(atletaId, temporada, competicoes, limite ?? 5), ct);
                    if (r is null) return (object)"Atleta não encontrado, sem função definida pelas escalações, ou fora da análise.";
                    Citar([r.Atleta]);
                    CitarTimes(r.Times.Select(t => t.Time));
                    return (object)new
                    {
                        Atleta = new { r.Atleta.Nome, r.Atleta.Funcao, r.Atleta.Nota, r.Atleta.Clube, r.Atleta.Competicao },
                        Times = r.Times.Select(t => new { t.Time.Nome, t.Time.EsquemaMaisUsado, t.Funcao, t.NotaTitulares, t.NotaAtleta, t.Ganho, t.ExperienciaNoEsquema, Titulares = t.TitularesAtuais.Select(a => a.Nome) }),
                        r.Observacoes,
                    };
                },
                "times_para_atleta",
                "Em que times um atleta encaixaria: times que usam a função dele e têm ali titulares com nota menor."),
            AIFunctionFactory.Create(
                async ([Description("ID da competição; omita para todas")] int? competicao = null,
                       [Description("Parte do nome do treinador, para encontrar o ID")] string? nome = null,
                       [Description("'aproveitamento' (padrão) ou 'jovens' (quem mais escala atletas até 23 anos)")] string? ordenarPor = null,
                       int? temporada = null) =>
                {
                    var r = await listarTreinadores.Handle(new ListarTreinadores(competicao, nome, nome is null ? temporada : null), ct);
                    if (nome is not null && competicao is not null && r.Count == 0) r = await listarTreinadores.Handle(new ListarTreinadores(Nome: nome), ct);
                    if (nome is not null && temporada is not null && r.Any(t => t.Temporada == temporada)) r = r.Where(t => t.Temporada == temporada).ToList();
                    var jovens = string.Equals(ordenarPor, "jovens", StringComparison.OrdinalIgnoreCase);
                    var lista = (jovens
                        ? r.Where(t => t.TitularesAte23 is not null).OrderByDescending(t => t.TitularesAte23)
                        : r.OrderByDescending(t => t.Aproveitamento)).ToList();
                    if (nome is not null) CitarTreinadores(lista.Take(1));
                    else if (ordenarPor is not null) CitarTreinadores(lista.Take(5));
                    return lista.Select(t => new
                    {
                        t.Id, t.Nome, t.Clube, t.Competicao, t.Temporada, t.NoCargo, t.Jogos, AproveitamentoPercentual = t.Aproveitamento,
                        t.EsquemaPreferido, t.Estilo, PercentualDasTitularidadesParaAte23 = t.TitularesAte23, t.Dimensoes,
                    });
                },
                "listar_treinadores",
                "Treinadores no cargo (ou todos, buscando pelo nome), já ordenados, com aproveitamento, esquema preferido, estilo e a porcentagem das titularidades dadas a atletas até 23 anos."),
            AIFunctionFactory.Create(
                async (int treinadorId, int? temporada = null) =>
                {
                    var d = await detalharTreinador.Handle(new ObterTreinador(treinadorId, temporada), ct);
                    if (d is null) return (object)"Treinador não encontrado.";
                    CitarTreinadores([d.Resumo]);
                    return (object)new
                    {
                        Passagens = d.Passagens.Select(p => new
                        {
                            p.Passagem.Clube, p.Passagem.Competicao, p.Passagem.Temporada, p.Passagem.NoCargo, p.Passagem.Inicio, p.Passagem.Fim,
                            p.Passagem.Jogos, p.Passagem.Vitorias, p.Passagem.Empates, p.Passagem.Derrotas, p.Passagem.Aproveitamento,
                            Esquemas = Esquemas(p.Esquemas), p.Estilo, p.Passagem.Dimensoes, p.IdadeMediaTitulares, p.Passagem.TitularesAte23, p.PercentilJovens,
                            p.Comparacao, OutrosNoClube = p.NoMesmoClube,
                        }),
                        Carreira = d.Carreira?.Passagens.Take(8),
                        d.Observacoes,
                    };
                },
                "analisar_treinador",
                "Perfil de um treinador: passagens na base com resultados, esquemas, estilo do time sob o comando dele, uso de jovens, comparação com o antecessor e carreira."),
            AIFunctionFactory.Create(
                async (int treinadorId, int? limite = null) =>
                {
                    var r = await atletasDoTreinador.Handle(new AtletasDoTreinador(treinadorId, limite ?? 12), ct);
                    Citar(r.Take(6).Select(x => x.Atleta));
                    return r.Select(x => new { x.Atleta.Id, x.Atleta.Nome, x.Atleta.Idade, x.Atleta.Clube, x.Atleta.Temporada, x.JogosComEle, x.FuncaoComEle, x.Atleta.Nota });
                },
                "atletas_do_treinador",
                "Atletas que o treinador mais escalou como titular nas passagens da base, com a função em que jogaram com ele."),
            AIFunctionFactory.Create(
                async (int treinadorId,
                       [Description("Função para restringir, ex.: 'Volante', 'Lateral esquerdo'; omita para todas as funções do esquema dele")] string? funcao = null,
                       int? idadeMaxima = null, [Description("IDs de competição para as sugestões; omita para todas")] int[]? competicoes = null) =>
                {
                    var r = await atletasParaTreinador.Handle(new AtletasParaTreinador(treinadorId, funcao, idadeMaxima, competicoes), ct);
                    if (r is null) return (object)"Treinador não encontrado.";
                    Citar(r.Funcoes.SelectMany(f => f.Sugestoes.Take(2)).Take(8).Select(s => s.Atleta));
                    return (object)new
                    {
                        Treinador = new { r.Treinador.Nome, r.Treinador.Clube, r.Treinador.EsquemaPreferido },
                        Funcoes = r.Funcoes.Select(f => new
                        {
                            f.Funcao, f.Jogos, NotaDeQuemEleEscala = f.NotaTitulares, PercentilDoTimeNaFuncao = f.PercentilNaCompeticao, f.Perfil, f.QuemEleEscala,
                            Sugestoes = f.Sugestoes.Select(s => new { s.Atleta.Id, s.Atleta.Nome, s.Atleta.Idade, s.Atleta.Clube, s.Atleta.Competicao, s.Atleta.Nota, s.Semelhanca, s.Atleta.ExperienciaNoEsquema }),
                        }),
                        r.Observacoes,
                    };
                },
                "atletas_para_treinador",
                "Atletas que combinam com o estilo do treinador: para cada função do esquema dele, o perfil de quem ele costuma escalar e atletas de outros clubes parecidos com esse perfil (e no mesmo nível ou acima)."),
            AIFunctionFactory.Create(
                async ([Description("Nome do time (oficial ou apelido, ex.: 'Galo')")] string time,
                       [Description("Nome do adversário, se citado")] string? adversario = null,
                       [Description("Data inicial (aaaa-mm-dd)")] string? de = null,
                       [Description("Data final (aaaa-mm-dd)")] string? ate = null,
                       [Description("ID da competição, se citada (ex.: Libertadores)")] int? competicao = null) =>
                {
                    DateOnly? Data(string? s) => DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
                    var r = await procurarPartidas.Handle(new ProcurarPartidas(time, adversario, Data(de), Data(ate), CompeticaoId: competicao), ct);
                    return r.Select(p => new { p.Id, Data = p.Data.ToString("yyyy-MM-dd"), DiaDaSemana = p.Data.DayOfWeek.ToString(), p.Competicao, p.Rodada, p.Mandante, p.Visitante, p.GolsMandante, p.GolsVisitante, p.Status });
                },
                "procurar_partidas",
                "Partidas de um time (com o adversário e o período opcionais), da mais recente para a mais antiga, com o ID para analisar."),
            AIFunctionFactory.Create(
                async (int partidaId) =>
                {
                    var a = await analisarPartida.Handle(new AnalisarPartida(partidaId), ct);
                    if (a is null) return (object)"Partida não encontrada.";
                    partidasCitadas[a.Partida.Id] = a.Partida;
                    object Lado(LadoDaPartida l) => new
                    {
                        l.Time, l.Esquema, l.Treinador, l.TreinadorId,
                        Titulares = l.Titulares.Select(j => $"{j.Nome}{(j.Funcao is null ? "" : $" ({j.Funcao})")}"),
                        Reservas = l.Reservas.Select(j => j.Nome),
                    };
                    return (object)new
                    {
                        a.Partida, Mandante = Lado(a.Mandante), Visitante = Lado(a.Visitante),
                        Eventos = a.Eventos.Select(e => new
                        {
                            Minuto = e.Acrescimo is { } x ? $"{e.Minuto}+{x}" : $"{e.Minuto}",
                            Time = e.ClubeId == a.Partida.MandanteId ? a.Partida.Mandante : a.Partida.Visitante,
                            e.Tipo, e.Jogador, AssistenciaOuQuemEntrou = e.Outro, e.Detalhe,
                        }),
                        a.Desfecho, a.Aspectos, a.Observacoes,
                        // Melhores notas do jogo de cada time (nota nossa, 0 a 100)
                        Destaques = (a.Desempenhos ?? []).Where(d => d.Nota is not null).GroupBy(d => d.ClubeId)
                            .SelectMany(g => g.Take(3)).Select(d => new
                            {
                                d.Nome, Time = d.ClubeId == a.Partida.MandanteId ? a.Partida.Mandante : a.Partida.Visitante,
                                d.Funcao, d.Nota, d.Minutos,
                                Melhores = d.Indicadores.Where(i => i.DoSetor && i.Percentil >= 70).Select(i => i.Nome),
                            }),
                    };
                },
                "analisar_partida",
                "Análise de uma partida: placar, esquemas, treinadores, titulares e reservas, eventos (gols, cartões, substituições, VAR), em cada aspecto quem levou vantagem e como cada time foi em relação aos próprios jogos, e os destaques individuais pela nota do jogo."),
            AIFunctionFactory.Create(
                async ([Description("'batedores' (aproveitamento de quem cobra, padrão), 'goleiros' (pênaltis defendidos), 'sofridos' (quem mais cava) ou 'cometidos'")] string tipo = "batedores",
                       [Description("IDs de competição para restringir; omita para todas da temporada")] int[]? competicoes = null,
                       [Description("ID do time, para os pênaltis de um clube")] int? timeId = null,
                       [Description("ID do atleta, para os pênaltis de um atleta específico")] int? atletaId = null,
                       [Description("Somar as cobranças das disputas de pênaltis (padrão true)")] bool comDisputas = true,
                       [Description("Mínimo de cobranças para entrar no ranking de batedores (padrão 3)")] int? minimoDeCobrancas = null,
                       int? temporada = null, int? limite = null) =>
                {
                    var r = await penaltis.Handle(new ObterRankingPenaltis(tipo, temporada, competicoes, timeId, comDisputas,
                        minimoDeCobrancas ?? (atletaId is null && timeId is null ? 3 : 1), limite ?? 8, atletaId), ct);
                    return r;
                },
                "penaltis",
                "Pênaltis por atleta na temporada: ranking dos melhores batedores (convertidos de cobrados, no jogo e nas disputas), goleiros que mais defendem, quem mais sofre e quem mais comete pênalti; também os números de um time ou de um atleta."),
        };

        var mensagens = new List<ChatMessage> { new(ChatRole.System, Instrucoes(disponiveis, temporadaPadrao)) };
        mensagens.AddRange(conversa.TakeLast(12).Select(m => new ChatMessage(m.Papel == "user" ? ChatRole.User : ChatRole.Assistant, m.Texto)));

        var resposta = await chat.GetResponseAsync(mensagens, new ChatOptions { Tools = ferramentas, Temperature = 0.2f }, ct);
        return new RespostaChat(resposta.Text, citados.Values.ToList(), timesCitados.Values.ToList(), treinadoresCitados.Values.ToList(), partidasCitadas.Values.ToList());
    }

    /// <summary>Parágrafo de analista sobre o atleta, só com as nossas métricas. Guardado por 6 horas.</summary>
    public Task<string> ResumirAtleta(DetalheAtleta d, CancellationToken ct) =>
        cache.GetOrCreateAsync($"resumo-ia:{d.Resumo.Id}:{d.Resumo.Temporada}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            var dados = JsonSerializer.Serialize(new
            {
                d.Resumo, d.Funcoes, d.Esquemas, d.Atributos, d.Cartoes, d.Presenca, d.Estilo,
                Clubes = d.Trajetoria?.Clubes.Take(6).Select(c => new { c.Clube, c.Temporadas }),
            });
            var resposta = await chat.GetResponseAsync(
            [
                new(ChatRole.System, """
                    Você é analista de desempenho. Escreva um resumo de 4 a 5 frases, em português do Brasil, sobre o atleta, só com os dados recebidos.
                    Cubra: perfil e função em campo, incluindo o estilo (mais ofensivo, equilibrado ou mais marcador, sempre em relação ao próprio setor); regularidade (presença como titular); 2 ou 3 pontos fortes e 1 ou 2 pontos a desenvolver, em percentis;
                    disciplina (cartões); e, se houver, a trajetória recente de clubes.
                    Não cite estatísticas brutas (desarmes, duelos, passes). Não fale de saúde nem de lesões.
                    Jogo aéreo, saída de bola e intensidade são estimativas: sempre que citar um deles, escreva "(estimativa)" logo depois. Tom profissional, sem exageros.
                    """),
                new(ChatRole.User, dados),
            ], new ChatOptions { Temperature = 0.3f }, ct);
            return resposta.Text;
        })!;

    /// <summary>Nomes de campo sem ambiguidade: o aproveitamento é do esquema, não dos jogos em casa.</summary>
    static IEnumerable<object> Esquemas(IEnumerable<EsquemaDoTime> esquemas) =>
        esquemas.Select(e => new { e.Esquema, Jogos = e.Jogos, JogosEmCasa = e.EmCasa, JogosFora = e.Fora, AproveitamentoPercentualComEsseEsquema = e.Aproveitamento });

    /// <summary>"terça-feira, 13/10/2026" sem depender da cultura instalada no servidor.</summary>
    static string Hoje()
    {
        string[] dias = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];
        var hoje = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        return $"{dias[(int)hoje.DayOfWeek]}, {hoje:dd/MM/yyyy}";
    }

    static string ChavesAtributos => string.Join(", ", Catalogo.Atributos.Select(a => $"{a.Chave} ({a.Nome})"));

    static string Instrucoes(IReadOnlyList<CompeticaoDisponivel> disponiveis, int temporadaPadrao) => $"""
        Você é o analista de scouting da AlpaFlow Scout. Responde em português do Brasil, de forma direta, como um analista de desempenho experiente.

        Dados disponíveis (competição, temporada, atletas analisáveis): {JsonSerializer.Serialize(disponiveis)}
        Sem temporada informada, cada competição usa a temporada mais recente com dados (a Série C, por exemplo, é a de 2025). Diga sempre a temporada ao citar um atleta.
        Competições com "Copa": true (Copa do Brasil, Libertadores, Sul-Americana) entram em jogos, times e treinadores, mas NÃO na nota
        dos atletas: a nota e os percentis de atleta vêm da liga (Séries A, B e C). O desempenho do atleta nas copas aparece em
        "DesempenhoEmCopas" do detalhar_atleta (jogos, minutos, gols e, com minutos suficientes, nota na própria copa). Para analisar um
        time ou treinador numa copa, passe o ID da copa em "competicao"; sem isso, vale a liga.

        Regras:
        - Use SEMPRE as ferramentas para qualquer afirmação sobre atletas. Nunca invente nomes, notas ou números.
        - Transforme o pedido em busca: função ("zagueiro pela esquerda" = funcao "Zagueiro esquerdo"), idade, atributos e pesos.
          Atributos disponíveis: {ChavesAtributos}.
        - Apresente cada atleta com: nome, idade, clube, competição, função, minutos e a nota de encaixe (0–100),
          explicando os pontos fortes em percentis ("na saída de bola, melhor que 82% dos defensores da Série C").
        - NÃO liste estatísticas brutas (desarmes, duelos, passes por jogo). Fale só em notas, percentis e fatos simples (jogos, minutos, gols, idade).
        - Quando um atributo for aproximação (jogo aéreo, saída de bola, intensidade), avise em uma frase curta.
        - Só existem na base atletas com 18 anos ou mais. Se pedirem menores de idade, explique que a plataforma não analisa menores.
        - Para indicar contratação, use participacaoMinima 40 por padrão (quem quase não joga não é recomendação), salvo se o usuário pedir outra coisa.
        - Quando o usuário citar um esquema, passe-o em "esquema". O resultado traz experienciaNoEsquema (ex.: "23 de 27 jogos com linha de 3"): destaque quem já está adaptado ao esquema e cite isso na recomendação final.
        - Se a busca vier vazia, tente de novo afrouxando um critério (minutos, idade, lado) antes de responder que não há atletas.
        - Respostas curtas: no máximo 5 atletas, com 1 ou 2 linhas cada, e uma conclusão de uma frase.
        - Atleta citado pelo nome e pelo clube ("Fernando do Bragantino"): procurar_atleta_por_nome traz o clube de cada resultado;
          use só o que tem o clube citado. Se nenhum bate, diga isso. Nunca atribua a um atleta um clube que não está nos dados.
        - Atleta sem nota: detalhar_atleta devolve o "Motivo" (minutos na liga, sem data de nascimento, menor de 18). Diga exatamente esse
          motivo e mostre os fatos dos "Registros" (clube, jogos, minutos, gols). NUNCA suponha que alguém é menor de idade.
        - "Reserva", "quem joga pouco", "elenco": use elenco_do_time. Reserva de uma função = quem tem essa função (ou do mesmo setor)
          com menos titularidades que o titular, que é o de mais titularidades na função. Passe a função em "funcao" no elenco_do_time:
          o primeiro da lista é o titular e os demais são os reservas (se a lista tiver só o titular, diga que não há reserva com jogos na função).
          Cite os reservas pelo nome, com idade, jogos, titularidades e minutos, mesmo os que não têm nota (nesse caso, diga que ainda
          não jogaram o suficiente para a nota).

        Times:
        - Dimensões e indicadores de times e treinadores são PERCENTIS (posição de 0 a 100 entre os outros times), não porcentagens:
          "percentil 95 em titularidades para jovens" = escala mais jovens que 95% dos times, e não "95% das titularidades".
          Porcentagem real só nos campos com "Percentual" no nome.
        - Ao procurar um time ou treinador pelo nome, só passe competição se o usuário citou uma (ex.: "na Libertadores"): não suponha a série do time.
        - Para falar de um time, encontre o ID com listar_times (nome) e use analisar_time. Descreva: esquema mais usado (e se muda em casa e fora),
          estilo, pontos fortes e fracos em percentis entre os times da competição, perfil do elenco e as carências por função.
        - Nunca cite médias brutas de time (posse em %, finalizações ou passes por jogo). Fale em percentis ("fica com a bola mais que 80% dos times da Série B")
          e em fatos públicos (classificação, pontos, vitórias, gols, esquemas).
        - "Quem precisa de um lateral esquerdo?" = times_com_carencia. "Onde esse atleta encaixa?" = times_para_atleta, explicando o ganho
          (nota do atleta menos a nota dos titulares atuais) e se ele já joga no esquema do time.
        - Carência é relativa: titulares abaixo da maioria dos times da mesma competição naquela função. Diga isso ao usar a palavra.
        - "Como o time está nas copas", campanha, fase, próximo jogo ou chance de ser campeão = copas_do_time (já traz as três copas;
          não precisa do ID da competição). Para o estilo do time numa copa em percentis, use analisar_time com o ID da copa
          (73 Copa do Brasil, 13 Libertadores, 11 Sul-Americana).
        - Na campanha nas copas, fale de fase, resultados (adversários e placares), V/E/D, gols e próximo jogo. Só fale de chance de título
          quando perguntarem por chance, probabilidade, favoritismo ou quem vai ganhar.
        - Chance de título: para CADA copa em que o time está vivo, dê o número em porcentagem como estimativa do modelo AlpaFlow, explique em uma frase como é calculada
          (força pelos resultados da temporada e o restante da copa simulado 10 mil vezes), cite o adversário da fase atual,
          a chance de chegar à final e os favoritos. Diga que é probabilidade, não certeza. Não invente número fora do que a ferramenta trouxe.
          "Chance de ganhar alguma/uma das copas" = ChanceDeAlgumTituloPercentual (já calculado): nunca some nem multiplique porcentagens você mesmo.

        Treinadores:
        - Encontre o ID com listar_treinadores (nome) e use analisar_treinador. Descreva: clube e período, campanha (V/E/D e aproveitamento),
          esquema preferido e se varia, estilo do time sob o comando dele em percentis, uso de jovens (% das titularidades para até 23 anos)
          e, se houver, a comparação com o antecessor no clube.
        - Os percentis comparam só os jogos em que cada treinador comandou, com os outros treinadores da mesma competição.
          Passagens com menos de 5 jogos não têm percentil: diga que a amostra é pequena.
        - Nomes de times e treinadores no provedor vêm sem acento e às vezes com sobrenome primeiro ("Atletico-MG", "Dominguez Eduardo").
          A busca por nome já tolera isso: passe só o nome principal ("Atletico MG", "Dominguez"), sem apelidos. Se não achar,
          tente o sobrenome sozinho ou procure pelo time (analisar_time traz os treinadores) antes de dizer que não existe.
        - Para rankings, use ordenarPor ("jovens" ou "aproveitamento") e mantenha a ordem recebida. Uso de jovens é porcentagem:
          "55% das titularidades para atletas até 23 anos", nunca "55 jovens".
        - Não opine sobre a pessoa nem sobre a vida pessoal: fale do trabalho em campo, com dados.
        - "Atletas que trabalharam com ele" = atletas_do_treinador. "Que atletas combinam com ele" = atletas_para_treinador: explique o perfil
          que ele escala em cada função (atributos em percentil) e por que cada sugestão se parece com esse perfil (semelhança 0–100).
        - A base só tem o esquema inicial de cada jogo. Se perguntarem pelo esquema com e sem a bola, explique que esse dado não existe
          no provedor atual (exige dados de posicionamento, como Wyscout ou StatsBomb) e mostre os esquemas iniciais que ele usa.

        Pênaltis:
        - "Melhor batedor", "quem mais converte pênalti", goleiro que mais defende, quem mais sofre ou comete pênalti, ou os pênaltis de um time ou atleta = ferramenta penaltis.
        - São fatos públicos, como gols: diga "converteu 7 de 8 (88%)", separando "no tempo de jogo" e "nas disputas de pênaltis" quando houver os dois.
        - Explique em meia frase o critério do ranking (mínimo de cobranças, desempate por convertidos) e mantenha a ordem recebida. Diga a temporada e as competições consideradas.
        - Mesma "Posicao" = empate: diga "empatados" e nunca invente desempate. Defesas de goleiro são só do tempo de jogo (não diga que incluem disputas).
        - Pênaltis de um time: cite todos os batedores que vieram, com quantos converteu e cobrou no jogo e na disputa.

        Partidas:
        - Hoje é {Hoje()}. Converta datas relativas ("último sábado", "domingo passado") em datas e use procurar_partidas
          com o time, o adversário e um período de 3 dias antes a 3 dias depois da data (as pessoas erram o dia: "sábado" pode ter sido domingo);
          depois analisar_partida com o ID do jogo mais próximo. Se a data real for outra, diga ("o jogo foi no domingo, 11/10").
          Sem data na pergunta ("o último jogo", "o jogo contra o Santos"), procure SEM datas e analise o mais recente da lista.
          Com data e sem jogo no período, procure de novo sem datas e analise o mais recente, sem perguntar antes.
        - Na análise: placar e contexto (competição, rodada), esquemas e treinadores, gols e lances (minuto, autor, assistência), cartões,
          substituições e quem levou vantagem em cada aspecto. Compare com o normal de cada time pelos percentis ("finalizou mais que em
          85% dos próprios jogos"). Não cite números brutos do provedor (posse em %, total de finalizações ou de passes).
        - Atletas menores de 18 anos aparecem como "Atleta sub-18": não tente identificá-los.
        """;
}
