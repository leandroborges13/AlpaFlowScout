using SkiaSharp;
using Scout.Application.Consultas;

namespace Scout.Api;

/// <summary>
/// A imagem da prévia do link /jogo/&lt;id&gt; (1200x630, og:image) no visual do Galo na Lupa: antes do jogo, as três barras do
/// palpite do modelo; depois, o placar, os gols e se o modelo acertou. Desenhada no servidor (WhatsApp e X não rodam o site).
/// </summary>
public static class ImagemDoLink
{
    const int L = 1200, A = 630, Galo = 1062;
    static readonly SKColor Fundo = new(0x0a, 0x0a, 0x0a), Listra = new(0x13, 0x13, 0x13), Painel = new(0x16, 0x16, 0x16),
        Ouro = new(0xf5, 0xc4, 0x00), Branco = new(0xf2, 0xf2, 0xf2), Cinza = new(0xbd, 0xbd, 0xbd), Linha = new(0x2a, 0x2a, 0x2a),
        Rival = new(0x6b, 0x6b, 0x6b), Verde = new(0x1d, 0xb9, 0x54), Vermelho = new(0xf0, 0x56, 0x3a);

    // Inter (a fonte do site): no servidor vem do pacote fonts-inter do Debian; no Windows de desenvolvimento, a do sistema
    static readonly Lazy<SKTypeface> Negrito = new(() => Fonte("Black", "ExtraBold", "Bold") ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default);
    static readonly Lazy<SKTypeface> Normal = new(() => Fonte("SemiBold", "Medium", "Regular") ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal) ?? SKTypeface.Default);

    static SKTypeface? Fonte(params string[] pesos)
    {
        foreach (var pasta in new[] { "/usr/share/fonts", "/usr/local/share/fonts" }.Where(Directory.Exists))
        {
            var arquivos = Directory.EnumerateFiles(pasta, "Inter*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var peso in pesos)
                if (arquivos.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals($"Inter-{peso}", StringComparison.OrdinalIgnoreCase)) is { } f)
                    return SKTypeface.FromFile(f);
        }
        return null;
    }

    public static byte[] Desenhar(PaginaPublica pg)
    {
        var p = pg.Partida;
        string N(int id) => id == Galo ? "Galo" : id == p.MandanteId ? p.Mandante : p.Visitante;
        using var bmp = new SKBitmap(L, A);
        using var c = new SKCanvas(bmp);
        c.Clear(Fundo);
        using (var listra = new SKPaint { Color = Listra })
            for (var x = 0; x < L; x += 140) c.DrawRect(x, 0, 70, A, listra);

        // marca no topo: lupa + "Galo na Lupa"
        Lupa(c, 60, 44, 56);
        Texto(c, "Galo na ", 130, 86, 34, Branco, Negrito.Value);
        Texto(c, "Lupa", 130 + Largura("Galo na ", 34, Negrito.Value), 86, 34, Ouro, Negrito.Value);
        var selo = $"{(p.Competicao == "Serie A" ? "Série A" : p.Competicao)}{(p.Rodada?.StartsWith("Regular Season - ") == true ? $" · {p.Rodada[17..]}ª rodada" : "")}";
        Texto(c, selo, L - 60, 86, 26, Cinza, Normal.Value, SKTextAlign.Right);

        if (p.GolsMandante is null) Antes(c, pg, N);
        else Depois(c, pg, N);

        Texto(c, "@galonalupa · dados: AlpaFlow Scout", L / 2f, A - 34, 24, Cinza, Normal.Value, SKTextAlign.Center);
        using var img = SKImage.FromBitmap(bmp);
        using var png = img.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray();
    }

    static void Antes(SKCanvas c, PaginaPublica pg, Func<int, string> N)
    {
        var p = pg.Partida;
        Texto(c, $"{N(p.MandanteId)} x {N(p.VisitanteId)}", L / 2f, 200, Encaixar($"{N(p.MandanteId)} x {N(p.VisitanteId)}", 72, L - 120), Branco, Negrito.Value, SKTextAlign.Center);
        if (pg.Palpite is not { } x)
        {
            Texto(c, "O palpite do modelo sai perto do jogo", L / 2f, 300, 34, Cinza, Normal.Value, SKTextAlign.Center);
            return;
        }
        Texto(c, "O que diz o modelo", L / 2f, 262, 30, Ouro, Negrito.Value, SKTextAlign.Center);
        var faixas = new[] { (N(p.MandanteId), x.ChanceMandante, x.Favorito == "mandante"), ("Empate", x.ChanceEmpate, x.Favorito == "empate"),
            (N(p.VisitanteId), x.ChanceVisitante, x.Favorito == "visitante") };
        float y = 300, x0 = 110, w = L - 220;
        foreach (var (nome, v, fav) in faixas)
        {
            Texto(c, nome, x0, y + 30, 30, fav ? Branco : Cinza, Negrito.Value);
            Texto(c, $"{Math.Round(v)}%", x0 + w, y + 30, 38, fav ? Ouro : Cinza, Negrito.Value, SKTextAlign.Right);
            Barra(c, x0, y + 44, w, 18, 100, Linha);
            Barra(c, x0, y + 44, w, 18, v, fav ? Ouro : Rival);
            y += 78;
        }
        Texto(c, $"Placar mais provável: {x.PlacarProvavel}", L / 2f, y + 26, 30, Branco, Normal.Value, SKTextAlign.Center);
    }

    static void Depois(SKCanvas c, PaginaPublica pg, Func<int, string> N)
    {
        var p = pg.Partida;
        // placar grande no meio e os nomes dos lados
        Texto(c, $"{p.GolsMandante} x {p.GolsVisitante}", L / 2f, 290, 140, Branco, Negrito.Value, SKTextAlign.Center);
        // os nomes em colunas dos lados, longe do placar (que ocupa ~ de 430 a 770)
        var tam = Math.Min(Encaixar(N(p.MandanteId), 52, 300), Encaixar(N(p.VisitanteId), 52, 300));
        Texto(c, N(p.MandanteId), 220, 258, tam, p.MandanteId == Galo ? Ouro : Cinza, Negrito.Value, SKTextAlign.Center);
        Texto(c, N(p.VisitanteId), L - 220, 258, tam, p.VisitanteId == Galo ? Ouro : Cinza, Negrito.Value, SKTextAlign.Center);
        if (pg.Desfecho is { } d) Texto(c, d, L / 2f, 330, 26, Cinza, Normal.Value, SKTextAlign.Center);
        if (pg.Gols.Count > 0)
        {
            var gols = string.Join("  ·  ", pg.Gols.Select(g => $"{g.Jogador} {g.Minuto}"));
            Texto(c, gols, L / 2f, 400, Encaixar(gols, 30, L - 120), Branco, Normal.Value, SKTextAlign.Center);
        }
        if (pg.Palpite is { Acertou: { } acertou })
        {
            var (cor, frase) = acertou ? (Verde, "O modelo acertou") : (Vermelho, "O modelo errou");
            using var tinta = new SKPaint { Color = cor, IsAntialias = true };
            var w = Largura(frase, 30, Negrito.Value) + 60;
            c.DrawRoundRect(new SKRect(L / 2f - w / 2, 450, L / 2f + w / 2, 506), 28, 28, tinta);
            Texto(c, frase, L / 2f, 489, 30, Branco, Negrito.Value, SKTextAlign.Center);
        }
    }

    static void Barra(SKCanvas c, float x, float y, float w, float h, double pct, SKColor cor)
    {
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawRoundRect(new SKRect(x, y, x + Math.Max(h, (float)(w * pct / 100)), y + h), h / 2, h / 2, tinta);
    }

    static void Lupa(SKCanvas c, float x, float y, float tam)
    {
        var s = tam / 120f;
        using var ouro = new SKPaint { Color = Ouro, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 15 * s, StrokeCap = SKStrokeCap.Round };
        c.DrawLine(x + 78 * s, y + 78 * s, x + 108 * s, y + 108 * s, ouro);
        using var barra = new SKPaint { Color = Ouro, IsAntialias = true };
        foreach (var (bx, by, bh) in new[] { (29f, 54f, 20f), (45f, 42f, 32f), (61f, 29f, 45f) })
            c.DrawRoundRect(new SKRect(x + bx * s, y + by * s, x + (bx + 11) * s, y + (by + bh) * s), 3 * s, 3 * s, barra);
        ouro.StrokeWidth = 7 * s;
        c.DrawCircle(x + 50 * s, y + 50 * s, 38 * s, ouro);
    }

    static float Largura(string t, float tam, SKTypeface f)
    {
        using var fonte = new SKFont(f, tam);
        return fonte.MeasureText(t);
    }

    /// <summary>O maior tamanho até `max` em que o texto cabe na largura.</summary>
    static float Encaixar(string t, float max, float largura)
    {
        var tam = max;
        while (tam > 18 && Largura(t, tam, Negrito.Value) > largura) tam -= 2;
        return tam;
    }

    static void Texto(SKCanvas c, string t, float x, float y, float tam, SKColor cor, SKTypeface f, SKTextAlign alinha = SKTextAlign.Left)
    {
        using var fonte = new SKFont(f, tam);
        using var tinta = new SKPaint { Color = cor, IsAntialias = true };
        c.DrawText(t, x, y, alinha, fonte, tinta);
    }
}
