using FlaUI.Core.WindowsAPI;

namespace MapoMyslacz.UiTests;

/// <summary>
/// Wersja 1.3.2: pola tekstowe panelu właściwości (zmiana trafia do właściwego kafelka i od razu na mapę), opcja
/// „nowe kafelki połączone strzałką” i okno opcji. Mapa jak w <see cref="CanvasUiTests"/>.
/// </summary>
[Collection("UI")]
public sealed class PanelOptionsUiTests
{
    private static Node Get(MindMap m, string id) => m.Nodes.Single(n => n.Id == id);

    [UiFact]
    public void PanelNote_StaysWithItsTile_WhenAnotherTileIsClicked()
    {
        using var app = new AppSession(CanvasUiTests.TestMap);
        app.Click(260, 0); // Alfa
        app.ClickElement("NodeNote");
        app.Type("Notatka alfy");
        app.Click(260, 110); // od razu Beta — wcześniej notatka przechodziła na nią
        var map = app.SaveAndRead();
        Assert.Equal("Notatka alfy", Get(map, "alfa").Note);
        Assert.Null(Get(map, "beta").Note);

        // panel pokazuje teraz Betę — z pustą notatką
        app.ClickElement("NodeNote");
        app.Type("Notatka bety");
        app.Click(-280, 200); // Gamma
        map = app.SaveAndRead();
        Assert.Equal(("Notatka alfy", "Notatka bety"), (Get(map, "alfa").Note, Get(map, "beta").Note));
    }

    [UiFact]
    public void PanelText_ChangesTileWhileTyping_AndUndoesAtOnce()
    {
        using var app = new AppSession(CanvasUiTests.TestMap);
        app.Click(-280, 200); // Gamma
        app.ClickElement("NodeText");
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        app.Type("Nowa gamma");
        Thread.Sleep(700);
        // zapis, gdy fokus wciąż jest w polu — tekst jest już na mapie
        Assert.Equal("Nowa gamma", Get(app.SaveAndRead(), "gamma").Text);

        app.Click(-280, 320); // puste miejsce — fokus na płótnie
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z); // całe pisanie to jeden krok
        Assert.Equal("Gamma", Get(app.SaveAndRead(), "gamma").Text);
    }

    [UiFact]
    public void ChildrenAsArrows_TabCreatesLinkedTile()
    {
        using var app = new AppSession(CanvasUiTests.TestMap, settings: "\"ChildrenAsArrows\": true");
        app.Click(260, 0); // Alfa
        app.Press(VirtualKeyShort.TAB);
        app.Type("Przez strzalke");
        app.Press(VirtualKeyShort.ENTER);
        var map = app.SaveAndRead();
        var added = map.Nodes.Single(n => n.Text == "Przez strzalke");
        Assert.Null(added.Parent);
        Assert.Contains(map.Links, l => l.From == "alfa" && l.To == added.Id);
        Assert.True(added.X > Get(map, "alfa").X); // tam, gdzie stanęłoby dziecko
    }

    [UiFact]
    public void Options_SwitchChildrenToArrows()
    {
        using var app = new AppSession(CanvasUiTests.TestMap);
        app.Press(VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);
        app.ClickElement("OptionChildrenAsArrows");
        app.Press(VirtualKeyShort.ESCAPE); // zamyka okno opcji
        app.Click(-280, 200); // Gamma (swobodna)
        app.Press(VirtualKeyShort.TAB);
        app.Type("Nowy");
        app.Press(VirtualKeyShort.ENTER);
        var map = app.SaveAndRead();
        var added = map.Nodes.Single(n => n.Text == "Nowy");
        Assert.Null(added.Parent);
        Assert.Contains(map.Links, l => l.From == "gamma" && l.To == added.Id);
    }
}
