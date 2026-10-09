namespace Scout.Domain;

public enum Setor { Goleiro, Defesa, Meio, Ataque }

public enum Lado { Centro, Esquerda, Direita }

public record PosicaoTatica(Setor Setor, Lado Lado, string Funcao);

/// <summary>
/// Traduz o esquema ("3-4-3") e a grade do provedor ("linha:coluna") na função do atleta em campo.
/// A linha 1 é o goleiro, a 2 é a defesa e a última é o ataque.
/// </summary>
public static class FuncaoTatica
{
    /// <summary>
    /// Na grade do provedor a coluna 1 é o lado esquerdo do time (confirmado com laterais-esquerdos conhecidos).
    /// </summary>
    public const bool ColunaUmEhEsquerda = true;

    public static PosicaoTatica? Identificar(string? esquema, string? grade)
    {
        if (string.IsNullOrWhiteSpace(esquema) || string.IsNullOrWhiteSpace(grade)) return null;
        var partes = grade.Split(':');
        if (partes.Length != 2 || !int.TryParse(partes[0], out var linha) || !int.TryParse(partes[1], out var coluna)) return null;
        if (linha == 1) return new(Setor.Goleiro, Lado.Centro, "Goleiro");

        var linhas = esquema.Split('-').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        if (linhas.Length < 2 || linhas.Any(n => n <= 0) || linha - 2 >= linhas.Length) return null;

        var naLinha = linhas[linha - 2];
        if (coluna < 1 || coluna > naLinha) return null;
        var lado = LadoDa(coluna, naLinha);
        var defesaComTres = linhas[0] == 3;

        if (linha == 2) return new(Setor.Defesa, lado, FuncaoNaDefesa(naLinha, coluna, lado));
        if (linha - 2 == linhas.Length - 1) return new(Setor.Ataque, lado, FuncaoNoAtaque(naLinha, lado));

        var linhasDeMeio = linhas.Length - 2;
        var primeiraDoMeio = linha == 3;
        return new(Setor.Meio, lado, FuncaoNoMeio(naLinha, coluna, lado, linhasDeMeio, primeiraDoMeio, defesaComTres));
    }

    static Lado LadoDa(int coluna, int naLinha)
    {
        if (naLinha == 1) return Lado.Centro;
        var esquerda = ColunaUmEhEsquerda ? 1 : naLinha;
        var direita = ColunaUmEhEsquerda ? naLinha : 1;
        if (coluna == esquerda) return Lado.Esquerda;
        if (coluna == direita) return Lado.Direita;
        // Linhas com 4: as duas do meio puxam para o lado em que estão
        if (naLinha == 4) return (coluna == 2) == ColunaUmEhEsquerda ? Lado.Esquerda : Lado.Direita;
        return Lado.Centro;
    }

    static bool NaPonta(int coluna, int naLinha) => coluna == 1 || coluna == naLinha;

    static string FuncaoNaDefesa(int naLinha, int coluna, Lado lado) => naLinha switch
    {
        3 => lado == Lado.Centro ? "Zagueiro central" : $"Zagueiro {Nome(lado)}",
        4 => NaPonta(coluna, 4) ? $"Lateral {Nome(lado)}" : $"Zagueiro {Nome(lado)}",
        5 => NaPonta(coluna, 5) ? $"Ala {Nome(lado)}" : lado == Lado.Centro ? "Zagueiro central" : $"Zagueiro {Nome(lado)}",
        _ => lado == Lado.Centro ? "Zagueiro" : $"Zagueiro {Nome(lado)}",
    };

    static string FuncaoNoAtaque(int naLinha, Lado lado) => naLinha switch
    {
        1 => "Centroavante",
        2 => $"Atacante {Nome(lado)}",
        _ => lado == Lado.Centro ? "Centroavante" : $"Ponta {NomeFeminino(lado)}",
    };

    static string FuncaoNoMeio(int naLinha, int coluna, Lado lado, int linhasDeMeio, bool primeiraDoMeio, bool defesaComTres)
    {
        // Linha de meio larga com defesa de 3 (3-4-3, 3-5-2): as pontas são alas
        if (defesaComTres && naLinha >= 4 && NaPonta(coluna, naLinha)) return $"Ala {Nome(lado)}";
        if (linhasDeMeio >= 2 && primeiraDoMeio && naLinha <= 2) return "Volante";
        if (linhasDeMeio == 1 && naLinha <= 2) return "Volante";
        if (naLinha >= 4 && NaPonta(coluna, naLinha)) return $"Meia {Nome(lado)}";
        if (!primeiraDoMeio && naLinha == 3) return lado == Lado.Centro ? "Meia central" : $"Meia {Nome(lado)}";
        return lado == Lado.Centro ? "Meio-campista" : $"Meio-campista pela {NomeFeminino(lado)}";
    }

    /// <summary>
    /// Ponto no campo (0–100) para desenhar: X vai da esquerda (0) para a direita (100) do time;
    /// Y vai do próprio gol (0) ao gol adversário (100). É a posição no desenho tático, não o deslocamento em jogo.
    /// </summary>
    public static (double X, double Y)? Coordenadas(string? esquema, string? grade)
    {
        if (Identificar(esquema, grade) is null) return null;
        var partes = grade!.Split(':');
        int linha = int.Parse(partes[0]), coluna = int.Parse(partes[1]);
        if (linha == 1) return (50, 5);

        var linhas = esquema!.Split('-').Select(int.Parse).ToArray();
        var naLinha = linhas[linha - 2];
        var x = 100.0 * (ColunaUmEhEsquerda ? coluna : naLinha - coluna + 1) / (naLinha + 1);
        var y = linhas.Length == 1 ? 50 : 18 + (linha - 2) * (66.0 / (linhas.Length - 1));
        return (Math.Round(x, 1), Math.Round(y, 1));
    }

    static string Nome(Lado lado) => lado == Lado.Esquerda ? "esquerdo" : lado == Lado.Direita ? "direito" : "central";
    static string NomeFeminino(Lado lado) => lado == Lado.Esquerda ? "esquerda" : lado == Lado.Direita ? "direita" : "central";
}
