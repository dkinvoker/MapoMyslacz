using MapoMyslacz.Core.Editing;

namespace MapoMyslacz.Tests;

/// <summary>Wersja 1.3.2: pisanie w panelu jako jeden krok cofania, nowe kafelki połączone strzałką zamiast gałęzi.</summary>
public sealed class PanelEditsArrowChildrenTests
{
    // ------------------------------------------------------------------ łączenie kroków cofania

    [Fact]
    public void Edits_WithSameKey_MergeIntoOneUndoStep()
    {
        var s = new MapSession(TestData.Tree());
        foreach (var t in new[] { "N", "No", "Not", "Nota" })
            s.Edit(m => m.Get("a").Note = t, "pole-1");
        Assert.Equal("Nota", s.Map.Get("a").Note);
        Assert.True(s.Undo());
        Assert.Null(s.Map.Get("a").Note); // całe pisanie naraz
        Assert.False(s.CanUndo);
        Assert.True(s.Redo());
        Assert.Equal("Nota", s.Map.Get("a").Note);
    }

    [Fact]
    public void Edits_OtherKeyOrEditBetween_StartNewStep()
    {
        var s = new MapSession(TestData.Tree());
        s.Edit(m => m.Get("a").Note = "A", "pole-1");
        s.Edit(m => m.Get("a").Note = "AB", "pole-1");
        s.Edit(m => m.Get("b").X += 10);               // inna zmiana pomiędzy
        s.Edit(m => m.Get("a").Note = "ABC", "pole-1"); // to już nowy krok
        s.Edit(m => m.Get("b").Note = "X", "pole-2");   // inne pole — nowy krok

        Assert.True(s.Undo());
        Assert.Null(s.Map.Get("b").Note);
        Assert.True(s.Undo());
        Assert.Equal("AB", s.Map.Get("a").Note);
        Assert.True(s.Undo());
        Assert.Equal(200, s.Map.Get("b").X); // przesunięcie cofnięte osobno
        Assert.True(s.Undo());
        Assert.Null(s.Map.Get("a").Note);
    }

    [Fact]
    public void Edits_AfterUndo_DoNotMergeIntoRedoneStep()
    {
        var s = new MapSession(TestData.Tree());
        s.Edit(m => m.Get("a").Text = "A1", "pole-1");
        s.Undo();
        s.Edit(m => m.Get("a").Text = "A2", "pole-1"); // po cofnięciu ten sam klucz nie skleja się z niczym
        Assert.True(s.Undo());
        Assert.Equal("A", s.Map.Get("a").Text);
    }

    // ------------------------------------------------------------------ dzieci jako strzałki

    [Fact]
    public void LinkedChild_FreeTileWithArrow_WhereChildWouldBe()
    {
        var map = TestData.Tree();
        var tree = MapOps.AddChild(TestData.Tree(), "b1", MapOps.EstimatingSizer);
        var n = MapOps.AddLinkedChild(map, "b1", MapOps.EstimatingSizer);
        Assert.Null(n.Parent);
        Assert.Contains(map.Links, l => l.From == "b1" && l.To == n.Id);
        Assert.Equal((tree.X, tree.Y), (n.X, n.Y)); // to samo miejsce co dziecko w drzewie
    }

    [Fact]
    public void LinkedChildren_StackBelowEachOther_AndGrowAwayFromArrowSource()
    {
        var map = TestData.Tree();
        var first = MapOps.AddLinkedChild(map, "free", MapOps.EstimatingSizer);
        var second = MapOps.AddLinkedChild(map, "free", MapOps.EstimatingSizer);
        Assert.True(second.Y > first.Y);
        Assert.Equal(first.X, second.X, 1); // wyrównane do jednej krawędzi

        // „Wolny” jest po lewej od A, z którego prowadzi do niego strzałka — jego kafelki rosną dalej w lewo
        Assert.True(first.X < map.Get("free").X);

        // dalsze pokolenie: od kafelka, do którego prowadzi strzałka, też w lewo
        var grandchild = MapOps.AddLinkedChild(map, first.Id, MapOps.EstimatingSizer);
        Assert.True(grandchild.X < first.X);
    }

    [Fact]
    public void LinkedSibling_SharesArrowSource_TreeTileGetsTreeSibling()
    {
        var map = TestData.Tree();
        var child = MapOps.AddLinkedChild(map, "root", MapOps.EstimatingSizer);
        var sibling = MapOps.AddLinkedSibling(map, child.Id, MapOps.EstimatingSizer);
        Assert.Null(sibling.Parent);
        Assert.Contains(map.Links, l => l.From == "root" && l.To == sibling.Id);

        var treeSibling = MapOps.AddLinkedSibling(map, "b1", MapOps.EstimatingSizer); // kafelek w drzewie
        Assert.Equal("b", treeSibling.Parent);

        var rootSibling = MapOps.AddLinkedSibling(map, "root", MapOps.EstimatingSizer); // bez rodzica i bez strzałki
        Assert.Null(rootSibling.Parent);
        Assert.DoesNotContain(map.Links, l => l.To == rootSibling.Id);
    }
}
