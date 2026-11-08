namespace MapoMyslacz.Tests;

/// <summary>Automatyczny układ gałęzi, wymiary kafelków, kolory i style.</summary>
public class LayoutAndStyleTests
{
    private static readonly NodeSizer Size = MapOps.EstimatingSizer;

    private static MindMap Wide()
    {
        var map = new MindMap();
        map.Nodes.Add(new Node { Id = "r", Text = "Temat" });
        for (int i = 0; i < 6; i++)
        {
            map.Nodes.Add(new Node { Id = $"c{i}", Text = $"Gałąź {i}", Parent = "r", X = 50, Y = i });
            for (int j = 0; j < i; j++) map.Nodes.Add(new Node { Id = $"c{i}-{j}", Text = $"Liść {j}", Parent = $"c{i}", X = 60, Y = j });
        }
        return map;
    }

    [Fact]
    public void ArrangeAll_NoOverlaps_RootStays()
    {
        var map = Wide();
        TreeLayout.ArrangeAll(map, Size);
        Assert.Equal(0, map.Get("r").X);
        Assert.Equal(0, map.Get("r").Y);
        var boxes = map.Nodes.Select(n => (n.Id, Box: TestData.BoxOf(n))).ToList();
        foreach (var a in boxes)
            foreach (var b in boxes)
                if (string.CompareOrdinal(a.Id, b.Id) < 0)
                    Assert.False(a.Box.Intersects(b.Box), $"{a.Id} nachodzi na {b.Id}");
    }

    [Fact]
    public void ArrangeAll_RootSplitsIntoBothSides_ChildrenOutwards()
    {
        var map = Wide();
        TreeLayout.ArrangeAll(map, Size);
        var idx = new MapIndex(map);
        var kids = idx.ChildrenOf("r");
        Assert.Contains(kids, k => k.X > 0);
        Assert.Contains(kids, k => k.X < 0);
        foreach (var k in kids)
            foreach (var g in idx.ChildrenOf(k.Id))
                Assert.Equal(Math.Sign(k.X), Math.Sign(g.X - k.X)); // wnuki dalej od środka
    }

    [Fact]
    public void Arrange_ForcedSide_KeepsOrderByY()
    {
        var map = Wide();
        TreeLayout.Arrange(map, "r", Size, side: 1);
        var kids = new MapIndex(map).ChildrenOf("r").OrderBy(k => k.Y).Select(k => k.Id).ToList();
        Assert.Equal(["c0", "c1", "c2", "c3", "c4", "c5"], kids);
        Assert.All(new MapIndex(map).ChildrenOf("r"), k => Assert.True(k.X > 0));
    }

    [Fact]
    public void Arrange_SkipsCollapsedSubtrees()
    {
        var map = Wide();
        map.Get("c5").Collapsed = true;
        var leaf = map.Get("c5-0");
        TreeLayout.ArrangeAll(map, Size);
        Assert.Equal(60, leaf.X); // ukryty liść nie został ruszony
    }

    [Fact]
    public void Metrics_ShapesNeedRoomForText()
    {
        var (rw, rh) = NodeMetrics.OuterSize(NodeShape.Rounded, 15, 100, 20);
        var (dw, dh) = NodeMetrics.OuterSize(NodeShape.Diamond, 15, 100, 20);
        var (ew, eh) = NodeMetrics.OuterSize(NodeShape.Ellipse, 15, 100, 20);
        Assert.True(dw > rw && dh > rh);
        Assert.True(ew > 100 && eh > 20);
        // narożnik prostokąta tekstu mieści się w rombie: x/a + y/b ≤ 1
        Assert.True(50 / (dw / 2) + 10 / (dh / 2) <= 1.0001);
    }

    [Fact]
    public void Metrics_LongTextWraps()
    {
        var shortText = NodeMetrics.EstimateText("Krótki", 15, false, 240);
        var longText = NodeMetrics.EstimateText(string.Join(' ', Enumerable.Repeat("słowo", 40)), 15, false, 240);
        Assert.Equal(NodeMetrics.LineHeight(15), shortText.H);
        Assert.True(longText.H >= 4 * NodeMetrics.LineHeight(15));
        Assert.True(longText.W <= 240);
        var twoLines = NodeMetrics.EstimateText("a\nb", 15, false, 240);
        Assert.Equal(2 * NodeMetrics.LineHeight(15), twoLines.H);
    }

