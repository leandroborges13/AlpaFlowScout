using System.Globalization;
using System.Text;

namespace Scout.Application.Consultas;

/// <summary>
/// Busca tolerante por nome: "Atlético MG" acha "Atletico-MG" e "Eduardo Domingues" acha "Eduardo Domínguez".
/// Ignora acento, maiúscula, pontuação e palavras curtas ("o", "de"); aceita uma letra de diferença em palavras longas.
/// </summary>
public static class BuscaPorNome
{
    static readonly HashSet<string> Ignoradas = ["de", "da", "do", "das", "dos", "e", "o", "a", "fc", "ec", "sc", "clube", "time", "tecnico", "treinador"];

    /// <summary>Quantas palavras da busca aparecem no nome (0 = não combina).</summary>
    public static int Pontuar(string busca, string nome)
    {
        var termos = Palavras(busca);
        var palavras = Palavras(nome);
        if (termos.Count == 0 || palavras.Count == 0) return 0;
        var acertos = termos.Where(t => palavras.Any(p => Combina(t, p))).ToList();
        // Só uma palavra curta em comum ("mg", "sp") não basta
        return acertos.Count == 0 || (acertos.All(a => a.Length < 4) && acertos.Count < termos.Count) ? 0 : acertos.Count;
    }

    /// <summary>Os itens que combinam com a busca, do mais parecido para o menos.</summary>
    public static IEnumerable<T> Filtrar<T>(IEnumerable<T> itens, string busca, Func<T, string> nome) =>
        itens.Select(i => (i, pontos: Pontuar(busca, nome(i))))
            .Where(x => x.pontos > 0)
            .OrderByDescending(x => x.pontos)
            .Select(x => x.i);

    static bool Combina(string termo, string palavra) =>
        termo == palavra
        || (termo.Length >= 3 && palavra.StartsWith(termo))
        || (palavra.Length >= 3 && termo.StartsWith(palavra))
        || (termo.Length >= 5 && palavra.Length >= 5 && Distancia(termo, palavra) <= 1);

    public static List<string> Palavras(string texto)
    {
        var semAcento = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                semAcento.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return semAcento.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Length >= 2 && !Ignoradas.Contains(p))
            .Distinct()
            .ToList();
    }

    /// <summary>Distância de edição (Levenshtein).</summary>
    static int Distancia(string a, string b)
    {
        var anterior = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var atual = new int[b.Length + 1];
            atual[0] = i;
            for (var j = 1; j <= b.Length; j++)
                atual[j] = Math.Min(Math.Min(atual[j - 1] + 1, anterior[j] + 1), anterior[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            anterior = atual;
        }
        return anterior[b.Length];
    }
}
