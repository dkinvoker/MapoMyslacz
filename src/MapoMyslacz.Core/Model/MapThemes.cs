namespace MapoMyslacz.Core.Model;

/// <summary>
/// Styl strzałek: kolor (null — z motywu mapy), czy w kolorze gałęzi kafelka, z którego wychodzi, grubość, wzór,
/// kształt i groty. Może pochodzić z motywu albo być wybrany osobno (np. motyw „Pergamin” + strzałki „Łamane”).
/// </summary>
public sealed record LinkTheme(string Id, string Name, string Description, Rgba? Color, bool BranchColored, double Width,
    LinePattern Pattern, LinkCurve Curve, bool ArrowEnd = true, bool ArrowStart = false);

/// <summary>
/// Motyw mapy: kolory płótna i domyślnego wyglądu kafelków, paleta gałęzi i domyślne strzałki. Motyw uzupełnia tylko
/// to, czego kafelek albo strzałka nie ustawia sama — własne kolory zostają (można je „ujednolicić” z motywem).
/// </summary>
public sealed record MapTheme(string Id, string Name, string Description,
    Rgba? Canvas, Rgba NodeFill, Rgba? NodeText, Rgba NodeBorder, double NodeBorderWidth,
    Rgba CentralFill, Rgba? CentralText, Rgba[] BranchPalette, LinkTheme Links);

public static class MapThemes
{
    private static Rgba H(string hex) => Rgba.Parse(hex, Rgba.Black);
    private static Rgba[] P(params string[] hex) => hex.Select(H).ToArray();

    // ------------------------------------------------------------------ style strzałek

    public static readonly LinkTheme Dashed = new("przerywane", "Przerywane łuki", "Klasyczne przerywane łuki z grotem",
        null, false, 2, LinePattern.Dashed, LinkCurve.Curved);

    public static readonly IReadOnlyList<LinkTheme> LinkThemes =
    [
        Dashed,
        new("ciagle", "Ciągłe łuki", "Gładkie ciągłe łuki", null, false, 2, LinePattern.Solid, LinkCurve.Curved),
        new("kropki", "Kropkowane", "Lekkie kropkowane łuki — relacje „w tle”", null, false, 2.5, LinePattern.Dotted, LinkCurve.Curved),
        new("proste", "Proste", "Proste odcinki", null, false, 1.75, LinePattern.Solid, LinkCurve.Straight),
        new("lamane", "Łamane (schemat)", "Pod kątem prostym, jak w schematach i drzewach genealogicznych", null, false, 1.75, LinePattern.Solid, LinkCurve.Elbow),
        new("kolorowe", "Kolorowe", "W kolorze gałęzi kafelka, z którego wychodzą — widać, czyja to relacja", null, true, 2.5, LinePattern.Solid, LinkCurve.Curved),
        new("dwustronne", "Dwukierunkowe", "Groty na obu końcach (relacje wzajemne)", null, false, 2, LinePattern.Dashed, LinkCurve.Curved, true, true),
    ];

    // ------------------------------------------------------------------ motywy

    /// <summary>Domyślny: dokładnie dotychczasowy wygląd (płótno jasne albo ciemne jak Windows).</summary>
    public static readonly MapTheme Default = new("domyslny", "Domyślny", "Jasny albo ciemny jak Windows",
        null, Styles.DefaultFill, null, Styles.DefaultBorder, 0, Styles.DefaultFill, null, Styles.BranchPalette,
        Dashed with { Color = Styles.DefaultLinkColor });

