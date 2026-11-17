using System.Text;
using System.Text.RegularExpressions;

namespace MapoMyslacz.Core.Model;

/// <summary>
/// Mapa jako konspekt Markdown (do notatek, wiki, Obsidiana, wydruku) i z powrotem.
/// Tematy to nagłówki „##”, gałęzie — listy z wcięciami, notatki — cytaty „>”, linki — [tekst](adres),
/// znaczniki — na początku tekstu. Strzałki i ramki trafiają do osobnych sekcji, które import odtwarza.
/// </summary>
public static partial class MarkdownOutline
{
    public const string ArrowsSection = "Powiązania (strzałki)";
    public const string FramesSection = "Ramki";

    // ------------------------------------------------------------------ eksport

    /// <summary>
    /// Kilka arkuszy w jednym konspekcie: każdy zaczyna się nagłówkiem „#” ze swoją nazwą (import takiego pliku
    /// odtwarza arkusze — <see cref="ParseSheets"/>). Jeden arkusz — zwykły konspekt mapy.
    /// </summary>
    public static string WriteSheets(IReadOnlyList<(string Name, MindMap Map)> sheets)
    {
        if (sheets.Count == 1) return Write(sheets[0].Map);
        return string.Join(Environment.NewLine, sheets.Select(s => Write(s.Map, s.Name)));
    }

