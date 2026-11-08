using System.Globalization;
using System.Text;

namespace Scout.Application.Consultas;

/// <summary>
/// Liga o nome de um atleta em outra fonte ("Tomás Cuello", "Ángelo Preciado") ao nosso cadastro ("T. Cuello", "A. Preciado"),
/// entre os candidatos do mesmo clube. Na dúvida (dois candidatos com a mesma força), não liga: melhor sem dado que dado trocado.
/// </summary>
public static class LigarNomes
{
    static readonly HashSet<string> SemPeso = ["de", "da", "do", "dos", "das", "e", "jr", "junior", "filho", "neto", "sobrinho"];

    public static string[] Tokens(string? nome)
    {
        var s = new StringBuilder();
        foreach (var c in (nome ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            s.Append(char.IsLetter(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return s.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !SemPeso.Contains(t)).ToArray();
    }

    /// <summary>Força da ligação: 3 nome igual; 2 mesmo sobrenome e inicial compatível; 1 um nome contido no outro (ou no completo); 0 nada.</summary>
    public static int Forca(string nomeFora, string nosso, string? completo)
    {
        var p = Tokens(nomeFora);
        var n = Tokens(nosso);
        if (p.Length == 0 || n.Length == 0) return 0;
        if (p.SequenceEqual(n)) return 3;
        var nCheios = n.Where(t => t.Length > 1).ToArray();
        if (nCheios.Length == 0) return 0;
        // "T. Cuello" x "Tomás Cuello"; "Gustavo Scarpa" x "Scarpa"
        if (n[^1] == p[^1] && n[^1].Length >= 3 && (n.Length == 1 || p.Length == 1 || n[0] == p[0] || (n[0].Length == 1 && p[0].StartsWith(n[0]))))
            return 2;
        var c = Tokens(completo).ToHashSet();
        if (nCheios.All(p.Contains) && nCheios.Any(t => t.Length >= 4)) return 1;
        if (p.Length >= 1 && p.All(t => c.Contains(t) || n.Contains(t)) && p.Any(t => t.Length >= 4)) return 1;
        return 0;
    }

    /// <summary>O candidato mais forte, se for único; nulo se ninguém serve ou se há empate entre atletas diferentes.</summary>
    public static int? Ligar(string nomeFora, IEnumerable<(int Id, string Nome, string? Completo)> candidatos)
    {
        var pontos = candidatos.Select(c => (c.Id, Forca: Forca(nomeFora, c.Nome, c.Completo))).Where(x => x.Forca > 0).ToList();
        if (pontos.Count == 0) return null;
        var melhor = pontos.Max(x => x.Forca);
        var ids = pontos.Where(x => x.Forca == melhor).Select(x => x.Id).Distinct().ToList();
        return ids.Count == 1 ? ids[0] : null;
    }
}
