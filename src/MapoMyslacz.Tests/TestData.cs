namespace MapoMyslacz.Tests;

/// <summary>Małe mapy do testów.</summary>
internal static class TestData
{
    /// <summary>Temat główny „Root” z dziećmi A, B (B ma dziecko B1) i swobodny kafelek „Wolny”; strzałka A → Wolny.</summary>
    public static MindMap Tree()
    {
        var map = new MindMap();
        map.Nodes.Add(new Node { Id = "root", Text = "Root", X = 0, Y = 0 });
        map.Nodes.Add(new Node { Id = "a", Text = "A", Parent = "root", X = 200, Y = -40 });
        map.Nodes.Add(new Node { Id = "b", Text = "B", Parent = "root", X = 200, Y = 40 });
        map.Nodes.Add(new Node { Id = "b1", Text = "B1", Parent = "b", X = 360, Y = 40 });
        map.Nodes.Add(new Node { Id = "free", Text = "Wolny", X = -300, Y = 200 });
        map.Links.Add(new Link { Id = "l1", From = "a", To = "free", Label = "zna" });
        return map;
    }

    public static Node Get(this MindMap map, string id) => map.Nodes.Single(n => n.Id == id);

    public static Box BoxOf(Node n) => BoxOf(n, MapOps.EstimatingSizer(n));

    public static Box BoxOf(Node n, Vec size) => Box.FromCenter(n.X, n.Y, size.X, size.Y);
}
