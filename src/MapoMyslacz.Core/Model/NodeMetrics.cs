namespace MapoMyslacz.Core.Model;

/// <summary>
/// Wspólne zasady wymiarowania kafelka: odstępy wewnętrzne i to, ile miejsca wokół tekstu zajmuje kształt
/// (romb czy elipsa muszą być większe niż prostokąt, żeby tekst się w nich zmieścił).
/// Aplikacja mierzy prawdziwy tekst, a import i testy korzystają z <see cref="Estimate"/>.
/// </summary>
public static class NodeMetrics
{
    public const double MinTextWidth = 8;

    public static (double X, double Y) Padding(NodeShape shape, double fontSize) => shape switch
    {
        NodeShape.None => (4, 2),
        NodeShape.Diamond => (8, 4),
        NodeShape.Ellipse => (6, 4),
        _ => (Math.Round(fontSize * 0.8 + 4), Math.Round(fontSize * 0.45 + 3)),
    };

    /// <summary>Wysokość wiersza tekstu (Segoe UI ma interlinię ok. 1,33 rozmiaru czcionki).</summary>
    public static double LineHeight(double fontSize) => Math.Ceiling(fontSize * 1.33);

    /// <summary>Rozmiar całego kafelka dla tekstu o rozmiarze (<paramref name="textW"/>, <paramref name="textH"/>).</summary>
    public static (double W, double H) OuterSize(NodeShape shape, double fontSize, double textW, double textH)
    {
        var (px, py) = Padding(shape, fontSize);
        double cw = Math.Max(textW, MinTextWidth) + 2 * px;
        double ch = Math.Max(textH, LineHeight(fontSize)) + 2 * py;
        return shape switch
        {
            NodeShape.Pill => (cw + ch * 0.5, ch),
            NodeShape.Ellipse => (cw * 1.42, ch * 1.42),
            // tekst wpisany w romb: |x|/a + |y|/b ≤ 1 dla narożnika prostokąta → 1/1,7 + 1/2,43 = 1
            NodeShape.Diamond => (cw * 1.7, ch * 2.43),
            NodeShape.Hexagon => (cw + ch * 0.9, ch),
            _ => (Math.Max(cw, 36), ch),
        };
    }

    /// <summary>Przybliżona szerokość tekstu w Segoe UI (bez mierzenia czcionki).</summary>
    public static double EstimateTextWidth(string text, double fontSize, bool bold)
    {
        double w = 0;
        foreach (var ch in text)
        {
            double k = ch switch
            {
                ' ' => 0.27,
                'i' or 'l' or 'j' or 'ł' or 'ı' or '|' or '!' or '.' or ',' or ':' or ';' or '\'' => 0.25,
                'f' or 't' or 'r' or 'I' or '(' or ')' or '-' => 0.36,
                'm' or 'w' or 'M' or 'W' or 'Ś' or '@' => 0.82,
                _ when char.IsUpper(ch) => 0.64,
                _ when char.IsDigit(ch) => 0.55,
                _ => 0.52,
            };
            w += k;
        }
        return w * fontSize * (bold ? 1.07 : 1.0);
    }

    /// <summary>Szacuje rozmiar tekstu z zawijaniem słów do <paramref name="wrapWidth"/>.</summary>
    public static (double W, double H) EstimateText(string text, double fontSize, bool bold, double wrapWidth)
    {
        double maxW = 0;
        int lines = 0;
        foreach (var paragraph in (text.Length == 0 ? " " : text).Replace("\r\n", "\n").Split('\n'))
        {
            double lineW = 0;
            lines++;
            foreach (var word in paragraph.Split(' '))
            {
                double ww = EstimateTextWidth(word, fontSize, bold);
                double sp = lineW > 0 ? EstimateTextWidth(" ", fontSize, bold) : 0;
                if (lineW > 0 && lineW + sp + ww > wrapWidth)
                {
                    maxW = Math.Max(maxW, lineW);
                    lines++;
                    lineW = ww;
                }
                else
                {
                    lineW += sp + ww;
                }
            }
            maxW = Math.Max(maxW, lineW);
        }
        return (Math.Min(Math.Ceiling(maxW), wrapWidth), lines * LineHeight(fontSize));
    }

    /// <summary>Szacowany rozmiar całego kafelka.</summary>
    public static Vec Estimate(Node node, ResolvedNodeStyle style)
    {
        var (tw, th) = EstimateText(node.Text, style.FontSize, style.Bold, style.WrapWidth(node));
        var (w, h) = OuterSize(style.Shape, style.FontSize, tw, th);
        return new Vec(Math.Ceiling(w), Math.Ceiling(h));
    }

    public static Vec Estimate(MapIndex index, Node node) =>
        Estimate(node, Styles.Resolve(node.Style, Styles.BranchColorFor(index, node)));
}