    public static string Write(MindMap map, string? title = null)
    {
        var idx = new MapIndex(map);
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(OneLine(title is { Length: > 0 } ? title : map.Title is { Length: > 0 } t ? t : "Mapa")).AppendLine();

        void Note(Node n, string indent)
        {
            if (string.IsNullOrWhiteSpace(n.Note)) return;
            foreach (var line in n.Note.Replace("\r\n", "\n").Split('\n'))
                sb.Append(indent).Append('>').Append(line.Length > 0 ? " " : "").AppendLine(line.TrimEnd());
        }

        void Item(Node n, int depth)
        {
            var indent = new string(' ', (depth - 1) * 2);
            sb.Append(indent).Append("- ").AppendLine(Label(n));
            Note(n, indent + "  ");
            foreach (var c in Children(idx, n)) Item(c, depth + 1);
        }

        foreach (var root in map.Nodes.Where(n => idx.ParentOf(n) is null).OrderBy(n => n.Y).ThenBy(n => n.X))
        {
            sb.Append("## ").AppendLine(Label(root));
            Note(root, "");
            foreach (var c in Children(idx, root)) Item(c, 1);
            sb.AppendLine();
        }

        if (map.Links.Count > 0)
        {
            sb.Append("## ").AppendLine(ArrowsSection);
            foreach (var l in map.Links)
            {
                if (idx.Get(l.From) is not { } a || idx.Get(l.To) is not { } b) continue;
                sb.Append("- ").Append(OneLine(a.Text)).Append(" → ").Append(OneLine(b.Text));
                if (!string.IsNullOrWhiteSpace(l.Label)) sb.Append(" (").Append(OneLine(l.Label)).Append(')');
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        if (map.Frames is { Count: > 0 } frames)
        {
            sb.Append("## ").AppendLine(FramesSection);
            foreach (var f in frames)
            {
                var names = f.Nodes.Select(idx.Get).OfType<Node>().Select(n => OneLine(n.Text));
                sb.Append("- ").Append(OneLine(f.Title is { Length: > 0 } ft ? ft : "Ramka")).Append(": ").AppendLine(string.Join("; ", names));
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static IEnumerable<Node> Children(MapIndex idx, Node n) => idx.ChildrenOf(n.Id).OrderBy(c => c.Y).ThenBy(c => c.X);

    private static string OneLine(string? s) => (s ?? "").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string Label(Node n)
    {
        var text = OneLine(n.Text);
        if (text.Length == 0) text = "…";
        var icons = string.Join(" ", (n.Icons ?? []).Select(id => Markers.Find(id) switch
        {
            { Group: MarkerGroup.Priority } m => "P" + m.Glyph,
            { Group: MarkerGroup.Progress } m => "[" + m.Glyph + "]",
            var m => m.Glyph,
        }));
        if (!string.IsNullOrWhiteSpace(n.Link))
        {
            var link = n.Link.Trim();
            text = $"[{text.Replace("]", "\\]")}]({(link.Contains(' ') ? $"<{link}>" : link)})";
        }
        return icons.Length > 0 ? $"{icons} {text}" : text;
    }

    // ------------------------------------------------------------------ import

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^([-*+•]|\d{1,3}[.)])\s+(.*)$")]
    private static partial Regex ListRegex();

    [GeneratedRegex(@"^\[(.*)\]\((?:<(.+)>|(\S+))\)$")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^P([1-5])\s+")]
    private static partial Regex PriorityPrefix();

    [GeneratedRegex(@"^\[(0|25|50|75|100)%\]\s+")]
    private static partial Regex ProgressPrefix();

    [GeneratedRegex(@"^(.+?) → (.+?)(?: \((.*)\))?$")]
    private static partial Regex ArrowRegex();

    /// <summary>
    /// Konspekt z kilkoma nagłówkami „#” (np. eksport wielu arkuszy) → arkusz na każdy z nich; inaczej jedna mapa
    /// (<see cref="Parse"/>). Tekst przed pierwszym „#” też trafia do jednej mapy.
    /// </summary>
    public static List<MindMap> ParseSheets(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var starts = new List<int>();
        bool inCode = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("```", StringComparison.Ordinal)) inCode = !inCode;
            else if (!inCode && HeadingRegex().Match(t) is { Success: true } m && m.Groups[1].Length == 1) starts.Add(i);
        }
        bool textBefore = starts.Count > 0 && lines.Take(starts[0]).Any(l => !string.IsNullOrWhiteSpace(l));
        if (starts.Count < 2 || textBefore) return [Parse(markdown)];
        var sheets = new List<MindMap>();
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1] : lines.Length;
            sheets.Add(Parse(string.Join("\n", lines[starts[k]..end])));
        }
        return sheets;
    }

    /// <summary>Konspekt Markdown (albo zwykła lista z wcięciami) → nowa mapa ułożona w drzewa.</summary>
    public static MindMap Parse(string markdown)
    {
        var map = new MindMap();
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // najpłytszy poziom nagłówków to tematy główne; pierwszy „#” przed innymi nagłówkami — tytuł mapy
        var levels = lines.Select(l => HeadingRegex().Match(l.Trim())).Where(m => m.Success).Select(m => m.Groups[1].Length).ToList();

        // zwykły tekst bez nagłówków i punktów: wcięcia wyznaczają gałęzie (jak przy wklejaniu)
        if (levels.Count == 0 && !lines.Any(l => ListRegex().IsMatch(l.Trim())))
            return new MindMap { Nodes = OutlineParser.Parse(markdown).Nodes };
        int titleLevel = 0;
        if (levels.Count > 1 && levels[0] == 1 && levels.Skip(1).All(l => l > 1)) titleLevel = 1;
        int baseLevel = levels.Where(l => l != titleLevel).DefaultIfEmpty(1).Min();

        var headingStack = new List<(int Level, Node Node)>();
        var listStack = new List<(int Indent, Node Node)>();
        Node? last = null;
        string? section = null;
        var arrows = new List<string>();
        var frames = new List<string>();
        bool inCode = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { inCode = !inCode; continue; }
            if (inCode || string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.TrimStart();
            int indent = 0;
            foreach (var ch in line)
            {
                if (ch == ' ') indent++;
                else if (ch == '\t') indent += 4;
                else break;
            }

            var heading = HeadingRegex().Match(trimmed);
            if (heading.Success)
            {
                int level = heading.Groups[1].Length;
                var text = heading.Groups[2].Value.Trim().TrimEnd('#').Trim();
                listStack.Clear();
                if (level == titleLevel && map.Title is null && map.Nodes.Count == 0)
                {
                    map.Title = text;
                    continue;
                }
                section = level == baseLevel && (text == ArrowsSection || text == FramesSection) ? text : null;
                if (section is not null) continue;
                while (headingStack.Count > 0 && headingStack[^1].Level >= level) headingStack.RemoveAt(headingStack.Count - 1);
                last = Add(map, text, headingStack.Count > 0 ? headingStack[^1].Node : null);
                headingStack.Add((level, last));
                continue;
            }

            var item = ListRegex().Match(trimmed);
            if (section is not null)
            {
                if (item.Success) (section == ArrowsSection ? arrows : frames).Add(item.Groups[2].Value.Trim());
                continue;
            }
            if (item.Success)
            {
                while (listStack.Count > 0 && listStack[^1].Indent >= indent) listStack.RemoveAt(listStack.Count - 1);
                var parent = listStack.Count > 0 ? listStack[^1].Node : headingStack.Count > 0 ? headingStack[^1].Node : null;
                last = Add(map, item.Groups[2].Value.Trim(), parent);
                listStack.Add((indent, last));
                continue;
            }

            // cytat albo zwykły akapit — notatka ostatniego kafelka (bez kafelka — osobny temat)
            var noteLine = trimmed.StartsWith('>') ? trimmed[1..].TrimStart() : trimmed;
            if (last is null)
            {
                last = Add(map, noteLine, null);
                continue;
            }
            last.Note = last.Note is null ? noteLine : last.Note + "\n" + noteLine;
        }

        Node? Find(string text) => map.Nodes.FirstOrDefault(n => string.Equals(OneLine(n.Text), text.Trim(), StringComparison.Ordinal));
        foreach (var a in arrows)
        {
            var m = ArrowRegex().Match(a);
            if (!m.Success || Find(m.Groups[1].Value) is not { } from || Find(m.Groups[2].Value) is not { } to) continue;
            MapOps.AddLink(map, from.Id, to.Id, m.Groups[3].Success ? m.Groups[3].Value : null);
        }
        foreach (var f in frames)
        {
            int colon = f.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) continue;
            var ids = f[(colon + 2)..].Split("; ").Select(Find).OfType<Node>().Select(n => n.Id).ToList();
            MapOps.AddFrame(map, ids, f[..colon].Trim());
        }

        OutlineParser.LayoutRoots(map.Nodes);
        return map;
    }

    /// <summary>Kafelek z tekstu pozycji: znaczniki z początku, link z [tekst](adres).</summary>
    private static Node Add(MindMap map, string text, Node? parent)
    {
        var node = new Node { Parent = parent?.Id };
        var icons = new List<string>();
        while (true)
        {
            if (PriorityPrefix().Match(text) is { Success: true } p)
            {
                icons.Add("priority-" + p.Groups[1].Value);
                text = text[p.Length..];
            }
            else if (ProgressPrefix().Match(text) is { Success: true } g)
            {
                icons.Add("progress-" + g.Groups[1].Value);
                text = text[g.Length..];
            }
            else if (Markers.Symbols.FirstOrDefault(s => text.StartsWith(s.Glyph + " ", StringComparison.Ordinal)) is { } sym)
            {
                icons.Add(sym.Id);
                text = text[(sym.Glyph.Length + 1)..];
            }
            else break;
        }
        if (LinkRegex().Match(text) is { Success: true } link)
        {
            text = link.Groups[1].Value.Replace("\\]", "]");
            node.Link = link.Groups[2].Success ? link.Groups[2].Value : link.Groups[3].Value;
        }
        node.Text = text.Replace("**", "").Trim();
        if (icons.Count > 0) node.Icons = Markers.Sorted(icons);
        map.Nodes.Add(node);
        return node;
    }
}
