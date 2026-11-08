namespace MapoMyslacz.Core.Model;

/// <summary>Ostateczny wygląd kafelka po uzupełnieniu brakujących pól domyślnymi.</summary>
public sealed record ResolvedNodeStyle(
    NodeShape Shape,
    Rgba Fill,
    Rgba TextColor,
    Rgba Border,
    double BorderWidth,
    LinePattern BorderPattern,
    double FontSize,
    bool Bold,
    bool Italic,
    bool Strike,
    TextAlign Align,
    Rgba BranchColor)
{
    /// <summary>Maksymalna szerokość tekstu przed zawinięciem.</summary>
    public double WrapWidth(Node node) => node.Width ?? Math.Max(240, FontSize * 20);
}

public sealed record ResolvedLinkStyle(Rgba Color, double Width, LinePattern Pattern, bool ArrowStart, bool ArrowEnd, LinkCurve Curve, double FontSize);

/// <summary>Gotowy zestaw stylu do jednego kliknięcia (np. „Ważny”, „Lokacja”).</summary>
public sealed record StylePreset(string Name, string Description, NodeStyle Style);

/// <summary>Domyślny wygląd, paleta kolorów i gotowe style.</summary>
public static class Styles
{
    public static readonly Rgba CanvasBackground = Rgba.White;
    public static readonly Rgba DarkCanvasBackground = Rgba.Parse("#1F1E24", Rgba.Black);
    public static readonly Rgba DefaultFill = Rgba.Parse("#EEECF3", Rgba.White);
    public static readonly Rgba DarkText = Rgba.Parse("#1E1B26", Rgba.Black);
    public static readonly Rgba LightText = Rgba.White;
    public static readonly Rgba DefaultBorder = Rgba.Parse("#4B4757", Rgba.Black);
    public const double DefaultFontSize = 15;

    public static readonly Rgba DefaultLinkColor = Rgba.Parse("#8A8794", Rgba.Black);
    public const double DefaultLinkWidth = 2;
    public const double DefaultLinkFontSize = 13;

    /// <summary>Kolory gałęzi kolejnych dzieci tematu głównego (jak w klasycznych mapach myśli).</summary>
    public static readonly Rgba[] BranchPalette =
    [
        Rgba.Parse("#E5484D", Rgba.Black), Rgba.Parse("#F76B15", Rgba.Black), Rgba.Parse("#30A46C", Rgba.Black),
        Rgba.Parse("#12A594", Rgba.Black), Rgba.Parse("#0090FF", Rgba.Black), Rgba.Parse("#8E4EC6", Rgba.Black),
        Rgba.Parse("#D6409F", Rgba.Black), Rgba.Parse("#AD7F58", Rgba.Black),
    ];

    /// <summary>Paleta w panelu właściwości (wypełnienia, ramki, tekst, strzałki).</summary>
    public static readonly string[] Swatches =
    [
        "#FFFFFF", "#EEECF3", "#D9D6E0", "#8A8794", "#4B4757", "#1E1B26",
        "#FFD6D6", "#FF9595", "#E5484D", "#9B1C1C",
        "#FFE3C2", "#FFB35C", "#F76B15", "#FFF4B8", "#F5D90A",
        "#D3F5DD", "#7BD88F", "#30A46C", "#15831C",
        "#CDEFF7", "#6FD0F9", "#0090FF", "#1D4ED8",
        "#EBDDFB", "#B98BF0", "#7F00AC", "#5B21B6", "#F8D2EA", "#D6409F",
    ];

    /// <param name="canvas">Kolor płótna — od niego zależy automatyczny kolor tekstu na przezroczystym kafelku.</param>
    public static ResolvedNodeStyle Resolve(NodeStyle? s, Rgba branchColor, Rgba? canvas = null)
    {
        var shape = s?.Shape ?? NodeShape.Rounded;
        var fill = shape == NodeShape.None ? Rgba.Transparent : Rgba.Parse(s?.Fill, DefaultFill);
        var behindText = fill.A > 96 ? fill : canvas ?? CanvasBackground;
        var text = s?.TextColor is { } tc
            ? Rgba.Parse(tc, DarkText)
            : (behindText.PrefersLightText ? LightText : DarkText);
        double bw = s?.BorderWidth ?? 0;
        return new ResolvedNodeStyle(
            shape, fill, text,
            Rgba.Parse(s?.Border, DefaultBorder),
            Math.Clamp(bw, 0, 24),
            s?.BorderPattern ?? LinePattern.Solid,
            Math.Clamp(s?.FontSize ?? DefaultFontSize, 6, 120),
            s?.Bold ?? false,
            s?.Italic ?? false,
            s?.Strike ?? false,
            s?.Align ?? TextAlign.Center,
            branchColor);
    }

