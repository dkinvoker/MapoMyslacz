namespace MapoMyslacz.Tests;

/// <summary>Operacje edycyjne: dodawanie, usuwanie, przenoszenie, schowek, style, nawigacja.</summary>
public class MapOpsTests
{
    private static readonly NodeSizer Size = MapOps.EstimatingSizer;

    [Fact]
    public void AddChild_GoesRightOfParent_BelowLastChild()
    {
        var map = TestData.Tree();
        var b = map.Get("b");
        var child = MapOps.AddChild(map, "b", Size);
        Assert.Equal("b", child.Parent);
        Assert.True(child.X > b.X, "dziecko powinno być po prawej");
        var b1 = map.Get("b1");
        Assert.True(child.Y > b1.Y, "nowe dziecko pod istniejącym");
        Assert.False(TestData.BoxOf(child).Intersects(TestData.BoxOf(b1)), "bez nachodzenia na rodzeństwo");
    }

    [Fact]
    public void AddChild_OfLeftBranch_GoesLeft()
    {
        var map = TestData.Tree();
        map.Nodes.Add(new Node { Id = "l", Text = "Lewa", Parent = "root", X = -200, Y = 0 });
        var child = MapOps.AddChild(map, "l", Size);
        Assert.True(child.X < -200);
    }

    [Fact]
    public void AddChild_OfRoot_BalancesSides()
    {
        var map = new MindMap();
        map.Nodes.Add(new Node { Id = "r", Text = "Środek" });
        var sides = Enumerable.Range(0, 4).Select(_ => Math.Sign(MapOps.AddChild(map, "r", Size).X)).ToList();
        Assert.Equal(2, sides.Count(s => s > 0));
        Assert.Equal(2, sides.Count(s => s < 0));
    }

    [Fact]
    public void AddChild_ExpandsCollapsedParent()
    {
        var map = TestData.Tree();
        map.Get("b").Collapsed = true;
        MapOps.AddChild(map, "b", Size);
        Assert.False(map.Get("b").Collapsed);
    }

    [Fact]
    public void AddSibling_SameParent_BelowWholeSubtree()
    {
        var map = TestData.Tree();
        var s = MapOps.AddSibling(map, "a", Size);
        Assert.Equal("root", s.Parent);
        Assert.True(s.Y > map.Get("a").Y);

        var s2 = MapOps.AddSibling(map, "b", Size);
        Assert.True(s2.Y > map.Get("b1").Y, "pod całym poddrzewem B");
    }

    [Fact]
    public void AddSibling_OfFreeNode_CopiesStyle()
    {
        var map = TestData.Tree();
        map.Get("free").Style = new NodeStyle { Fill = "#FFF4B8" };
        var s = MapOps.AddSibling(map, "free", Size);
        Assert.Null(s.Parent);
        Assert.Equal("#FFF4B8", s.Style!.Fill);
        Assert.NotSame(map.Get("free").Style, s.Style);
    }

    [Fact]
    public void Delete_RemovesDescendantsAndTheirLinks()
    {
        var map = TestData.Tree();
        map.Links.Add(new Link { Id = "l2", From = "b1", To = "free" });
        MapOps.Delete(map, ["b"]);
        Assert.DoesNotContain(map.Nodes, n => n.Id is "b" or "b1");
        Assert.DoesNotContain(map.Links, l => l.Id == "l2");
        Assert.Contains(map.Links, l => l.Id == "l1");
    }

    [Fact]
    public void Delete_OnlyLinks()
    {
        var map = TestData.Tree();
        MapOps.Delete(map, [], ["l1"]);
        Assert.Empty(map.Links);
        Assert.Equal(5, map.Nodes.Count);
    }

    [Fact]
    public void Move_WithAndWithoutDescendants()
    {
        var map = TestData.Tree();
        MapOps.Move(map, ["b"], new Vec(10, 5));
        Assert.Equal(210, map.Get("b").X);
        Assert.Equal(370, map.Get("b1").X);
        MapOps.Move(map, ["b"], new Vec(10, 0), withDescendants: false);
        Assert.Equal(220, map.Get("b").X);
        Assert.Equal(370, map.Get("b1").X);
    }

    [Fact]
    public void Reparent_RefusesCycles()
    {
        var map = TestData.Tree();
        Assert.False(MapOps.Reparent(map, "b", "b1"));   // potomek nie może zostać rodzicem
        Assert.False(MapOps.Reparent(map, "b", "b"));
        Assert.True(MapOps.Reparent(map, "free", "a"));
        Assert.Equal("a", map.Get("free").Parent);
        Assert.True(MapOps.Reparent(map, "free", null));
        Assert.Null(map.Get("free").Parent);
    }

