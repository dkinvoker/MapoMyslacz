using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MapoMyslacz.Core.Model;

namespace MapoMyslacz.Core.Import;

/// <summary>
/// Import map z XMind (format XMind 2020+ / XMind Zen: archiwum ZIP z plikiem content.json).
/// Przenosi: tematy (główny, dzieci, tematy swobodne z pozycjami), style (kształt, wypełnienie, ramka,
/// czcionka), relacje z podpisami i krzywizną, notatki i zwinięte gałęzie. Każdy arkusz to osobna mapa.
/// Nie przenosi obrazków, ikon (markerów), granic i podsumowań.
/// </summary>
public static partial class XmindImporter
{
    [GeneratedRegex(@"^\s*(mind\s*map|map|sheet|arkusz|mapa(\s+myśli)?|plansza)\s*\d*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericSheetTitle();

    public static IReadOnlyList<MindMap> Import(string path)
    {
        using var stream = File.OpenRead(path);
        var maps = Import(stream);
        // jeden arkusz bez nazwy → tytuł z nazwy pliku
        if (maps.Count == 1 && string.IsNullOrWhiteSpace(maps[0].Title)) maps[0].Title = Path.GetFileNameWithoutExtension(path);
        return maps;
    }

    public static IReadOnlyList<MindMap> Import(Stream xmindZip)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(xmindZip, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("To nie jest plik XMind (nie da się go otworzyć jako archiwum).", ex);
        }
        using (zip)
        {
            var entry = zip.GetEntry("content.json")
                ?? throw new InvalidDataException(zip.GetEntry("content.xml") is not null
                    ? "To mapa ze starego XMind 8 (content.xml) — otwórz ją w nowym XMind i zapisz ponownie."
                    : "W pliku XMind brakuje content.json.");
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            return ImportContentJson(reader.ReadToEnd());
        }
    }

    public static IReadOnlyList<MindMap> ImportContentJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var sheets = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        var result = new List<MindMap>();
        foreach (var sheet in sheets)
        {
            if (sheet.ValueKind != JsonValueKind.Object || !sheet.TryGetProperty("rootTopic", out var root)) continue;
            result.Add(ImportSheet(sheet, root));
        }
        if (result.Count == 0) throw new InvalidDataException("Plik XMind nie zawiera żadnego arkusza.");
        return result;
    }

    // ------------------------------------------------------------------ arkusz

    private sealed class Ctx
    {
        public required Dictionary<string, Dictionary<string, string>> Theme;
        public required MindMap Map;
        public readonly List<string> NeedLayout = [];
    }

    private static MindMap ImportSheet(JsonElement sheet, JsonElement root)
    {
        var ctx = new Ctx { Theme = ReadTheme(sheet), Map = new MindMap() };
        var map = ctx.Map;
        // domyślne nazwy arkuszy („Mind Map”, „Arkusz 1”…) nic nie mówią — wtedy tytuł weźmie się z nazwy pliku
        var sheetTitle = Str(sheet, "title");
        map.Title = string.IsNullOrWhiteSpace(sheetTitle) || GenericSheetTitle().IsMatch(sheetTitle) ? null : sheetTitle.Trim();

        // tło płótna z motywu XMind (kolory relacji są dobrane do niego)
        if (ctx.Theme.TryGetValue("map", out var mapProps) && Color(mapProps.GetValueOrDefault("svg:fill")) is { } bg)
            map.Background = bg.ToString();

        AddTopic(ctx, root, parentId: null, baseClass: "centralTopic", at: new Vec(0, 0), isRoot: true);

        // dzieci bez zapisanych pozycji (XMind układa je sam) — układamy drzewem
        foreach (var id in ctx.NeedLayout.Distinct())
            TreeLayout.Arrange(map, id, MapOps.EstimatingSizer);

        if (sheet.TryGetProperty("relationships", out var rels) && rels.ValueKind == JsonValueKind.Array)
            foreach (var rel in rels.EnumerateArray()) AddRelationship(ctx, rel);

        MapOps.Repair(map);
        return map;
    }

