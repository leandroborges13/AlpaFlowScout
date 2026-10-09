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

/// <summary>Resposta do chat: o texto e os atletas citados, para a tela mostrar os cartões.</summary>
public record RespostaChat(string Texto, IReadOnlyList<ResumoAtleta> Atletas);

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
    IMemoryCache cache,
    ILogger<AssistenteScout> log)
{
    public async Task<RespostaChat> Responder(IReadOnlyList<MensagemChat> conversa, CancellationToken ct)
    {
        var disponiveis = await competicoes.Handle(new ListarCompeticoes(), ct);
        var temporadaPadrao = disponiveis.Select(c => c.Temporada).DefaultIfEmpty(DateTime.Today.Year).Max();
        var citados = new Dictionary<int, ResumoAtleta>();
        void Citar(IEnumerable<ResumoAtleta> atletas) { foreach (var a in atletas) citados[a.Id] = a; }

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
        };

        var mensagens = new List<ChatMessage> { new(ChatRole.System, Instrucoes(disponiveis, temporadaPadrao)) };
        mensagens.AddRange(conversa.TakeLast(12).Select(m => new ChatMessage(m.Papel == "user" ? ChatRole.User : ChatRole.Assistant, m.Texto)));

        var resposta = await chat.GetResponseAsync(mensagens, new ChatOptions { Tools = ferramentas, Temperature = 0.2f }, ct);
        return new RespostaChat(resposta.Text, citados.Values.ToList());
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
        """;
}