    [Fact]
    public void AddLink_NoSelfLinksNoDuplicates()
    {
        var map = TestData.Tree();
        Assert.Null(MapOps.AddLink(map, "a", "a"));
        Assert.Null(MapOps.AddLink(map, "a", "free")); // już jest
        Assert.Null(MapOps.AddLink(map, "a", "nie-ma"));
        var l = MapOps.AddLink(map, "free", "a", "  ");
        Assert.NotNull(l);
        Assert.Null(l!.Label);
    }

    [Fact]
    public void Reverse_SwapsEndsControlsAndArrows()
    {
        var l = new Link { From = "x", To = "y", C1 = new Vec(1, 1), C2 = new Vec(2, 2), Style = new LinkStyle { ArrowEnd = false, ArrowStart = true } };
        MapOps.Reverse(l);
        Assert.Equal(("y", "x"), (l.From, l.To));
        Assert.Equal(new Vec(2, 2), l.C1);
        Assert.Equal(new Vec(1, 1), l.C2);
        Assert.Equal(true, l.Style!.ArrowEnd);
        Assert.Equal(false, l.Style.ArrowStart);
    }

    [Fact]
    public void CopyPaste_NewIds_SameShape_InternalLinksOnly()
    {
        var map = TestData.Tree();
        map.Links.Add(new Link { Id = "inner", From = "b", To = "b1" });
        var frag = MapOps.Copy(map, ["b"]);
        Assert.Equal(2, frag.Nodes.Count);
        Assert.Null(frag.Nodes.Single(n => n.Id == "b").Parent); // rodzic spoza fragmentu odcięty
        Assert.Single(frag.Links);                               // tylko strzałka wewnątrz fragmentu

        var added = MapOps.Paste(map, frag, offset: new Vec(100, 0));
        Assert.Equal(2, added.Count);
        Assert.DoesNotContain(added, n => n.Id is "b" or "b1");
        var newB = added.Single(n => n.Text == "B");
        var newB1 = added.Single(n => n.Text == "B1");
        Assert.Equal(newB.Id, newB1.Parent);
        Assert.Equal(300, newB.X);
        Assert.Contains(map.Links, l => l.From == newB.Id && l.To == newB1.Id);
        Assert.Equal(7, map.Nodes.Count);
    }

    [Fact]
    public void Paste_AtCenter()
    {
        var map = TestData.Tree();
        var frag = MapOps.Copy(map, ["b"]);
        var added = MapOps.Paste(map, frag, center: new Vec(1000, 1000));
        Assert.Equal(1000, added.Average(n => n.X), 0);
        Assert.Equal(1000, added.Average(n => n.Y), 0);
    }

    [Fact]
    public void ApplyPreset_ReplacesStyleButKeepsBranchColor()
    {
        var n = new Node { Style = new NodeStyle { BranchColor = "#123456", Italic = true } };
        MapOps.ApplyPreset(n, Styles.Presets.First(p => p.Name == "Lokacja"));
        Assert.Equal(NodeShape.Diamond, n.Style!.Shape);
        Assert.Null(n.Style.Italic);
        Assert.Equal("#123456", n.Style.BranchColor);

        MapOps.ApplyPreset(new Node(), Styles.Presets.First(p => p.Name == "Zwykły"));
        var plain = new Node { Style = new NodeStyle { Bold = true } };
        MapOps.ApplyPreset(plain, Styles.Presets.First(p => p.Name == "Zwykły"));
        Assert.Null(plain.Style); // pusty styl nie zostaje w pliku
    }

    [Fact]
    public void EditStyle_RemovesEmptyStyle()
    {
        var n = new Node { Style = new NodeStyle { Bold = true } };
        MapOps.EditStyle(n, s => s.Bold = null);
        Assert.Null(n.Style);
    }

    [Fact]
    public void Nearest_PicksNodeInDirection()
    {
        var map = TestData.Tree();
        var root = map.Get("root");
        Assert.Equal("free", MapOps.Nearest(map.Nodes, root, Direction.Left)!.Id);
        Assert.Contains(MapOps.Nearest(map.Nodes, root, Direction.Right)!.Id, new[] { "a", "b" });
        Assert.Equal("b1", MapOps.Nearest(map.Nodes, map.Get("b"), Direction.Right)!.Id);
        Assert.Null(MapOps.Nearest(map.Nodes, map.Get("b1"), Direction.Right));
    }

    [Fact]
    public void Index_DescendantsVisibilityDepth()
    {
        var map = TestData.Tree();
        var idx = new MapIndex(map);
        Assert.Equal(["a", "b", "b1"], idx.Descendants("root").Select(n => n.Id).OrderBy(x => x));
        Assert.Equal(2, idx.Depth(map.Get("b1")));
        Assert.True(idx.IsAncestorOrSelf("root", "b1"));
        Assert.False(idx.IsAncestorOrSelf("a", "b1"));
        map.Get("b").Collapsed = true;
        Assert.False(new MapIndex(map).IsVisible(map.Get("b1")));
        Assert.True(new MapIndex(map).IsVisible(map.Get("b")));
    }
}
