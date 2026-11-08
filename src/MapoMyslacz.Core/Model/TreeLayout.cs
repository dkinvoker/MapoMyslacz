namespace MapoMyslacz.Core.Model;

/// <summary>
/// Automatyczne uporządkowanie gałęzi („Uporządkuj”): dzieci w równych odstępach obok rodzica,
/// poddrzewa jedno pod drugim bez nachodzenia. Temat główny (bez rodzica) dzieli gałęzie na prawą
/// i lewą stronę tak, żeby obie były podobnej wysokości.
/// </summary>
public static class TreeLayout
{
    public static void ArrangeAll(MindMap map, NodeSizer size, double hGap = MapOps.HorizontalGap, double vGap = MapOps.VerticalGap)
    {
        var index = new MapIndex(map);
        foreach (var root in map.Nodes.Where(n => n.Parent is null).ToList())
            if (index.ChildrenOf(root.Id).Count > 0) Arrange(index, root, size, hGap, vGap);
    }

    /// <summary>
    /// Układa potomków kafelka <paramref name="rootId"/>; sam kafelek zostaje na miejscu.
    /// <paramref name="side"/> wymusza stronę (+1 prawa, −1 lewa) — domyślnie temat główny dzieli gałęzie na obie.
    /// </summary>
    public static void Arrange(MindMap map, string rootId, NodeSizer size, double hGap = MapOps.HorizontalGap,
        double vGap = MapOps.VerticalGap, int? side = null)
    {
        var index = new MapIndex(map);
        if (index.Get(rootId) is { } root) Arrange(index, root, size, hGap, vGap, side);
    }

    private static void Arrange(MapIndex index, Node root, NodeSizer size, double hGap, double vGap, int? forcedSide = null)
    {
        var heights = new Dictionary<string, double>();
        var children = Visible(index, root);
        if (children.Count == 0) return;

        if (forcedSide is not null || index.ParentOf(root) is not null)
        {
            int side = forcedSide ?? MapOps.SideOf(index, root);
            Place(index, root, children.OrderBy(c => c.Y).ToList(), side, size, hGap, vGap, heights);
            return;
        }

        // temat główny: kolejność „zgodnie z zegarem” (prawa strona z góry na dół, potem lewa z dołu do góry)
        var ordered = children.Where(c => c.X >= root.X).OrderBy(c => c.Y)
            .Concat(children.Where(c => c.X < root.X).OrderByDescending(c => c.Y)).ToList();
        double total = ordered.Sum(c => SubtreeHeight(index, c, size, vGap, heights)) + vGap * (ordered.Count - 1);
        var right = new List<Node>();
        double acc = 0;
        foreach (var c in ordered)
        {
            double h = SubtreeHeight(index, c, size, vGap, heights);
            if (right.Count > 0 && acc + h / 2 > total / 2) break;
            right.Add(c);
            acc += h + vGap;
        }
        var left = ordered.Skip(right.Count).Reverse().ToList();
        Place(index, root, right, 1, size, hGap, vGap, heights);
        Place(index, root, left, -1, size, hGap, vGap, heights);
    }

    private static List<Node> Visible(MapIndex index, Node n) => n.Collapsed ? [] : index.ChildrenOf(n.Id).ToList();

    private static double SubtreeHeight(MapIndex index, Node n, NodeSizer size, double vGap, Dictionary<string, double> cache)
    {
        if (cache.TryGetValue(n.Id, out var h)) return h;
        var own = size(n).Y;
        var kids = Visible(index, n);
        double sum = kids.Count == 0 ? 0 : kids.Sum(k => SubtreeHeight(index, k, size, vGap, cache)) + vGap * (kids.Count - 1);
        h = Math.Max(own, sum);
        cache[n.Id] = h;
        return h;
    }

    private static void Place(MapIndex index, Node parent, List<Node> kids, int side, NodeSizer size, double hGap, double vGap,
        Dictionary<string, double> heights)
    {
        if (kids.Count == 0) return;
        double total = kids.Sum(k => SubtreeHeight(index, k, size, vGap, heights)) + vGap * (kids.Count - 1);
        double y = parent.Y - total / 2;
        double pw = size(parent).X;
        foreach (var k in kids)
        {
            double h = SubtreeHeight(index, k, size, vGap, heights);
            var ks = size(k);
            k.Y = Math.Round(y + h / 2);
            k.X = Math.Round(parent.X + side * (pw / 2 + hGap + ks.X / 2));
            Place(index, k, Visible(index, k).OrderBy(c => c.Y).ToList(), side, size, hGap, vGap, heights);
            y += h + vGap;
        }
    }
}
