using Scout.Application.Analise;
using Scout.Domain;

namespace Scout.Application.Consultas;

/// <summary>Um ponto forte ou fraco explicado: "Jogo aéreo: melhor que 85% dos defensores".</summary>
public record Destaque(string Nome, double Percentil, string? Observacao = null);

/// <summary>
/// O que a plataforma mostra de um atleta: fatos públicos (jogos, minutos, gols) e as nossas métricas
/// (notas e percentis). Nunca as estatísticas brutas do provedor, que não podemos republicar.
/// </summary>
public record ResumoAtleta(
    int Id, string Nome, int Idade, string? Nacionalidade, int? AlturaCm, string? Foto,
    string Clube, string Competicao, int Temporada,
    string Setor, string? Funcao, string? Lado, string? EsquemaMaisComum,
    int Jogos, int Titular, int Minutos, int Gols, int Assistencias, int ParticipacaoMinutos,
    double? Nota, double? NotaEncaixe, IReadOnlyList<Destaque> Destaques,
    string? ExperienciaNoEsquema = null,
    // Atleta de liga de fora: a nota convertida para a escala da Série A pela força da liga (ForcaDasLigas). Nulo para liga brasileira.
    double? NotaSerieA = null,
    // A liga tem pouco confronto direto com a Série A: a nota convertida é ordem de grandeza (a tela mostra "≈")
    bool NotaSerieAEstimada = false,
    // Saiu do clube ou não joga há mais de 60 dias (ElencoAtual): a frase pronta. Nulo = segue no clube (ou não se sabe).
    string? Situacao = null,
    // Valor de mercado em euros e fim de contrato (Bzzoiro); nulos quando a fonte não tem
    long? ValorMercado = null,
    DateOnly? ContratoAte = null);

public static class Resumir
{
    public static string NomeSetor(Setor s) => s switch
    {
        Setor.Goleiro => "Goleiro", Setor.Defesa => "Defesa", Setor.Meio => "Meio-campo", _ => "Ataque",
    };

    /// <summary>Nota (0–100) de um atributo = média dos percentis dos indicadores dele.</summary>
    public static double? NotaDoAtributo(LinhaAnalise l, Atributo a) =>
        MotorDeAnalise.Media(a.Indicadores.Select(k => l.Percentis.GetValueOrDefault(k)));

    /// <summary>"14 de 27 jogos com linha de 3": quantas vezes foi titular num esquema com a mesma linha de defesa do pedido.</summary>
    public static string? ExperienciaNoEsquema(LinhaAnalise l, string? esquema)
    {
        if (string.IsNullOrWhiteSpace(esquema) || l.Esquemas.Count == 0) return null;
        var linha = esquema.Split('-')[0];
        var noEsquema = l.Esquemas.Where(e => e.Key.Split('-')[0] == linha).Sum(e => e.Value);
        return $"{noEsquema} de {l.Esquemas.Values.Sum()} jogos com linha de {linha}";
    }

    public static ResumoAtleta De(LinhaAnalise l, double? notaEncaixe = null, IEnumerable<Atributo>? atributos = null, string? esquema = null,
        double? notaSerieA = null, bool notaSerieAEstimada = false)
    {
        // Destaques: os atributos pedidos na busca, ou os 3 melhores atributos do atleta
        var lista = (atributos ?? Catalogo.Atributos.Where(a => a.Chave != "goleiro" || l.Setor == Setor.Goleiro))
            .Select(a => (a, nota: NotaDoAtributo(l, a)))
            .Where(x => x.nota is not null)
            .ToList();
        if (atributos is null) lista = lista.OrderByDescending(x => x.nota).Take(3).ToList();

        return new ResumoAtleta(
            l.AtletaId, l.Nome, l.Idade, l.Nacionalidade, l.AlturaCm, l.Foto,
            l.Clube, l.Competicao, l.Temporada,
            NomeSetor(l.Setor), l.Funcao, l.Lado?.ToString(), l.EsquemaMaisComum,
            l.Jogos, l.Titular, l.Minutos, l.Gols, l.Assistencias, (int)Math.Round(l.ParticipacaoMinutos * 100),
            Arredondar(l.Nota), Arredondar(notaEncaixe),
            lista.Select(x => new Destaque(x.a.Nome, Math.Round(x.nota!.Value), x.a.Observacao)).ToList(),
            ExperienciaNoEsquema(l, esquema), Arredondar(notaSerieA), notaSerieA is not null && notaSerieAEstimada)
            { ValorMercado = l.ValorMercado, ContratoAte = l.ContratoAte };
    }

    static double? Arredondar(double? v) => v is null ? null : Math.Round(v.Value, 1);
}
