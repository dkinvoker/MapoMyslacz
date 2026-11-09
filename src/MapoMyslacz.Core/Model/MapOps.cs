namespace MapoMyslacz.Core.Model;

/// <summary>Rozmiar kafelka (szerokość, wysokość) — w aplikacji zmierzony, w testach i imporcie szacowany.</summary>
public delegate Vec NodeSizer(Node node);

public enum Direction { Left, Right, Up, Down }

/// <summary>Kawałek mapy (kafelki + strzałki między nimi) — do schowka, duplikowania i wklejania konspektu.</summary>
public sealed class MapFragment
{
    public const string FormatName = "MapoMyslacz-fragment";

    /// <summary>Znacznik — tylko z nim JSON ze schowka jest traktowany jako kafelki MapoMyślacza.</summary>
    public string? Format { get; set; }
    public List<Node> Nodes { get; set; } = [];
    public List<Link> Links { get; set; } = [];
}

/// <summary>Operacje edycyjne na mapie. Bez interfejsu — testowane jednostkowo.</summary>
public static class MapOps
{
    public const double HorizontalGap = 56;
    public const double VerticalGap = 16;
    public const string DefaultText = "Nowy temat";

    /// <summary>Rozmiar szacowany z tekstu (bez mierzenia czcionki) — dla importu i testów.</summary>
    public static readonly NodeSizer EstimatingSizer = n => NodeMetrics.Estimate(n, Styles.Resolve(n.Style, Rgba.Black));

    /// <summary>Po której stronie rodzica rośnie gałąź kafelka: +1 w prawo, −1 w lewo.</summary>
    public static int SideOf(MapIndex index, Node node)
    {
        var parent = index.ParentOf(node);
        if (parent is null) return 1;
        return node.X < parent.X ? -1 : 1;
    }

    /// <summary>Dodaje dziecko: obok rodzica, pod ostatnim z jego dzieci (dla tematu głównego — na stronie z mniejszą liczbą gałęzi).</summary>
    public static Node AddChild(MindMap map, string parentId, NodeSizer size, string text = DefaultText)
    {
        var index = new MapIndex(map);
        var parent = index.Get(parentId) ?? throw new ArgumentException("Nie ma takiego kafelka.", nameof(parentId));
        var children = index.ChildrenOf(parentId);

        int side;
        if (index.ParentOf(parent) is not null) side = SideOf(index, parent);
        else
        {
            int right = children.Count(c => c.X >= parent.X), left = children.Count - right;
            side = right <= left ? 1 : -1;
        }

        var node = new Node { Text = text, Parent = parentId };
        var ps = size(parent);
        var cs = size(node);
        var sameSide = children.Where(c => Math.Sign(c.X - parent.X) == side || (c.X == parent.X && side == 1)).ToList();
        double x = parent.X + side * (ps.X / 2 + HorizontalGap + cs.X / 2);
        double y = parent.Y;
        if (sameSide.Count > 0)
        {
            var last = sameSide.MaxBy(c => c.Y + size(c).Y / 2)!;
            var ls = size(last);
            y = last.Y + ls.Y / 2 + VerticalGap + cs.Y / 2;
            // wyrównanie do lewej (po prawej stronie) / do prawej (po lewej) krawędzi rodzeństwa
            x = side > 0 ? last.X - ls.X / 2 + cs.X / 2 : last.X + ls.X / 2 - cs.X / 2;
        }
        node.X = Math.Round(x);
        node.Y = Math.Round(y);
        if (parent.Collapsed) parent.Collapsed = false;
        map.Nodes.Add(node);
        return node;
    }

