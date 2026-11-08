using System.Text.RegularExpressions;

namespace MapoMyslacz.Core.Model;

/// <summary>
/// Wklejanie zwykłego tekstu: każdy niepusty wiersz to kafelek, a wcięcia (tabulatory albo spacje)
/// tworzą gałęzie — można wkleić konspekt z notatnika, listę punktowaną z Worda albo listę Markdown.
/// </summary>
public static partial class OutlineParser
{
    [GeneratedRegex(@"^(?:[-*+•–·▪◦]\s+|\d{1,3}[.)]\s+|#{1,6}\s+)")]
    private static partial Regex BulletRegex();

    public static MapFragment Parse(string text)
    {
        var frag = new MapFragment();
        var stack = new List<(int Indent, Node Node)>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            int indent = 0;
            int i = 0;
            for (; i < raw.Length && raw[i] is ' ' or '\t'; i++) indent += raw[i] == '\t' ? 4 : 1;
            var content = BulletRegex().Replace(raw[i..].TrimEnd(), "").Trim();
            if (content.Length == 0) continue;
            // nagłówki Markdown: liczba # wyznacza poziom
            var heading = Regex.Match(raw[i..], @"^(#{1,6})\s");
            if (heading.Success) indent = (heading.Groups[1].Length - 1) * 4;

            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            var node = new Node { Text = content, Parent = stack.Count > 0 ? stack[^1].Node.Id : null };
            frag.Nodes.Add(node);
            stack.Add((indent, node));
        }

        // pozycje: drzewa obok siebie w pionie, każde uporządkowane w prawo
        var map = new MindMap { Nodes = frag.Nodes };
        double y = 0;
        foreach (var root in frag.Nodes.Where(n => n.Parent is null).ToList())
        {
            root.X = 0;
            root.Y = y;
            TreeLayout.Arrange(map, root.Id, MapOps.EstimatingSizer, side: 1);
            var idx = new MapIndex(map);
            var all = idx.Descendants(root.Id).Append(root).ToList();
            double bottom = all.Max(n => n.Y + MapOps.EstimatingSizer(n).Y / 2);
            double top = all.Min(n => n.Y - MapOps.EstimatingSizer(n).Y / 2);
            // przesunięcie, żeby kolejne drzewo zaczynało się pod poprzednim
            double shift = y - top;
            foreach (var n in all) n.Y += shift;
            y = bottom + shift + MapOps.VerticalGap * 2;
        }
        return frag;
    }
}
