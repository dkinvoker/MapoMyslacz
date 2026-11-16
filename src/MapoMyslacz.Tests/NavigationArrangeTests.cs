namespace MapoMyslacz.Tests;

/// <summary>Wyszukiwanie kafelków i wyrównywanie/rozkładanie zaznaczenia.</summary>
public class NavigationArrangeTests
{
    private static readonly NodeSizer Fixed = n => n.Id switch
    {
        "wide" => new Vec(200, 40),
        _ => new Vec(100, 40),
    };

    // ------------------------------------------------------------------ wyszukiwanie

    [Theory]
    [InlineData("Żółw", "zolw")]
    [InlineData("ŁĄKA nad rzeką", "laka nad rzeka")]
    [InlineData("Wieś\nzachód", "wies zachod")]
    public void Normalize_IgnoresCaseAndPolishLetters(string text, string expected) =>
        Assert.Equal(expected, MapSearch.Normalize(text));

    [Fact]
    public void Find_MatchesTextAndNote_AllWordsRequired()
    {
        var map = TestData.Tree();
        map.Get("b1").Note = "Stary dąb przy drodze";
        map.Get("free").Text = "Dąb w lesie";

        Assert.Equal(["b1", "free"], MapSearch.Find(map, "dab").Select(n => n.Id));
        Assert.Equal(["b1"], MapSearch.Find(map, "DROGA DĄB".Replace("DROGA", "drodze")).Select(n => n.Id));
        Assert.Empty(MapSearch.Find(map, "dąb smok"));
        Assert.Empty(MapSearch.Find(map, "   "));
    }

    [Fact]
    public void Find_UsesTreeReadingOrder()
    {
        var map = TestData.Tree();
        foreach (var n in map.Nodes) n.Text = "x " + n.Id;
        // tematy od góry (Root y=0, Wolny y=200); w temacie: kafelek, potem gałęzie od góry
        Assert.Equal(["root", "a", "b", "b1", "free"], MapSearch.Find(map, "x").Select(n => n.Id));
    }

    [Fact]
    public void CollapsedAncestors_ListsOnlyCollapsedOnes()
    {
        var map = TestData.Tree();
        map.Get("root").Collapsed = true;
        var idx = new MapIndex(map);
        Assert.Equal(["root"], MapSearch.CollapsedAncestors(idx, map.Get("b1")).Select(n => n.Id));
        Assert.Empty(MapSearch.CollapsedAncestors(idx, map.Get("free")));
    }

    // ------------------------------------------------------------------ wyrównywanie

    private static MindMap Row()
    {
        var map = new MindMap();
        map.Nodes.Add(new Node { Id = "p", Text = "P", X = 0, Y = 0 });
        map.Nodes.Add(new Node { Id = "wide", Text = "W", X = 300, Y = 100 });
        map.Nodes.Add(new Node { Id = "q", Text = "Q", X = 120, Y = 260 });
        map.Nodes.Add(new Node { Id = "q1", Text = "Q1", Parent = "q", X = 260, Y = 260 });
        return map;
    }

    [Fact]
    public void Align_Left_UsesSelectionEdge_AndMovesBranches()
    {
        var map = Row();
        Assert.True(MapOps.Align(map, ["p", "wide", "q"], AlignMode.Left, Fixed));
        // lewa krawędź zaznaczenia: P (0 − 50 = −50)
        Assert.Equal(0, map.Get("p").X);
        Assert.Equal(50, map.Get("wide").X);   // −50 + 200/2
        Assert.Equal(0, map.Get("q").X);
        Assert.Equal(140, map.Get("q1").X);    // dziecko jedzie razem z Q (o −120)
        Assert.Equal(100, map.Get("wide").Y);  // pionowo bez zmian
    }

    [Fact]
    public void Align_CenterY_AndBottom()
    {
        var map = Row();
        MapOps.Align(map, ["p", "q"], AlignMode.CenterY, Fixed);
        Assert.Equal(130, map.Get("p").Y);     // środek zaznaczenia: (−20 … 280) / 2
        Assert.Equal(130, map.Get("q").Y);

        map = Row();
        MapOps.Align(map, ["p", "wide"], AlignMode.Bottom, Fixed);
        Assert.Equal(100, map.Get("p").Y);
        Assert.False(MapOps.Align(map, ["p", "wide"], AlignMode.Bottom, Fixed)); // już wyrównane
    }

    [Fact]
    public void Align_SelectedDescendant_EndsExactlyAligned()
    {
        var map = Row();
        // Q i jego dziecko Q1 w zaznaczeniu: Q przesuwa Q1, ale Q1 i tak trafia na swoje miejsce
        MapOps.Align(map, ["q", "q1", "p"], AlignMode.Right, Fixed);
        Assert.Equal(260, map.Get("q").X);
        Assert.Equal(260, map.Get("q1").X);
        Assert.Equal(260, map.Get("p").X);
    }

    [Fact]
    public void Distribute_Horizontal_EqualGaps_EndsStay()
    {
        var map = Row();
        map.Get("q").X = 40; // kolejność w poziomie: P(0), Q(40), W(300)
        Assert.True(MapOps.Distribute(map, ["p", "q", "wide"], horizontal: true, Fixed));
        Assert.Equal(0, map.Get("p").X);
        Assert.Equal(300, map.Get("wide").X);
        // od −50 do 400: 450 − (100 + 100 + 200) = 50 → odstęp 25; Q: −50 + 100 + 25 + 50 = 125
        Assert.Equal(125, map.Get("q").X);
        Assert.False(MapOps.Distribute(map, ["p", "q"], horizontal: true, Fixed)); // za mało kafelków
    }
}