    /// <summary>Dodaje rodzeństwo pod kafelkiem (dla kafelka bez rodzica — nowy swobodny kafelek pod nim).</summary>
    public static Node AddSibling(MindMap map, string nodeId, NodeSizer size, string text = DefaultText)
    {
        var index = new MapIndex(map);
        var node = index.Get(nodeId) ?? throw new ArgumentException("Nie ma takiego kafelka.", nameof(nodeId));
        var sibling = new Node { Text = text, Parent = node.Parent };
        if (node.Parent is null && node.Style is { } st)
        {
            // swobodny kafelek: nowy dostaje ten sam wygląd (wygodne przy listach notatek)
            sibling.Style = st.Clone();
        }
        var ns = size(node);
        var ss = size(sibling);
        int side = SideOf(index, node);
        // poniżej najniższego elementu poddrzewa, żeby nie wejść na wnuki
        double bottom = node.Y + ns.Y / 2;
        foreach (var d in index.Descendants(node.Id)) bottom = Math.Max(bottom, d.Y + size(d).Y / 2);
        sibling.Y = Math.Round(bottom + VerticalGap + ss.Y / 2);
        sibling.X = Math.Round(node.Parent is null ? node.X : side > 0 ? node.X - ns.X / 2 + ss.X / 2 : node.X + ns.X / 2 - ss.X / 2);
        map.Nodes.Add(sibling);
        return sibling;
    }

    public static Node AddFloating(MindMap map, Vec at, string text = DefaultText, NodeStyle? style = null)
    {
        var node = new Node { Text = text, X = Math.Round(at.X), Y = Math.Round(at.Y), Style = style?.Clone() };
        map.Nodes.Add(node);
        return node;
    }

    /// <summary>Usuwa kafelki razem z ich potomkami oraz wskazane strzałki (i strzałki prowadzące do usuniętych).</summary>
    public static int Delete(MindMap map, IEnumerable<string> nodeIds, IEnumerable<string>? linkIds = null)
    {
        var index = new MapIndex(map);
        var remove = new HashSet<string>();
        foreach (var id in nodeIds)
        {
            if (!index.Contains(id)) continue;
            remove.Add(id);
            foreach (var d in index.Descendants(id)) remove.Add(d.Id);
        }
        var removeLinks = new HashSet<string>(linkIds ?? []);
        int count = map.Nodes.RemoveAll(n => remove.Contains(n.Id));
        count += map.Links.RemoveAll(l => removeLinks.Contains(l.Id) || remove.Contains(l.From) || remove.Contains(l.To));
        return count;
    }

    /// <summary>Kafelki, które poruszą się razem z zaznaczeniem (opcjonalnie z całymi gałęziami).</summary>
    public static HashSet<string> MovingSet(MapIndex index, IEnumerable<string> ids, bool withDescendants)
    {
        var set = new HashSet<string>();
        foreach (var id in ids)
        {
            if (!index.Contains(id)) continue;
            set.Add(id);
            if (!withDescendants) continue;
            foreach (var d in index.Descendants(id)) set.Add(d.Id);
        }
        return set;
    }

    public static void Move(MindMap map, IEnumerable<string> ids, Vec delta, bool withDescendants = true)
    {
        var index = new MapIndex(map);
        foreach (var id in MovingSet(index, ids, withDescendants))
        {
            var n = index.Get(id)!;
            n.X += delta.X;
            n.Y += delta.Y;
        }
    }

    /// <summary>Podpina kafelek pod nowego rodzica (null = odłącza). Odmawia, gdyby powstała pętla.</summary>
    public static bool Reparent(MindMap map, string nodeId, string? newParentId)
    {
        var index = new MapIndex(map);
        var node = index.Get(nodeId);
        if (node is null) return false;
        if (newParentId is not null && (!index.Contains(newParentId) || index.IsAncestorOrSelf(nodeId, newParentId))) return false;
        node.Parent = newParentId;
        return true;
    }

    /// <summary>Nowa strzałka (null, gdy łączyłaby kafelek sam ze sobą albo taka już jest).</summary>
    public static Link? AddLink(MindMap map, string from, string to, string? label = null)
    {
        if (from == to) return null;
        var index = new MapIndex(map);
        if (!index.Contains(from) || !index.Contains(to)) return null;
        if (map.Links.Any(l => l.From == from && l.To == to)) return null;
        var link = new Link { From = from, To = to, Label = string.IsNullOrWhiteSpace(label) ? null : label };
        map.Links.Add(link);
        return link;
    }

