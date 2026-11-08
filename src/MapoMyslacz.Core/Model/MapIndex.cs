namespace MapoMyslacz.Core.Model;

/// <summary>
/// Szybki dostęp do kafelków po id i do dzieci. Tworzony na nowo po każdej zmianie struktury
/// (mapy mają setki, nie miliony kafelków — to tanie).
/// </summary>
public sealed class MapIndex
{
    private readonly Dictionary<string, Node> _nodes;
    private readonly Dictionary<string, List<Node>> _children = [];
    private static readonly List<Node> NoChildren = [];

    public MindMap Map { get; }

    public MapIndex(MindMap map)
    {
        Map = map;
        _nodes = new Dictionary<string, Node>(map.Nodes.Count);
        foreach (var n in map.Nodes) _nodes.TryAdd(n.Id, n);
        foreach (var n in map.Nodes)
        {
            if (n.Parent is null || !_nodes.ContainsKey(n.Parent)) continue;
            if (!_children.TryGetValue(n.Parent, out var list)) _children[n.Parent] = list = [];
            list.Add(n);
        }
    }

    public Node? Get(string? id) => id is not null && _nodes.TryGetValue(id, out var n) ? n : null;

    public bool Contains(string id) => _nodes.ContainsKey(id);

    public IReadOnlyList<Node> ChildrenOf(string id) => _children.TryGetValue(id, out var l) ? l : NoChildren;

    public Node? ParentOf(Node n) => Get(n.Parent);

    /// <summary>Wszyscy potomkowie (bez samego kafelka), wszerz.</summary>
    public List<Node> Descendants(string id)
    {
        var result = new List<Node>();
        var queue = new Queue<string>();
        var seen = new HashSet<string> { id };
        queue.Enqueue(id);
        while (queue.Count > 0)
        {
            foreach (var c in ChildrenOf(queue.Dequeue()))
            {
                if (!seen.Add(c.Id)) continue;
                result.Add(c);
                queue.Enqueue(c.Id);
            }
        }
        return result;
    }

    /// <summary>Czy <paramref name="ancestorId"/> jest przodkiem (albo tym samym kafelkiem co) <paramref name="nodeId"/>.</summary>
    public bool IsAncestorOrSelf(string ancestorId, string nodeId)
    {
        var cur = Get(nodeId);
        int guard = 0;
        while (cur is not null && guard++ < 100_000)
        {
            if (cur.Id == ancestorId) return true;
            cur = Get(cur.Parent);
        }
        return false;
    }

    public int Depth(Node n)
    {
        int d = 0;
        var cur = ParentOf(n);
        while (cur is not null && d < 100_000)
        {
            d++;
            cur = ParentOf(cur);
        }
        return d;
    }

    /// <summary>Kafelek jest widoczny, jeśli żaden przodek nie jest zwinięty.</summary>
    public bool IsVisible(Node n)
    {
        var cur = ParentOf(n);
        int guard = 0;
        while (cur is not null && guard++ < 100_000)
        {
            if (cur.Collapsed) return false;
            cur = ParentOf(cur);
        }
        return true;
    }
}