    public static ResolvedLinkStyle Resolve(LinkStyle? s) => new(
        Rgba.Parse(s?.Color, DefaultLinkColor),
        Math.Clamp(s?.Width ?? DefaultLinkWidth, 0.5, 24),
        s?.Pattern ?? LinePattern.Dashed,
        s?.ArrowStart ?? false,
        s?.ArrowEnd ?? true,
        s?.Curve ?? LinkCurve.Curved,
        Math.Clamp(s?.FontSize ?? DefaultLinkFontSize, 6, 72));

    /// <summary>
    /// Kolor gałęzi kafelka: własny (BranchColor) albo dziedziczony po przodkach; dla dzieci tematu głównego
    /// — kolejne kolory palety.
    /// </summary>
    public static Rgba BranchColorFor(MapIndex index, Node node)
    {
        var current = node;
        Node? childOfRoot = null;
        int guard = 0;
        while (current is not null && guard++ < 10_000)
        {
            if (current.Style?.BranchColor is { } own && Rgba.TryParse(own, out var c)) return c;
            var parent = current.Parent is null ? null : index.Get(current.Parent);
            if (parent is null) break;
            childOfRoot = current;
            current = parent;
        }
        if (childOfRoot is null || current is null) return BranchPalette[0];
        var siblings = index.ChildrenOf(current.Id);
        int i = 0;
        for (; i < siblings.Count; i++)
            if (ReferenceEquals(siblings[i], childOfRoot)) break;
        return BranchPalette[i % BranchPalette.Length];
    }

    public static readonly IReadOnlyList<StylePreset> Presets =
    [
        new("Zwykły", "Domyślny wygląd kafelka", new NodeStyle()),
        new("Tytuł", "Duży napis bez tła — nazwa mapy", new NodeStyle { Shape = NodeShape.None, FontSize = 34, Bold = true }),
        new("Temat główny", "Środek mapy", new NodeStyle { Shape = NodeShape.Rounded, Fill = "#5B21B6", FontSize = 22, Bold = true }),
        new("Ważny", "Fioletowy, pogrubiony (np. postać)", new NodeStyle { Fill = "#7F00AC", FontSize = 18, Bold = true }),
        new("Ramka", "Wyróżnienie grubą zieloną ramką", new NodeStyle { Border = "#15831C", BorderWidth = 6, FontSize = 18 }),
        new("Lokacja", "Różowy romb", new NodeStyle { Shape = NodeShape.Diamond, Fill = "#FF9595" }),
        new("Cel", "Owal z przerywaną ramką", new NodeStyle { Shape = NodeShape.Pill, Border = "#000000", BorderWidth = 2, BorderPattern = LinePattern.Dashed }),
        new("Karteczka", "Żółta notatka, tekst do lewej", new NodeStyle { Shape = NodeShape.Rectangle, Fill = "#FFF4B8", Align = TextAlign.Left, FontSize = 14 }),
        new("Pomysł", "Niebieska elipsa", new NodeStyle { Shape = NodeShape.Ellipse, Fill = "#CDEFF7" }),
        new("Uwaga", "Pomarańczowy sześciokąt", new NodeStyle { Shape = NodeShape.Hexagon, Fill = "#FFE3C2", Border = "#F76B15", BorderWidth = 2 }),
        new("Nieaktualne", "Przekreślone, bez tła", new NodeStyle { Shape = NodeShape.Rounded, Fill = "#FFFFFF", Border = "#D9D6E0", BorderWidth = 1, Strike = true, TextColor = "#8A8794" }),
    ];
}