    /// <summary>Odwraca kierunek strzałki (krzywizna zostaje ta sama).</summary>
    public static void Reverse(Link link)
    {
        (link.From, link.To) = (link.To, link.From);
        (link.C1, link.C2) = (link.C2, link.C1);
        link.Via?.Reverse();
        if (link.Style is { } s) (s.ArrowStart, s.ArrowEnd) = (s.ArrowEnd, s.ArrowStart);
    }

    // ------------------------------------------------------------------ schowek

    /// <summary>Kopiuje kafelki (z potomkami) i strzałki łączące skopiowane kafelki.</summary>
    public static MapFragment Copy(MindMap map, IEnumerable<string> nodeIds, bool withDescendants = true)
    {
        var index = new MapIndex(map);
        var ids = MovingSet(index, nodeIds, withDescendants);
        var frag = new MapFragment { Format = MapFragment.FormatName };
        foreach (var n in map.Nodes)
            if (ids.Contains(n.Id)) frag.Nodes.Add(CloneNode(n, ids.Contains(n.Parent ?? "") ? n.Parent : null));
        foreach (var l in map.Links)
            if (ids.Contains(l.From) && ids.Contains(l.To)) frag.Links.Add(CloneLink(l));
        return frag;
    }

    /// <summary>
    /// Wkleja fragment z nowymi id. Gdy podano <paramref name="center"/>, fragment ląduje środkiem w tym punkcie,
    /// w przeciwnym razie przesunięty o <paramref name="offset"/>. Zwraca wklejone kafelki.
    /// </summary>
    public static List<Node> Paste(MindMap map, MapFragment fragment, Vec? center = null, Vec? offset = null)
    {
        var map2 = new Dictionary<string, string>();
        foreach (var n in fragment.Nodes) map2[n.Id] = Ids.New();
        Vec shift = offset ?? new Vec(32, 32);
        if (center is { } c && fragment.Nodes.Count > 0)
        {
            double minX = fragment.Nodes.Min(n => n.X), maxX = fragment.Nodes.Max(n => n.X);
            double minY = fragment.Nodes.Min(n => n.Y), maxY = fragment.Nodes.Max(n => n.Y);
            shift = c - new Vec((minX + maxX) / 2, (minY + maxY) / 2);
        }
        var added = new List<Node>();
        foreach (var n in fragment.Nodes)
        {
            var copy = CloneNode(n, n.Parent is not null && map2.TryGetValue(n.Parent, out var np) ? np : null);
            copy.Id = map2[n.Id];
            copy.X = Math.Round(n.X + shift.X);
            copy.Y = Math.Round(n.Y + shift.Y);
            map.Nodes.Add(copy);
            added.Add(copy);
        }
        foreach (var l in fragment.Links)
        {
            if (!map2.TryGetValue(l.From, out var f) || !map2.TryGetValue(l.To, out var t)) continue;
            var copy = CloneLink(l);
            copy.Id = Ids.New();
            copy.From = f;
            copy.To = t;
            if (copy.Via is { } via) copy.Via = via.Select(p => p + shift).ToList();
            map.Links.Add(copy);
        }
        return added;
    }

    public static Node CloneNode(Node n, string? parent) => new()
    {
        Id = n.Id, Text = n.Text, X = n.X, Y = n.Y, Parent = parent, Style = n.Style?.Clone(),
        Note = n.Note, Collapsed = n.Collapsed, Width = n.Width, Icons = n.Icons?.ToList(), Link = n.Link,
    };

    public static Link CloneLink(Link l) => new()
    {
        Id = l.Id, From = l.From, To = l.To, Label = l.Label, Style = l.Style?.Clone(), C1 = l.C1, C2 = l.C2,
        Via = l.Via?.ToList(),
    };

    // ------------------------------------------------------------------ styl