    private static void AddTopic(Ctx ctx, JsonElement topic, string? parentId, string baseClass, Vec at, bool isRoot = false)
    {
        var node = new Node
        {
            Id = Str(topic, "id") is { Length: > 0 } id ? id : Ids.New(),
            Text = TitleOf(topic),
            Parent = parentId,
            X = Math.Round(at.X, 1),
            Y = Math.Round(at.Y, 1),
        };
        if (topic.TryGetProperty("position", out var pos) && pos.ValueKind == JsonValueKind.Object)
        {
            node.X = Math.Round(Num(pos, "x"), 1);
            node.Y = Math.Round(Num(pos, "y"), 1);
        }
        node.Style = ConvertTopicStyle(ctx, topic, baseClass, isRoot);
        node.Note = NoteOf(topic);
        node.Collapsed = Str(topic, "branch") == "folded";
        ctx.Map.Nodes.Add(node);

        if (!topic.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Object) return;

        if (children.TryGetProperty("attached", out var attached) && attached.ValueKind == JsonValueKind.Array)
        {
            bool anyWithoutPosition = false;
            foreach (var child in attached.EnumerateArray())
            {
                anyWithoutPosition |= !child.TryGetProperty("position", out _);
                AddTopic(ctx, child, node.Id, isRoot ? "mainTopic" : "subTopic", new Vec(node.X, node.Y));
            }
            if (anyWithoutPosition) ctx.NeedLayout.Add(node.Id);
        }
        if (children.TryGetProperty("detached", out var detached) && detached.ValueKind == JsonValueKind.Array)
        {
            // tematy swobodne — pozycja w XMind jest względem środka tematu głównego, czyli u nas (0, 0)
            int i = 0;
            foreach (var child in detached.EnumerateArray())
                AddTopic(ctx, child, null, "floatingTopic", new Vec(node.X + 250, node.Y + 60 * i++));
        }
    }