    [Theory]
    [InlineData("#FF8000", 255, 128, 0, 255)]
    [InlineData("ff8000", 255, 128, 0, 255)]
    [InlineData("#F80", 255, 136, 0, 255)]
    [InlineData("#00000066", 0, 0, 0, 0x66)]
    public void Rgba_Parse(string text, int r, int g, int b, int a)
    {
        Assert.True(Rgba.TryParse(text, out var c));
        Assert.Equal(new Rgba((byte)r, (byte)g, (byte)b, (byte)a), c);
    }

    [Fact]
    public void Rgba_ToStringAndInvalid()
    {
        Assert.Equal("#15831C", new Rgba(0x15, 0x83, 0x1C).ToString());
        Assert.Equal("#00000066", new Rgba(0, 0, 0, 0x66).ToString());
        Assert.False(Rgba.TryParse("zielony", out _));
        Assert.False(Rgba.TryParse("#12345", out _));
        Assert.Equal(Rgba.White, Rgba.Parse(null, Rgba.White));
    }

    [Fact]
    public void Resolve_Defaults_AndAutoTextContrast()
    {
        var d = Styles.Resolve(null, Rgba.Black);
        Assert.Equal(NodeShape.Rounded, d.Shape);
        Assert.Equal(Styles.DefaultFontSize, d.FontSize);
        Assert.Equal(Styles.DarkText, d.TextColor);

        var dark = Styles.Resolve(new NodeStyle { Fill = "#7F00AC" }, Rgba.Black);
        Assert.Equal(Styles.LightText, dark.TextColor);

        var title = Styles.Resolve(new NodeStyle { Shape = NodeShape.None }, Rgba.Black, Styles.DarkCanvasBackground);
        Assert.Equal(Styles.LightText, title.TextColor); // tekst bez tła na ciemnym płótnie — jasny
        Assert.Equal(0, title.Fill.A);

        var own = Styles.Resolve(new NodeStyle { Fill = "#7F00AC", TextColor = "#FFFF00" }, Rgba.Black);
        Assert.Equal(Rgba.Parse("#FFFF00", Rgba.Black), own.TextColor);

        Assert.Equal(120, Styles.Resolve(new NodeStyle { FontSize = 1000 }, Rgba.Black).FontSize); // rozsądne granice
    }

    [Fact]
    public void Resolve_LinkDefaults()
    {
        var s = Styles.Resolve((LinkStyle?)null);
        Assert.True(s.ArrowEnd);
        Assert.False(s.ArrowStart);
        Assert.Equal(LinePattern.Dashed, s.Pattern);
        Assert.Equal(LinkCurve.Curved, s.Curve);
    }

    [Fact]
    public void BranchColor_FromPaletteByIndex_InheritedDown_Overridable()
    {
        var map = TestData.Tree();
        var idx = new MapIndex(map);
        Assert.Equal(Styles.BranchPalette[0], Styles.BranchColorFor(idx, map.Get("a")));
        Assert.Equal(Styles.BranchPalette[1], Styles.BranchColorFor(idx, map.Get("b")));
        Assert.Equal(Styles.BranchPalette[1], Styles.BranchColorFor(idx, map.Get("b1")));
        map.Get("b").Style = new NodeStyle { BranchColor = "#123456" };
        Assert.Equal(Rgba.Parse("#123456", Rgba.Black), Styles.BranchColorFor(idx, map.Get("b1")));
    }

    [Fact]
    public void Presets_HaveUniqueNames()
    {
        Assert.Equal(Styles.Presets.Count, Styles.Presets.Select(p => p.Name).Distinct().Count());
        Assert.All(Styles.Swatches, s => Assert.True(Rgba.TryParse(s, out _)));
    }
}
