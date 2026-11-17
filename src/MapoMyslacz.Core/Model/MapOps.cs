namespace MapoMyslacz.Core.Model;

/// <summary>Rozmiar kafelka (szerokość, wysokość) — w aplikacji zmierzony, w testach i imporcie szacowany.</summary>
public delegate Vec NodeSizer(Node node);

public enum Direction { Left, Right, Up, Down }

/// <summary>Do czego wyrównać zaznaczone kafelki.</summary>
public enum AlignMode { Left, CenterX, Right, Top, CenterY, Bottom }

/// <summary>Kawałek mapy (kafelki + strzałki między nimi) — do schowka, duplikowania i wklejania konspektu.</summary>
public sealed class MapFragment
{
    public const string FormatName = "MapoMyslacz-fragment";

    /// <summary>Znacznik — tylko z nim JSON ze schowka jest traktowany jako kafelki MapoMyślacza.</summary>
    public string? Format { get; set; }
    public List<Node> Nodes { get; set; } = [];
    public List<Link> Links { get; set; } = [];

    /// <summary>Ramki, których wszystkie kafelki są w kawałku.</summary>
    public List<Frame>? Frames { get; set; }

    /// <summary>Obrazki kafelków z kawałka (żeby wklejenie do innej mapy też je miało).</summary>
    public Dictionary<string, string>? Images { get; set; }
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
        PlaceBeside(node, parent, children, side, size);
        if (parent.Collapsed) parent.Collapsed = false;
        map.Nodes.Add(node);
        return node;
    }

    /// <summary>
    /// Nowy kafelek połączony strzałką z <paramref name="fromId"/> zamiast gałęzi (opcja „dzieci jako strzałki”) —
    /// stoi tam, gdzie stanęłoby dziecko: obok, pod ostatnim z dzieci i kafelków, do których już prowadzą strzałki.
    /// Rośnie w tę stronę, w którą rośnie sam <paramref name="fromId"/> (od rodzica albo od kafelka, z którego
    /// prowadzi do niego strzałka).
    /// </summary>
    public static Node AddLinkedChild(MindMap map, string fromId, NodeSizer size, string text = DefaultText)
    {
        var index = new MapIndex(map);
        var from = index.Get(fromId) ?? throw new ArgumentException("Nie ma takiego kafelka.", nameof(fromId));
        var children = index.ChildrenOf(fromId)
            .Concat(map.Links.Where(l => l.From == fromId).Select(l => index.Get(l.To)).OfType<Node>())
            .Distinct().ToList();

        int side;
        if (index.ParentOf(from) is not null) side = SideOf(index, from);
        else if (ArrowParent(map, index, from) is { } source) side = from.X < source.X ? -1 : 1;
        else
        {
            int right = children.Count(c => c.X >= from.X), left = children.Count - right;
            side = right <= left ? 1 : -1;
        }

        var node = new Node { Text = text };
        PlaceBeside(node, from, children, side, size);
        map.Nodes.Add(node);
        AddLink(map, fromId, node.Id);
        return node;
    }

    /// <summary>
    /// Rodzeństwo w trybie „dzieci jako strzałki”: kafelek w drzewie dostaje zwykłe rodzeństwo, a kafelek, do którego
    /// prowadzi strzałka — kolejny kafelek połączony strzałką z tym samym źródłem.
    /// </summary>
    public static Node AddLinkedSibling(MindMap map, string nodeId, NodeSizer size, string text = DefaultText)
    {
        var index = new MapIndex(map);
        var node = index.Get(nodeId) ?? throw new ArgumentException("Nie ma takiego kafelka.", nameof(nodeId));
        if (node.Parent is null && ArrowParent(map, index, node) is { } source) return AddLinkedChild(map, source.Id, size, text);
        return AddSibling(map, nodeId, size, text);
    }

    /// <summary>Kafelek, z którego prowadzi pierwsza strzałka do <paramref name="node"/> (jego „rodzic przez strzałkę”).</summary>
    private static Node? ArrowParent(MindMap map, MapIndex index, Node node) =>
        map.Links.Where(l => l.To == node.Id && l.From != node.Id).Select(l => index.Get(l.From)).OfType<Node>().FirstOrDefault();

    /// <summary>Ustawia kafelek obok rodzica, pod najniższym z rodzeństwa po stronie <paramref name="side"/>.</summary>
    private static void PlaceBeside(Node node, Node parent, IReadOnlyList<Node> children, int side, NodeSizer size)
    {
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

    /// <summary>
    /// Usuwa kafelki razem z ich potomkami, wskazane strzałki (i strzałki prowadzące do usuniętych) oraz wskazane
    /// ramki (same ramki — ich kafelki zostają). Ramki, w których nie został żaden kafelek, też znikają.
    /// </summary>
    public static int Delete(MindMap map, IEnumerable<string> nodeIds, IEnumerable<string>? linkIds = null, IEnumerable<string>? frameIds = null)
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
        if (map.Frames is { } frames)
        {
            var removeFrames = new HashSet<string>(frameIds ?? []);
            foreach (var f in frames) f.Nodes.RemoveAll(remove.Contains);
            count += frames.RemoveAll(f => removeFrames.Contains(f.Id) || f.Nodes.Count == 0);
            if (frames.Count == 0) map.Frames = null;
        }
        return count;
    }

    // ------------------------------------------------------------------ ramki

    /// <summary>Nowa ramka wokół kafelków (pomija nieistniejące; null, gdy nie ma żadnego).</summary>
    public static Frame? AddFrame(MindMap map, IEnumerable<string> nodeIds, string? title = "Grupa")
    {
        var index = new MapIndex(map);
        var ids = nodeIds.Distinct().Where(index.Contains).ToList();
        if (ids.Count == 0) return null;
        var frame = new Frame { Title = title, Nodes = ids };
        (map.Frames ??= []).Add(frame);
        return frame;
    }

    /// <summary>Dokłada kafelki do ramki (bez powtórzeń).</summary>
    public static void AddToFrame(Frame frame, IEnumerable<string> nodeIds)
    {
        foreach (var id in nodeIds)
            if (!frame.Nodes.Contains(id)) frame.Nodes.Add(id);
    }

    /// <summary>Wyjmuje kafelki z ramek; ramka bez kafelków znika.</summary>
    public static void RemoveFromFrames(MindMap map, IEnumerable<string> nodeIds)
    {
        if (map.Frames is not { } frames) return;
        var ids = nodeIds.ToHashSet();
        foreach (var f in frames) f.Nodes.RemoveAll(ids.Contains);
        frames.RemoveAll(f => f.Nodes.Count == 0);
        if (frames.Count == 0) map.Frames = null;
    }

    /// <summary>Kafelki należące do ramki razem z ich potomkami (to, co ramka obejmuje).</summary>
    public static HashSet<string> FrameContent(MapIndex index, Frame frame) => MovingSet(index, frame.Nodes, withDescendants: true);

    public static Frame CloneFrame(Frame f) => new() { Id = f.Id, Title = f.Title, Nodes = f.Nodes.ToList(), Color = f.Color };

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

    /// <summary>
    /// Zamienia gałąź drzewa (rodzic → <paramref name="childId"/>) na zwykłą strzałkę w tym samym kolorze: kafelek
    /// zostaje na miejscu jako swobodny, a strzałkę można już zaznaczać, wyginać i podpisywać.
    /// </summary>
    public static Link? BranchToLink(MindMap map, string childId)
    {
        var index = new MapIndex(map);
        if (index.Get(childId) is not { Parent: { } parentId } child || index.Get(parentId) is null) return null;
        var color = Styles.BranchColorFor(index, child).ToString();
        child.Parent = null;
        var link = AddLink(map, parentId, childId) ?? map.Links.FirstOrDefault(l => l.From == parentId && l.To == childId);
        if (link is not null) EditStyle(link, s => { s.Color = color; s.Pattern = LinePattern.Solid; });
        return link;
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
        // łamana: C1 to przesunięcie środkowego odcinka w układzie mapy — zostaje bez zmian
        if (link.Style?.Curve != LinkCurve.Elbow) (link.C1, link.C2) = (link.C2, link.C1);
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
        foreach (var f in map.Frames ?? [])
            if (f.Nodes.Count > 0 && f.Nodes.All(ids.Contains)) (frag.Frames ??= []).Add(CloneFrame(f));
        frag.Images = MapImages.Referenced(frag.Nodes, map.Images);
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
        // obrazki z innej mapy trafiają do magazynu tej (id z zawartości — powtórki się nie dublują)
        if (fragment.Images is { Count: > 0 } images)
        {
            var store = map.Images ??= [];
            foreach (var (id, data) in images) store.TryAdd(id, data);
        }
        foreach (var n in fragment.Nodes)
        {
            var copy = CloneNode(n, n.Parent is not null && map2.TryGetValue(n.Parent, out var np) ? np : null);
            if (copy.Image is { } img && map.Images?.ContainsKey(img.Id) != true) copy.Image = null;
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
        foreach (var f in fragment.Frames ?? [])
        {
            var nodes = f.Nodes.Where(map2.ContainsKey).Select(id => map2[id]).ToList();
            if (nodes.Count == 0) continue;
            var copy = CloneFrame(f);
            copy.Id = Ids.New();
            copy.Nodes = nodes;
            (map.Frames ??= []).Add(copy);
        }
        return added;
    }

    public static Node CloneNode(Node n, string? parent) => new()
    {
        Id = n.Id, Text = n.Text, X = n.X, Y = n.Y, Parent = parent, Style = n.Style?.Clone(),
        Note = n.Note, Collapsed = n.Collapsed, Width = n.Width, Icons = n.Icons?.ToList(), Link = n.Link,
        Image = n.Image?.Clone(),
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

    // ------------------------------------------------------------------ wyrównywanie

    /// <summary>
    /// Wyrównuje kafelki do krawędzi albo środka całego zaznaczenia. Gałęzie jadą razem z kafelkami (jak przy
    /// przeciąganiu). Zwraca false, gdy nic się nie zmieniło.
    /// </summary>
    public static bool Align(MindMap map, IEnumerable<string> ids, AlignMode mode, NodeSizer size)
    {
        var idx = new MapIndex(map);
        var nodes = ids.Distinct().Select(idx.Get).OfType<Node>().ToList();
        if (nodes.Count < 2) return false;
        var boxes = nodes.ToDictionary(n => n.Id, n => BoxOf(n, size));
        var all = Box.Union(boxes.Values)!.Value;
        var targets = new Dictionary<string, Vec>();
        foreach (var n in nodes)
        {
            var b = boxes[n.Id];
            targets[n.Id] = mode switch
            {
                AlignMode.Left => new Vec(all.Left + b.Width / 2, n.Y),
                AlignMode.CenterX => new Vec(all.Center.X, n.Y),
                AlignMode.Right => new Vec(all.Right - b.Width / 2, n.Y),
                AlignMode.Top => new Vec(n.X, all.Top + b.Height / 2),
                AlignMode.CenterY => new Vec(n.X, all.Center.Y),
                _ => new Vec(n.X, all.Bottom - b.Height / 2),
            };
        }
        return MoveTo(idx, targets);
    }

    /// <summary>Rozkłada kafelki równo (jednakowe odstępy między nimi); skrajne zostają na miejscu.</summary>
    public static bool Distribute(MindMap map, IEnumerable<string> ids, bool horizontal, NodeSizer size)
    {
        var idx = new MapIndex(map);
        var nodes = ids.Distinct().Select(idx.Get).OfType<Node>().ToList();
        if (nodes.Count < 3) return false;
        var boxes = nodes.ToDictionary(n => n.Id, n => BoxOf(n, size));
        var sorted = nodes.OrderBy(n => horizontal ? n.X : n.Y).ToList();
        var first = boxes[sorted[0].Id];
        var last = boxes[sorted[^1].Id];
        double span = horizontal ? last.Right - first.Left : last.Bottom - first.Top;
        double sum = sorted.Sum(n => horizontal ? boxes[n.Id].Width : boxes[n.Id].Height);
        double gap = (span - sum) / (sorted.Count - 1);
        double pos = horizontal ? first.Left : first.Top;
        var targets = new Dictionary<string, Vec>();
        foreach (var n in sorted)
        {
            var b = boxes[n.Id];
            targets[n.Id] = horizontal ? new Vec(pos + b.Width / 2, n.Y) : new Vec(n.X, pos + b.Height / 2);
            pos += (horizontal ? b.Width : b.Height) + gap;
        }
        return MoveTo(idx, targets);
    }

    private static Box BoxOf(Node n, NodeSizer size)
    {
        var s = size(n);
        return Box.FromCenter(n.X, n.Y, s.X, s.Y);
    }

    /// <summary>
    /// Ustawia kafelki w docelowych miejscach, przesuwając ich gałęzie. Najpierw przodkowie — wtedy zaznaczony
    /// potomek, którego przesunął przodek, i tak trafia dokładnie tam, gdzie powinien.
    /// </summary>
    private static bool MoveTo(MapIndex idx, Dictionary<string, Vec> targets)
    {
        bool changed = false;
        foreach (var id in targets.Keys.OrderBy(id => idx.Depth(idx.Get(id)!)))
        {
            var n = idx.Get(id)!;
            var t = targets[id];
            var d = new Vec(Math.Round(t.X - n.X), Math.Round(t.Y - n.Y));
            if (d == Vec.Zero) continue;
            changed = true;
            foreach (var m in MovingSet(idx, [id], withDescendants: true))
            {
                var node = idx.Get(m)!;
                node.X += d.X;
                node.Y += d.Y;
            }
        }
        return changed;
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
            // obrazek bez danych (sprawdzamy tylko, gdy mapa ma magazyn — migawki historii cofania go nie mają)
            if (n.Image is { } img && (string.IsNullOrEmpty(img.Id) || img.Width <= 0 || img.Height <= 0
                                       || (map.Images is { } store && !store.ContainsKey(img.Id))))
            {
                n.Image = null;
                fixes++;
            }
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
        if (map.Frames is { } frames)
        {
            frames.RemoveAll(f => f is null);
            var frameIds = new HashSet<string>();
            foreach (var f in frames)
            {
                f.Nodes ??= [];
                int before = f.Nodes.Count;
                f.Nodes = f.Nodes.Where(seen.Contains).Distinct().ToList();
                if (f.Nodes.Count != before) fixes++;
                if (string.IsNullOrEmpty(f.Id) || !frameIds.Add(f.Id))
                {
                    f.Id = Ids.New();
                    frameIds.Add(f.Id);
                    fixes++;
                }
            }
            fixes += frames.RemoveAll(f => f.Nodes.Count == 0);
            if (frames.Count == 0) map.Frames = null;
        }
        // arkusze: każdy naprawiamy osobno (obrazki sprawdzamy z magazynu pliku, który jest w pierwszym arkuszu)
        if (map.Sheets is { } sheets)
        {
            sheets.RemoveAll(s => s is null);
            foreach (var sheet in sheets)
            {
                sheet.Sheets = null;
                sheet.Images = map.Images;
                fixes += Repair(sheet);
                sheet.Images = null;
                sheet.Format = MindMap.FormatName;
            }
            if (sheets.Count == 0) map.Sheets = null;
        }
        return fixes;
    }
}