    private static string TitleOf(JsonElement topic)
    {
        if (topic.TryGetProperty("attributedTitle", out var at) && at.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var part in at.EnumerateArray()) sb.Append(Str(part, "text"));
            if (sb.Length > 0) return Clean(sb.ToString());
        }
        return Clean(Str(topic, "title") ?? "");
    }

    private static string Clean(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string? NoteOf(JsonElement topic)
    {
        var parts = new List<string>();
        if (topic.TryGetProperty("notes", out var notes) && notes.TryGetProperty("plain", out var plain)
            && Str(plain, "content") is { Length: > 0 } text)
            parts.Add(Clean(text));
        if (topic.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            var l = labels.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (l.Count > 0) parts.Add("Etykiety: " + string.Join(", ", l));
        }
        if (Str(topic, "href") is { Length: > 0 } href && !href.StartsWith("xmind:", StringComparison.OrdinalIgnoreCase))
            parts.Add("Link: " + href);
        if (topic.TryGetProperty("image", out _)) parts.Add("(W XMind był tu obrazek — nie został przeniesiony.)");
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    // ------------------------------------------------------------------ style

    private static Dictionary<string, Dictionary<string, string>> ReadTheme(JsonElement sheet)
    {
        var theme = new Dictionary<string, Dictionary<string, string>>();
        if (!sheet.TryGetProperty("theme", out var t) || t.ValueKind != JsonValueKind.Object) return theme;
        foreach (var cls in t.EnumerateObject())
            if (cls.Value.ValueKind == JsonValueKind.Object && cls.Value.TryGetProperty("properties", out var props))
                theme[cls.Name] = Props(props);
        return theme;
    }

    private static Dictionary<string, string> Props(JsonElement props)
    {
        var d = new Dictionary<string, string>();
        if (props.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in props.EnumerateObject())
        {
            var v = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.GetRawText();
            if (!string.IsNullOrWhiteSpace(v) && v != "inherited") d[p.Name] = v.Trim();
        }
        return d;
    }

    /// <summary>Styl efektywny: motyw (klasa bazowa) → klasa tematu (np. importantTopic) → własny styl tematu.</summary>
    private static Dictionary<string, string> Effective(Ctx ctx, JsonElement element, params string?[] classes)
    {
        var d = new Dictionary<string, string>();
        foreach (var c in classes)
            if (c is not null && ctx.Theme.TryGetValue(c, out var props))
                foreach (var kv in props) d[kv.Key] = kv.Value;
        if (element.TryGetProperty("style", out var style) && style.TryGetProperty("properties", out var own))
            foreach (var kv in Props(own)) d[kv.Key] = kv.Value;
        return d;
    }

    private static NodeStyle? ConvertTopicStyle(Ctx ctx, JsonElement topic, string baseClass, bool isRoot)
    {
        var p = Effective(ctx, topic, baseClass, Str(topic, "class"));
        var s = new NodeStyle();

        bool noFill = p.GetValueOrDefault("fill-pattern") == "none";
        double borderWidth = Length(p.GetValueOrDefault("border-line-width")) ?? 0;
        var shape = ShapeFrom(p.GetValueOrDefault("shape-class"));
        if (noFill && borderWidth <= 0) shape = NodeShape.None;
        if (shape is { } sh && sh != NodeShape.Rounded) s.Shape = sh;

        if (!noFill && Color(p.GetValueOrDefault("svg:fill")) is { } fill && !Similar(fill, Styles.DefaultFill))
            s.Fill = fill.ToString();
        else if (noFill && shape != NodeShape.None)
            s.Fill = "#FFFFFF00";

        if (borderWidth > 0)
        {
            s.BorderWidth = Math.Round(Math.Max(1, borderWidth * 0.75), 1);
            if (Color(p.GetValueOrDefault("border-line-color")) is { } bc) s.Border = bc.ToString();
            s.BorderPattern = Pattern(p.GetValueOrDefault("border-line-pattern"));
            if (s.BorderPattern == LinePattern.Solid) s.BorderPattern = null;
        }

        if (Color(p.GetValueOrDefault("fo:color")) is { } tc) s.TextColor = tc.ToString();
        if (Length(p.GetValueOrDefault("fo:font-size")) is { } fs && Math.Abs(fs - Styles.DefaultFontSize) > 0.1) s.FontSize = fs;
        var weight = p.GetValueOrDefault("fo:font-weight");
        if (weight == "bold" || int.TryParse(weight, out var w) && w >= 600) s.Bold = true;
        if (p.GetValueOrDefault("fo:font-style") == "italic") s.Italic = true;
        if (p.GetValueOrDefault("fo:text-decoration")?.Contains("line-through") == true) s.Strike = true;
        s.Align = p.GetValueOrDefault("fo:text-align") switch
        {
            "left" => isRoot ? null : TextAlign.Left,
            "right" => TextAlign.Right,
            _ => null,
        };
        // jednowierszowy tekst wygląda tak samo przy każdym wyrównaniu — nie zaśmiecamy pliku
        if (s.Align == TextAlign.Left && !TitleOf(topic).Contains('\n') && TitleOf(topic).Length < 40) s.Align = null;
        return s.IsEmpty ? null : s;
    }

    private static NodeShape? ShapeFrom(string? cls) => cls switch
    {
        null => null,
        _ when cls.EndsWith(".roundedRect", StringComparison.OrdinalIgnoreCase) => NodeShape.Rounded,
        _ when cls.EndsWith(".ellipserect", StringComparison.OrdinalIgnoreCase) => NodeShape.Pill,
        _ when cls.EndsWith(".rect", StringComparison.OrdinalIgnoreCase) => NodeShape.Rectangle,
        _ when cls.EndsWith(".ellipse", StringComparison.OrdinalIgnoreCase) || cls.EndsWith(".circle", StringComparison.OrdinalIgnoreCase) => NodeShape.Ellipse,
        _ when cls.EndsWith(".diamond", StringComparison.OrdinalIgnoreCase) => NodeShape.Diamond,
        _ when cls.Contains("hexagon", StringComparison.OrdinalIgnoreCase) => NodeShape.Hexagon,
        _ when cls.EndsWith(".underline", StringComparison.OrdinalIgnoreCase) || cls.EndsWith(".noBorder", StringComparison.OrdinalIgnoreCase) => NodeShape.None,
        _ => NodeShape.Rounded,
    };

    private static LinePattern Pattern(string? p) => p switch
    {
        "dash" or "dash-dot" or "dash-dot-dot" => LinePattern.Dashed,
        "dot" => LinePattern.Dotted,
        _ => LinePattern.Solid,
    };

    private static bool Similar(Rgba a, Rgba b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 24 && a.A == b.A;

    // ------------------------------------------------------------------ relacje

    private static void AddRelationship(Ctx ctx, JsonElement rel)
    {
        var from = Str(rel, "end1Id");
        var to = Str(rel, "end2Id");
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to) return;
        var link = new Link
        {
            Id = Str(rel, "id") is { Length: > 0 } id ? id : Ids.New(),
            From = from,
            To = to,
            Label = Str(rel, "title") is { Length: > 0 } t ? Clean(t) : null,
        };

        var p = Effective(ctx, rel, "relationship");
        var shapeClass = p.GetValueOrDefault("shape-class") ?? "";
        bool flexible = shapeClass.Contains("flexible", StringComparison.OrdinalIgnoreCase);

        if (flexible && rel.TryGetProperty("flexibleControlPoints", out var fcp) && fcp.ValueKind == JsonValueKind.Array)
        {
            link.Via = fcp.EnumerateArray().Select(v => new Vec(Math.Round(Num(v, "x"), 1), Math.Round(Num(v, "y"), 1))).ToList();
        }
        else if (rel.TryGetProperty("controlPoints", out var cps) && cps.ValueKind == JsonValueKind.Object)
        {
            // XMind: punkt „0” względem środka tematu end1, „1” względem end2 — tak samo jak u nas
            if (cps.TryGetProperty("0", out var c0)) link.C1 = new Vec(Math.Round(Num(c0, "x"), 1), Math.Round(Num(c0, "y"), 1));
            if (cps.TryGetProperty("1", out var c1)) link.C2 = new Vec(Math.Round(Num(c1, "x"), 1), Math.Round(Num(c1, "y"), 1));
            // brakujący drugi punkt: XMind liczy go sam — u nas zostaje automatyczny
        }

        var s = new LinkStyle();
        if (Color(p.GetValueOrDefault("line-color")) is { } c) s.Color = c.ToString();
        if (Length(p.GetValueOrDefault("line-width")) is { } lw && Math.Abs(lw - Styles.DefaultLinkWidth) > 0.01) s.Width = lw;
        var pattern = Pattern(p.GetValueOrDefault("line-pattern"));
        if (pattern != LinePattern.Dashed) s.Pattern = pattern;
        var begin = p.GetValueOrDefault("arrow-begin-class");
        var end = p.GetValueOrDefault("arrow-end-class");
        if (begin is not null && !begin.EndsWith(".none", StringComparison.OrdinalIgnoreCase)) s.ArrowStart = true;
        if (end is not null && end.EndsWith(".none", StringComparison.OrdinalIgnoreCase)) s.ArrowEnd = false;
        if (shapeClass.EndsWith(".straight", StringComparison.OrdinalIgnoreCase)) s.Curve = LinkCurve.Straight;
        if (Length(p.GetValueOrDefault("fo:font-size")) is { } fs && Math.Abs(fs - Styles.DefaultLinkFontSize) > 0.1) s.FontSize = fs;
        link.Style = s.IsEmpty ? null : s;
        ctx.Map.Links.Add(link);
    }

    // ------------------------------------------------------------------ pomocnicze

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : 0;

    /// <summary>„18pt”, „2”, „8pt” → liczba.</summary>
    private static double? Length(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim().ToLowerInvariant();
        foreach (var unit in new[] { "pt", "px" })
            if (s.EndsWith(unit)) s = s[..^unit.Length];
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static Rgba? Color(string? v) => v is not null && v.StartsWith('#') && Rgba.TryParse(v, out var c) ? c : null;
}
