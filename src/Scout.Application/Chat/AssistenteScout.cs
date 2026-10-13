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
public record RespostaChat(string Texto, IReadOnlyList<ResumoAtleta> Atletas, IReadOnlyList<ResumoTime> Times);

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
                    if (d is null) return (object)"Atleta não encontrado ou fora da análise (menor de idade ou poucos minutos).";
                    Citar([d.Resumo]);
                    // Sem a linha do tempo jogo a jogo: o resumo da presença basta para a conversa
                    return (object)new { d.Resumo, d.Funcoes, d.Esquemas, d.Atributos, d.Cartoes, d.Presenca, d.Estilo };
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
                async ([Description("Parte do nome do atleta")] string nome) =>
                    await db.Atletas.AsNoTracking()
                        .Where(a => a.Nome.ToLower().Contains(nome.ToLower()) || (a.NomeCompleto ?? "").ToLower().Contains(nome.ToLower()))
                        .OrderBy(a => a.Nome).Take(8)
                        .Select(a => new { a.Id, a.Nome, a.NomeCompleto })
                        .ToListAsync(ct),
                "procurar_atleta_por_nome",
                "Encontra o ID de um atleta pelo nome, para usar nas outras ferramentas."),
            AIFunctionFactory.Create(
                async ([Description("ID da competição; omita para todas")] int? competicao = null,
                       [Description("Parte do nome do time, para encontrar o ID")] string? nome = null,
                       int? temporada = null) =>
                {
                    var r = await listarTimes.Handle(new ListarTimes(competicao, temporada, nome), ct);
                    // A lista inteira só vira cartão quando é uma busca por nome (poucos times)
                    if (nome is not null) CitarTimes(r.Take(4));
                    return r.Select(t => new { t.Id, t.Nome, t.Competicao, t.Temporada, t.Posicao, t.Pontos, t.Jogos, t.EsquemaMaisUsado, t.Estilo, t.Carencias });
                },
                "listar_times",
                "Times de uma competição com classificação, esquema mais usado, estilo e carências; ou encontra o ID de um time pelo nome."),
            AIFunctionFactory.Create(
                async (int timeId, int? competicao = null, int? temporada = null) =>
                {
                    var d = await detalharTime.Handle(new ObterTime(timeId, competicao, temporada), ct);
                    if (d is null) return (object)"Time não encontrado.";
                    CitarTimes([d.Resumo]);
                    return (object)new
                    {
                        d.Resumo, d.Esquemas, d.Dimensoes, d.Indicadores, d.Estilo, d.Elenco,
                        Funcoes = d.Funcoes.Select(f => new { f.Funcao, f.Jogos, f.NotaTitulares, f.Percentil, f.Carencia, Titulares = f.Titulares.Select(a => $"{a.Nome} ({a.Jogos} jogos)") }),
                        Principais = d.Principais.Select(a => new { a.Id, a.Nome, a.Idade, a.Funcao, a.Nota }),
                        d.Observacoes,
                    };
                },
                "analisar_time",
                "Análise de um time: esquemas (em casa e fora, com aproveitamento), estilo de jogo, percentis em posse, ataque, defesa e disciplina, perfil do elenco e como está servido em cada função (carências)."),
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
        };

        var mensagens = new List<ChatMessage> { new(ChatRole.System, Instrucoes(disponiveis, temporadaPadrao)) };
        mensagens.AddRange(conversa.TakeLast(12).Select(m => new ChatMessage(m.Papel == "user" ? ChatRole.User : ChatRole.Assistant, m.Texto)));

        var resposta = await chat.GetResponseAsync(mensagens, new ChatOptions { Tools = ferramentas, Temperature = 0.2f }, ct);
        return new RespostaChat(resposta.Text, citados.Values.ToList(), timesCitados.Values.ToList());
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

    static string ChavesAtributos => string.Join(", ", Catalogo.Atributos.Select(a => $"{a.Chave} ({a.Nome})"));

    static string Instrucoes(IReadOnlyList<CompeticaoDisponivel> disponiveis, int temporadaPadrao) => $"""
        Você é o analista de scouting da AlpaFlow Scout. Responde em português do Brasil, de forma direta, como um analista de desempenho experiente.

        Dados disponíveis (competição, temporada, atletas analisáveis): {JsonSerializer.Serialize(disponiveis)}
        Sem temporada informada, cada competição usa a temporada mais recente com dados (a Série C, por exemplo, é a de 2025). Diga sempre a temporada ao citar um atleta.

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

        Times:
        - Para falar de um time, encontre o ID com listar_times (nome) e use analisar_time. Descreva: esquema mais usado (e se muda em casa e fora),
          estilo, pontos fortes e fracos em percentis entre os times da competição, perfil do elenco e as carências por função.
        - Nunca cite médias brutas de time (posse em %, finalizações ou passes por jogo). Fale em percentis ("fica com a bola mais que 80% dos times da Série B")
          e em fatos públicos (classificação, pontos, vitórias, gols, esquemas).
        - "Quem precisa de um lateral esquerdo?" = times_com_carencia. "Onde esse atleta encaixa?" = times_para_atleta, explicando o ganho
          (nota do atleta menos a nota dos titulares atuais) e se ele já joga no esquema do time.
        - Carência é relativa: titulares abaixo da maioria dos times da mesma competição naquela função. Diga isso ao usar a palavra.
        """;
}