    public static readonly IReadOnlyList<MapTheme> All =
    [
        Default,
        new("pastel", "Pastelowy", "Jasne, łagodne kolory",
            H("#FBF8F3"), H("#FFFFFF"), H("#4A4458"), H("#E3DDF0"), 1.5, H("#C9B8F5"), H("#2E2547"),
            P("#E88B8B", "#F2AE6F", "#7FC79A", "#6CC4CF", "#7FA9EB", "#B094EA", "#EB97C4", "#C9A57D"),
            Dashed with { Color = H("#B4A7CF") }),
        new("noc", "Noc", "Ciemne płótno, jaskrawe gałęzie",
            H("#12121A"), H("#232334"), H("#E8E6F2"), H("#3A3A55"), 1, H("#7C3AED"), H("#FFFFFF"),
            P("#FF5C8A", "#FF9F43", "#2ED573", "#1ED6C7", "#3DA5FF", "#A66CFF", "#FF6BD6", "#E0B354"),
            Dashed with { Color = H("#7A7A9C") }),
        new("pergamin", "Pergamin", "Do sesji RPG — papier, sepia, atrament",
            H("#F1E6CC"), H("#FAF3E0"), H("#3B2A18"), H("#8B6B3E"), 1.5, H("#7A2E1F"), H("#FBF3E0"),
            P("#8B2E2E", "#B5651D", "#556B2F", "#2F5D62", "#5B4A8B", "#8B6B3E", "#7A3E65", "#4A6B8A"),
            Dashed with { Color = H("#7A5A30") }),
        new("tablica", "Tablica", "Zielona tablica i kreda",
            H("#24352D"), H("#2E4239"), H("#F3EFE2"), H("#DAD5C5"), 1.5, H("#F3EFE2"), H("#24352D"),
            P("#F7A8A8", "#F9D07A", "#B9E6A5", "#9FDCE8", "#B7C6F7", "#E0B9F2", "#F7B9DA", "#E8E3D3"),
            Dashed with { Color = H("#DAD5C5") }),
        new("schemat", "Schemat", "Rzeczowy — biel, granat, łamane strzałki",
            H("#F7F8FA"), H("#FFFFFF"), H("#1F2937"), H("#94A3B8"), 1.5, H("#1E3A8A"), H("#FFFFFF"),
            P("#2563EB", "#0891B2", "#059669", "#7C3AED", "#DB2777", "#EA580C", "#4B5563", "#0D9488"),
            LinkThemes[4] with { Color = H("#64748B") }),
        new("kontrast", "Wysoki kontrast", "Czarno na białym, grube linie — czytelne i w druku",
            H("#FFFFFF"), H("#FFFFFF"), H("#000000"), H("#000000"), 2, H("#000000"), H("#FFFFFF"),
            P("#C00000", "#0040C0", "#007A00", "#8000A0", "#C05000", "#006070", "#A00060", "#505050"),
            LinkThemes[1] with { Color = H("#000000"), Width = 2.5 }),
    ];

    public static MapTheme Find(string? id) => All.FirstOrDefault(t => t.Id == id) ?? Default;

    /// <summary>Styl strzałek mapy: wybrany osobno albo ten z motywu.</summary>
    public static LinkTheme LinksFor(MindMap map)
    {
        var theme = Find(map.Theme);
        if (map.ArrowStyle is null || LinkThemes.FirstOrDefault(l => l.Id == map.ArrowStyle) is not { } chosen) return theme.Links;
        // kolor strzałek wybranego stylu bez własnego koloru — z motywu
        return chosen.Color is null && !chosen.BranchColored ? chosen with { Color = theme.Links.Color } : chosen;
    }

    /// <summary>Czy kafelek jest „tematem głównym” (bez rodzica, z gałęziami) — motyw może go wyróżnić kolorem.</summary>
    public static bool IsCentral(MapIndex idx, Node n) => n.Parent is null && idx.ChildrenOf(n.Id).Count > 0;

    /// <summary>Ostateczny wygląd strzałki z uwzględnieniem motywu (do menu i panelu).</summary>
    public static ResolvedLinkStyle ResolveLink(MindMap map, MapIndex idx, Link l)
    {
        var theme = Find(map.Theme);
        var from = idx.Get(l.From);
        return Styles.Resolve(l.Style, LinksFor(map), from is null ? null : Styles.BranchColorFor(idx, from, theme));
    }

    /// <summary>
    /// „Ujednolić z motywem”: usuwa własne kolory kafelków (wypełnienie, tekst, ramka, gałąź) i strzałek (kolor, wzór,
    /// grubość) — zostają kształty kafelków, czcionki, znaczniki i wygięcia strzałek.
    /// </summary>
    public static int ClearCustomColors(MindMap map)
    {
        int n = 0;
        foreach (var node in map.Nodes)
        {
            if (node.Style is not { } s) continue;
            if (s.Fill is null && s.TextColor is null && s.Border is null && s.BranchColor is null && s.BorderWidth is null) continue;
            s.Fill = s.TextColor = s.Border = s.BranchColor = null;
            s.BorderWidth = null;
            s.BorderPattern = null;
            node.Style = s.IsEmpty ? null : s;
            n++;
        }
        foreach (var link in map.Links)
        {
            if (link.Style is not { } s || (s.Color is null && s.Pattern is null && s.Width is null)) continue;
            s.Color = null;
            s.Pattern = null;
            s.Width = null;
            link.Style = s.IsEmpty ? null : s;
            n++;
        }
        map.Background = null;
        return n;
    }
}