    /// <summary>Nakłada gotowy styl (zachowując kolor gałęzi).</summary>
    public static void ApplyPreset(Node node, StylePreset preset)
    {
        var branch = node.Style?.BranchColor;
        var s = preset.Style.Clone();
        s.BranchColor = branch;
        node.Style = s.IsEmpty ? null : s;
    }

    /// <summary>Zmienia styl kafelka i sprząta pusty obiekt stylu.</summary>
    public static void EditStyle(Node node, Action<NodeStyle> change)
    {
        var s = node.Style ?? new NodeStyle();
        change(s);
        node.Style = s.IsEmpty ? null : s;
    }

    public static void EditStyle(Link link, Action<LinkStyle> change)
    {
        var s = link.Style ?? new LinkStyle();
        change(s);
        link.Style = s.IsEmpty ? null : s;
    }

    // ------------------------------------------------------------------ nawigacja

    /// <summary>Najbliższy kafelek w danym kierunku (strzałki na klawiaturze).</summary>
    public static Node? Nearest(IEnumerable<Node> candidates, Node from, Direction dir)
    {
        Node? best = null;
        double bestScore = double.MaxValue;
        foreach (var n in candidates)
        {
            if (ReferenceEquals(n, from)) continue;
            double dx = n.X - from.X, dy = n.Y - from.Y;
            double primary = dir switch { Direction.Right => dx, Direction.Left => -dx, Direction.Down => dy, _ => -dy };
            double secondary = dir is Direction.Left or Direction.Right ? Math.Abs(dy) : Math.Abs(dx);
            if (primary <= 1) continue;
            double score = primary + secondary * 2.2;
            if (score < bestScore)
            {
                bestScore = score;
                best = n;
            }
        }
        return best;
    }

    // ------------------------------------------------------------------ naprawa po wczytaniu

    /// <summary>
    /// Doprowadza mapę do poprawnego stanu: unikalne id, istniejący rodzice bez pętli, strzałki tylko między
    /// istniejącymi kafelkami. Zwraca liczbę poprawek (0 = plik był w porządku).
    /// </summary>
    public static int Repair(MindMap map)
    {
        int fixes = 0;
        map.Nodes ??= [];
        map.Links ??= [];
        map.Nodes.RemoveAll(n => n is null);
        map.Links.RemoveAll(l => l is null);
        var seen = new HashSet<string>();
        foreach (var n in map.Nodes)
        {
            n.Text ??= "";
            if (string.IsNullOrEmpty(n.Id) || !seen.Add(n.Id))
            {
                n.Id = Ids.New();
                seen.Add(n.Id);
                fixes++;
            }
            if (!double.IsFinite(n.X) || !double.IsFinite(n.Y))
            {
                n.X = n.Y = 0;
                fixes++;
            }
        }
        foreach (var n in map.Nodes)
        {
            if (n.Parent is not null && (n.Parent == n.Id || !seen.Contains(n.Parent)))
            {
                n.Parent = null;
                fixes++;
            }
        }
        // pętle w drzewie (A→B→A) — przecinamy
        var byId = map.Nodes.ToDictionary(n => n.Id);
        foreach (var n in map.Nodes)
        {
            var visited = new HashSet<string> { n.Id };
            var cur = n;
            while (cur.Parent is not null && byId.TryGetValue(cur.Parent, out var p))
            {
                if (!visited.Add(p.Id))
                {
                    cur.Parent = null;
                    fixes++;
                    break;
                }
                cur = p;
            }
        }
        var linkIds = new HashSet<string>();
        fixes += map.Links.RemoveAll(l => l.From == l.To || !seen.Contains(l.From) || !seen.Contains(l.To));
        foreach (var l in map.Links)
        {
            if (string.IsNullOrEmpty(l.Id) || !linkIds.Add(l.Id))
            {
                l.Id = Ids.New();
                linkIds.Add(l.Id);
                fixes++;
            }
            if (l.Via is { Count: 0 }) l.Via = null;
        }
        return fixes;
    }
}
