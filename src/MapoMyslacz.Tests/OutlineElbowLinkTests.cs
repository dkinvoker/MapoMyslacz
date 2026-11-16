namespace MapoMyslacz.Tests;

/// <summary>Konspekt Markdown, strzałki łamane, linki do kafelków i linki zwrotne.</summary>
public sealed class OutlineElbowLinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mapo-links-" + Guid.NewGuid().ToString("N"));

    public OutlineElbowLinkTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------ Markdown

    [Fact]
    public void Markdown_Write_HeadingsListsNotesLinksMarkers()
    {
        var map = TestData.Tree();
        map.Title = "Kampania";
        map.Get("b").Note = "Pierwsza linia\nDruga";
        map.Get("b1").Link = "Sesja 2.mapo";
        map.Get("a").Icons = ["priority-1", "progress-50", "🏰"];
        MapOps.AddFrame(map, ["a", "b"], "Okolica");
        var md = MarkdownOutline.Write(map);

        Assert.StartsWith("# Kampania", md);
        Assert.Contains("## Root", md);
        Assert.Contains("- P1 [50%] 🏰 A", md);
        Assert.Contains("- B\n  > Pierwsza linia\n  > Druga".Replace("\n", Environment.NewLine), md);
        Assert.Contains("  - [B1](<Sesja 2.mapo>)", md);
        Assert.Contains("## Wolny", md);
        Assert.Contains("- A → Wolny (zna)", md);
        Assert.Contains("- Okolica: A; B", md);
    }

    [Fact]
    public void Markdown_RoundTrip_KeepsStructureNotesLinksArrowsFrames()
    {
        var map = TestData.Tree();
        map.Title = "Kampania";
        map.Get("b").Note = "Notatka";
        map.Get("b1").Link = "https://example.com";
        map.Get("a").Icons = ["priority-2", "🏰"];
        MapOps.AddFrame(map, ["a", "b"], "Okolica");

        var back = MarkdownOutline.Parse(MarkdownOutline.Write(map));
        var idx = new MapIndex(back);
        Node N(string text) => back.Nodes.Single(n => n.Text == text);

        Assert.Equal("Kampania", back.Title);
        Assert.Equal(5, back.Nodes.Count);
        Assert.Equal("Root", idx.ParentOf(N("A"))!.Text);
        Assert.Equal("B", idx.ParentOf(N("B1"))!.Text);
        Assert.Null(N("Wolny").Parent);
        Assert.Equal("Notatka", N("B").Note);
        Assert.Equal("https://example.com", N("B1").Link);
        Assert.Equal(["priority-2", "🏰"], N("A").Icons);
        var link = Assert.Single(back.Links);
        Assert.Equal((N("A").Id, N("Wolny").Id, "zna"), (link.From, link.To, link.Label));
        var frame = Assert.Single(back.Frames!);
        Assert.Equal("Okolica", frame.Title);
        Assert.Equal([N("A").Id, N("B").Id], frame.Nodes);
    }

    [Fact]
    public void Markdown_Parse_ListUnderHeadingsAndPlainIndentedText()
    {
        var map = MarkdownOutline.Parse("""
            ## Wioska
            - Sołtys
              - Córka
            * Karczma
            ## Las
            Zwykły akapit — notatka Lasu
            """);
        var idx = new MapIndex(map);
        Node N(string t) => map.Nodes.Single(n => n.Text == t);
        Assert.Null(map.Title);
        Assert.Equal("Wioska", idx.ParentOf(N("Sołtys"))!.Text);
        Assert.Equal("Sołtys", idx.ParentOf(N("Córka"))!.Text);
        Assert.Equal("Wioska", idx.ParentOf(N("Karczma"))!.Text);
        Assert.Equal("Zwykły akapit — notatka Lasu", N("Las").Note);

        var plain = MarkdownOutline.Parse("Temat\n\tGałąź\n\t\tLiść\nDrugi temat");
        Assert.Equal(2, plain.Nodes.Count(n => n.Parent is null));
        Assert.Equal("Gałąź", new MapIndex(plain).ParentOf(plain.Nodes.Single(n => n.Text == "Liść"))!.Text);
    }

    // ------------------------------------------------------------------ łamane

    private static NodeOutline Rect(double x, double y) => new(NodeShape.Rectangle, Box.FromCenter(x, y, 100, 40));

    [Fact]
    public void Elbow_SideBySide_GoesHorizontalVerticalHorizontal()
    {
        var link = new Link();
        var curves = LinkRouting.Elbow(link, Rect(0, 0), Rect(300, 200));
        Assert.Equal(new Vec(50, 0), curves[0].P0);          // z prawego boku
        Assert.Equal(new Vec(250, 200), curves[^1].P3);      // do lewego boku celu
        var path = LinkRouting.Route(link, LinkCurve.Elbow, Rect(0, 0), Rect(300, 200));
        Assert.Equal(150, path.LabelAt.X, 3);                 // podpis na środkowym, pionowym odcinku
        Assert.Equal(100, path.LabelAt.Y, 3);
        Assert.Null(path.Handle1);

        link.C1 = new Vec(40, 999);                           // przesunięty środek (Y się nie liczy)
        path = LinkRouting.Route(link, LinkCurve.Elbow, Rect(0, 0), Rect(300, 200));
        Assert.Equal(190, path.LabelAt.X, 3);
    }

    [Fact]
    public void Elbow_AboveEachOther_GoesVertical_AndStraightWhenAligned()
    {
        var curves = LinkRouting.Elbow(new Link(), Rect(0, 0), Rect(30, 300));
        Assert.Equal(new Vec(0, 20), curves[0].P0);           // z dołu
        Assert.Equal(new Vec(30, 280), curves[^1].P3);        // do góry celu
        Assert.Single(LinkRouting.Elbow(new Link(), Rect(0, 0), Rect(300, 0))); // w jednej linii — prosta
    }

    [Fact]
    public void Reverse_KeepsElbowOffset()
    {
        var link = new Link { From = "a", To = "b", C1 = new Vec(30, 0), Style = new LinkStyle { Curve = LinkCurve.Elbow } };
        MapOps.Reverse(link);
        Assert.Equal(new Vec(30, 0), link.C1);
    }

    // ------------------------------------------------------------------ linki do kafelków

    [Fact]
    public void Resolve_NodeLinks()
    {
        var map = Path.Combine(_dir, "Sesja 1.mapo");
        var same = LinkTargets.Resolve("#abc123", map);
        Assert.Equal((LinkKind.Node, "abc123"), (same.Kind, same.NodeId));

        var other = LinkTargets.Resolve("Sesja 2.mapo#x_y-1", map);
        Assert.Equal(LinkKind.Map, other.Kind);
        Assert.Equal(Path.Combine(_dir, "Sesja 2.mapo"), other.Target);
        Assert.Equal("x_y-1", other.NodeId);

        // krzyżyk w nazwie zwykłego pliku to część nazwy
        var file = LinkTargets.Resolve("notatki #2.txt", map);
        Assert.Equal((LinkKind.File, (string?)null), (file.Kind, file.NodeId));
        Assert.Equal(LinkKind.Web, LinkTargets.Resolve("https://x.pl/strona#rozdzial", map).Kind);
    }

    [Fact]
    public void Normalize_AbsoluteToRelative_AndSameMapToHash()
    {
        var map = Path.Combine(_dir, "Sesja 1.mapo");
        Assert.Equal("#abc", LinkTargets.Normalize(LinkTargets.ForNode("abc", map), map));
        Assert.Equal("Sesja 2.mapo#abc", LinkTargets.Normalize(Path.Combine(_dir, "Sesja 2.mapo") + "#abc", map));
        Assert.Equal(Path.Combine("sub", "plik.pdf"), LinkTargets.Normalize(Path.Combine(_dir, "sub", "plik.pdf"), map));
        Assert.Equal("https://x.pl", LinkTargets.Normalize(" https://x.pl ", map));
        Assert.Equal("#abc", LinkTargets.ForNode("abc", null));
    }

    [Fact]
    public void Describe_NodeLinks()
    {
        Assert.Equal("kafelek w tej mapie", LinkTargets.Describe("#abc"));
        Assert.Equal("Sesja 2.mapo → kafelek", LinkTargets.Describe(@"..\Sesja 2.mapo#abc"));
    }

    [Fact]
    public void Backlinks_FindsMapsLinkingHere()
    {
        string P(string name) => Path.Combine(_dir, name);
        MapSerializer.Save(new MindMap { Nodes = [new Node { Id = "x", Text = "Wróć" }] }, P("Cel.mapo"));
        MapSerializer.Save(new MindMap { Nodes = [new Node { Id = "a", Text = "Do celu", Link = "Cel.mapo" }, new Node { Id = "b", Text = "Gdzie indziej", Link = "Inna.mapo" }] }, P("Źródło.mapo"));
        MapSerializer.Save(new MindMap { Nodes = [new Node { Id = "k", Text = "Do kafelka", Link = P("Cel.mapo") + "#x" }] }, P("Atlas.mapo"));
        File.WriteAllText(P("Zepsuta.mapo"), "{ to nie json");

        var found = Backlinks.Find(P("Cel.mapo"));
        Assert.Equal(["Atlas.mapo", "Źródło.mapo"], found.Select(b => Path.GetFileName(b.MapPath)));
        Assert.Equal(("k", "x"), (found[0].NodeId, found[0].TargetNodeId));
        Assert.Equal("Do celu", found[1].NodeText);
    }
}
