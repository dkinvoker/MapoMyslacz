using FlaUI.Core.WindowsAPI;

namespace MapoMyslacz.UiTests;

/// <summary>
/// Wersja 1.4: zamiana we wszystkich arkuszach (Ctrl+H), upuszczenie kafelka w ramce, przeciągnięcie kafelków
/// na zakładkę innego arkusza. Mapa jak w <see cref="CanvasUiTests"/>, z ramką wokół Gammy i drugim arkuszem.
/// </summary>
[Collection("UI")]
public sealed class SheetsFramesUiTests
{
    private const string TwoSheets = """
        { "format": "MapoMyslacz", "title": "Test interfejsu", "view": { "x": 0, "y": 0, "zoom": 1 },
          "nodes": [
            { "id": "root", "text": "Korzen", "x": 0, "y": 0 },
            { "id": "alfa", "text": "Alfa", "x": 260, "y": 0, "parent": "root" },
            { "id": "beta", "text": "Beta smok", "x": 260, "y": 110, "parent": "root" },
            { "id": "gamma", "text": "Gamma", "x": -280, "y": 200 } ],
          "links": [ { "id": "l1", "from": "beta", "to": "gamma" } ],
          "frames": [ { "id": "r1", "title": "Ramka", "nodes": ["gamma"] } ],
          "sheets": [ { "title": "Drugi", "view": { "x": 0, "y": 0, "zoom": 1 },
            "nodes": [ { "id": "s2", "text": "Zielony smok", "x": 0, "y": 0 } ] } ] }
        """;

    private static Node Get(MindMap m, string id) => m.Nodes.Single(n => n.Id == id);

    [UiFact]
    public void CtrlH_ReplacesInAllSheets()
    {
        using var app = new AppSession(TwoSheets);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_H);
        app.Type("smok");
        Thread.Sleep(400);
        Assert.Equal("1/2", app.TextOf("SearchCount")); // wynik z bieżącego arkusza i z „Drugiego”
        app.ClickElement("ReplaceBox");
        app.Type("Wyvern");
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.RETURN); // zamień wszystko
        app.Press(VirtualKeyShort.ESCAPE);
        var map = app.SaveAndRead();
        Assert.Equal("Beta Wyvern", Get(map, "beta").Text);
        Assert.Equal("Zielony Wyvern", Assert.Single(map.Sheets!).Nodes.Single().Text);
    }

    [UiFact]
    public void DropTileInFrame_AddsItToFrame()
    {
        using var app = new AppSession(TwoSheets);
        app.Click(260, 0); // Alfa
        app.Drag(260, 0, -250, 226); // do wnętrza ramki wokół Gammy
        var frame = Assert.Single(app.SaveAndRead().Frames!);
        Assert.Contains("alfa", frame.Nodes);
        Assert.Contains("gamma", frame.Nodes);
    }

    [UiFact]
    public void DragTileToSheetTab_MovesItThere()
    {
        using var app = new AppSession(TwoSheets);
        app.Click(-280, 200); // Gamma
        app.DragToScreen(-280, 200, app.ScreenOfName("Drugi"));
        var map = app.SaveAndRead();
        Assert.DoesNotContain(map.Nodes, n => n.Id == "gamma");
        Assert.DoesNotContain(map.Links, l => l.Id == "l1");      // strzałka do kafelka, który odszedł, znika
        var second = Assert.Single(map.Sheets!);
        var moved = second.Nodes.Single(n => n.Id == "gamma");     // to samo id — linki „#gamma” dalej działają
        Assert.Equal((-280d, 200d), (moved.X, moved.Y));
        Assert.Equal("gamma", Assert.Single(second.Frames!).Nodes.Single()); // ramka obejmująca tylko ją — razem z nią
    }
}
