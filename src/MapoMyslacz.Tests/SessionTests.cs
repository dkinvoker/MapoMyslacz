using MapoMyslacz.Core.Editing;

namespace MapoMyslacz.Tests;

/// <summary>Sesja edycji: historia cofania, partie zmian, zaznaczenie, znacznik niezapisanych zmian.</summary>
public class SessionTests
{
    [Fact]
    public void NewSession_HasCentralTopic_AndIsClean()
    {
        var s = new MapSession();
        Assert.Single(s.Map.Nodes);
        Assert.False(s.IsDirty);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void Edit_UndoRedo()
    {
        var s = new MapSession(TestData.Tree());
        int changed = 0;
        s.Changed += () => changed++;

        Assert.True(s.Edit(m => m.Get("a").Text = "Nowe A"));
        Assert.True(s.IsDirty);
        Assert.True(s.CanUndo);

        Assert.True(s.Undo());
        Assert.Equal("A", s.Map.Get("a").Text);
        Assert.True(s.CanRedo);

        Assert.True(s.Redo());
        Assert.Equal("Nowe A", s.Map.Get("a").Text);
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Edit_WithoutChange_DoesNotCreateStep()
    {
        var s = new MapSession(TestData.Tree());
        Assert.False(s.Edit(m => m.Get("a").Text = "A"));
        Assert.False(s.CanUndo);
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void Batch_IsOneUndoStep_AndNests()
    {
        var s = new MapSession(TestData.Tree());
        s.BeginBatch();
        s.Map.Get("a").X = 1;
        s.Edit(m => m.Get("a").X = 2); // zagnieżdżona zmiana nie tworzy osobnego kroku
        s.Map.Get("a").X = 3;
        Assert.True(s.EndBatch());
        Assert.True(s.Undo());
        Assert.Equal(200, s.Map.Get("a").X);
        Assert.False(s.CanUndo);
    }

    [Fact]
    public void CancelBatch_RestoresState()
    {
        var s = new MapSession(TestData.Tree());
        s.BeginBatch();
        MapOps.AddFloating(s.Map, new Vec(5, 5));
        s.Touch();
        s.CancelBatch();
        Assert.Equal(5, s.Map.Nodes.Count);
        Assert.False(s.CanUndo);
        Assert.False(s.IsBatching);
    }

    [Fact]
    public void Undo_IsBlockedDuringBatch()
    {
        var s = new MapSession(TestData.Tree());
        s.Edit(m => m.Get("a").Text = "x");
        s.BeginBatch();
        Assert.False(s.Undo());
        s.EndBatch();
        Assert.True(s.Undo());
    }

    [Fact]
    public void Undo_PrunesSelectionOfRemovedNodes()
    {
        var s = new MapSession(TestData.Tree());
        Node? added = null;
        s.Edit(m => added = MapOps.AddFloating(m, new Vec(1, 1)));
        s.Select([added!.Id, "a"]);
        s.Undo();
        Assert.Equal(["a"], s.SelectedNodes);
    }

    [Fact]
    public void Selection_HidesCollapsedChildren()
    {
        var s = new MapSession(TestData.Tree());
        s.Select(["b1"]);
        s.Edit(m => m.Get("b").Collapsed = true);
        Assert.Empty(s.SelectedNodes);
    }

    [Fact]
    public void SelectAll_SingleNode_Toggle()
    {
        var s = new MapSession(TestData.Tree());
        s.SelectAll();
        Assert.Equal(5, s.SelectedNodes.Count);
        Assert.Single(s.SelectedLinks);
        s.SelectNode("a");
        Assert.Equal("a", s.SingleNode!.Id);
        s.Toggle("b", null);
        Assert.Null(s.SingleNode);
        s.Toggle("b", null);
        Assert.Equal("a", s.SingleNode!.Id);
    }

    [Fact]
    public void SaveAs_ClearsDirty_KeepsViewOutOfHistory()
    {
        var dir = Directory.CreateTempSubdirectory("mapo-test");
        try
        {
            var path = Path.Combine(dir.FullName, "x.mapo");
            var s = new MapSession(TestData.Tree());
            s.Edit(m => m.Get("a").Text = "zmiana");
            s.SaveAs(path, new ViewState { X = 1, Y = 2, Zoom = 0.5 });
            Assert.False(s.IsDirty);
            Assert.Null(s.Map.View); // widok tylko w pliku, nie w historii

            var reopened = MapSession.Open(path);
            Assert.Equal(0.5, reopened.InitialView!.Zoom);
            Assert.Null(reopened.Map.View);
            Assert.Equal("x", reopened.DisplayName);
            Assert.False(reopened.IsDirty);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void History_RespectsLimit()
    {
        var h = new History(limit: 3);
        for (int i = 0; i < 5; i++) h.Push($"s{i}");
        Assert.Equal(3, h.UndoCount);
        Assert.Equal("s4", h.Undo("now"));
        Assert.Equal("now", h.Redo("s4"));
    }
}
