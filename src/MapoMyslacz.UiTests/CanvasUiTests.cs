using FlaUI.Core.WindowsAPI;

namespace MapoMyslacz.UiTests;

/// <summary>
/// Płótno w prawdziwej aplikacji: dodawanie, przeciąganie, cofanie, zaznaczanie prostokątem, gałęzie, wyszukiwanie,
/// ramki, arkusze. Każdy test startuje z tą samą mapą: temat „Korzeń” z dziećmi „Alfa” (w poziomie po prawej)
/// i „Beta” (niżej), swobodna „Gamma” po lewej i strzałka Beta → Gamma.
/// </summary>
[Collection("UI")] // jedna aplikacja naraz — testy dzielą mysz i klawiaturę
public sealed class CanvasUiTests
{
    internal const string TestMap = """
        { "format": "MapoMyslacz", "title": "Test interfejsu", "view": { "x": 0, "y": 0, "zoom": 1 },
          "nodes": [
            { "id": "root", "text": "Korzen", "x": 0, "y": 0 },
            { "id": "alfa", "text": "Alfa", "x": 260, "y": 0, "parent": "root" },
            { "id": "beta", "text": "Beta", "x": 260, "y": 110, "parent": "root" },
            { "id": "gamma", "text": "Gamma", "x": -280, "y": 200 } ],
          "links": [ { "id": "l1", "from": "beta", "to": "gamma" } ] }
        """;

    private static Node Get(MindMap m, string id) => m.Nodes.Single(n => n.Id == id);

    [UiFact]
    public void Opens_MapWithCounts()
    {
        using var app = new AppSession(TestMap);
        Assert.Contains("ui-test", app.Window.Title);
        Assert.StartsWith("4 kafelki · 1 strzałka", app.Counts);
    }

    [UiFact]
    public void Tab_AddsChild_WithTypedText()
    {
        using var app = new AppSession(TestMap);
        app.Click(260, 0); // Alfa
        app.Press(VirtualKeyShort.TAB);
        app.Type("Nowe dziecko");
        app.Press(VirtualKeyShort.ENTER);
        var map = app.SaveAndRead();
        var child = map.Nodes.Single(n => n.Text == "Nowe dziecko");
        Assert.Equal("alfa", child.Parent);
    }

    [UiFact]
    public void Drag_MovesTile_AndUndoRestores()
    {
        using var app = new AppSession(TestMap);
        app.Drag(-280, 200, -180, 200); // Gamma o 100 w prawo
        var moved = app.SaveAndRead();
        Assert.InRange(Get(moved, "gamma").X, -184, -176);
        Assert.Equal(200, Get(moved, "gamma").Y, 1);

        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Assert.Equal(-280, Get(app.SaveAndRead(), "gamma").X, 1);
    }

    [UiFact]
    public void ShiftDrag_SelectsByRectangle_DeleteRemoves()
    {
        using var app = new AppSession(TestMap);
        app.Drag(190, 70, 340, 150, VirtualKeyShort.SHIFT); // prostokąt wokół Bety
        Assert.Contains("zaznaczono 1", app.Counts);
        app.Press(VirtualKeyShort.DELETE);
        Assert.StartsWith("3 kafelki · 0 strzałek", app.Counts);
        Assert.DoesNotContain(app.SaveAndRead().Nodes, n => n.Id == "beta");
    }

    [UiFact]
    public void BranchClick_ThenDelete_DetachesTile()
    {
        using var app = new AppSession(TestMap);
        app.Click(130, 0); // gałąź Korzeń → Alfa (pozioma linia między kafelkami)
        Assert.Contains("zaznaczono 1", app.Counts);
        app.Press(VirtualKeyShort.DELETE);
        var map = app.SaveAndRead();
        Assert.Null(Get(map, "alfa").Parent); // kafelek został, już bez gałęzi
        Assert.Equal(4, map.Nodes.Count);
    }

    [UiFact]
    public void Search_FindsAndSelects()
    {
        using var app = new AppSession(TestMap);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        app.Type("gam");
        Thread.Sleep(600);
        Assert.Equal("1/1", app.TextOf("SearchCount"));
        Assert.Contains("zaznaczono 1", app.Counts);
    }

    [UiFact]
    public void CtrlG_FramesSelectedTiles()
    {
        using var app = new AppSession(TestMap);
        app.Click(260, 0);
        app.ClickWith(VirtualKeyShort.CONTROL, 260, 110);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
        app.Type("Grupa testowa");
        app.Press(VirtualKeyShort.ENTER);
        var frame = Assert.Single(app.SaveAndRead().Frames!);
        Assert.Equal("Grupa testowa", frame.Title);
        Assert.Equal(new HashSet<string> { "alfa", "beta" }, frame.Nodes.ToHashSet());
    }

    [UiFact]
    public void CtrlT_AddsSheet_SavedInFile()
    {
        using var app = new AppSession(TestMap);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_T);
        Assert.StartsWith("1 kafelek", app.Counts); // nowy arkusz: sam temat główny
        var map = app.SaveAndRead();
        Assert.Equal(4, map.Nodes.Count); // pierwszy arkusz bez zmian
        Assert.Single(map.Sheets!);
    }
}
