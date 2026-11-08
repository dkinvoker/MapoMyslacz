namespace MapoMyslacz.Tests;

/// <summary>Format pliku .mapo: zapis, odczyt, odporność na błędy.</summary>
public class SerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var map = TestData.Tree();
        map.Title = "Kampania";
        map.Background = "#FAF7F0";
        map.View = new ViewState { X = 10, Y = 20, Zoom = 1.5 };
        map.Get("a").Style = new NodeStyle { Shape = NodeShape.Diamond, Fill = "#FF9595", Bold = true, BorderPattern = LinePattern.Dashed };
        map.Get("a").Note = "Notatka\nw dwóch wierszach";
        map.Get("b").Collapsed = true;
        map.Get("b").Width = 320;
        map.Links[0].C1 = new Vec(10, -5);
        map.Links[0].Via = [new Vec(1, 2), new Vec(3, 4)];
        map.Links[0].Style = new LinkStyle { ArrowStart = true, Pattern = LinePattern.Solid, Color = "#00000066" };

        var back = MapSerializer.Deserialize(MapSerializer.Serialize(map));

        Assert.Equal("Kampania", back.Title);
        Assert.Equal("#FAF7F0", back.Background);
        Assert.Equal(1.5, back.View!.Zoom);
        Assert.Equal(map.Nodes.Count, back.Nodes.Count);
        var a = back.Get("a");
        Assert.Equal(NodeShape.Diamond, a.Style!.Shape);
        Assert.True(a.Style.Bold);
        Assert.Equal(LinePattern.Dashed, a.Style.BorderPattern);
        Assert.Equal("Notatka\nw dwóch wierszach", a.Note);
        Assert.True(back.Get("b").Collapsed);
        Assert.Equal(320, back.Get("b").Width);
        Assert.Equal("b", back.Get("b1").Parent);
        var l = back.Links.Single();
        Assert.Equal(new Vec(10, -5), l.C1);
        Assert.Null(l.C2);
        Assert.Equal(2, l.Via!.Count);
        Assert.True(l.Style!.ArrowStart);
        Assert.Equal("#00000066", l.Style.Color);
    }

    [Fact]
    public void Json_IsReadable_PolishLettersAndNamesAsText()
    {
        var map = new MindMap();
        map.Nodes.Add(new Node { Id = "x", Text = "Zażółć gęślą jaźń", Style = new NodeStyle { Shape = NodeShape.Pill } });
        var json = MapSerializer.Serialize(map);
        Assert.Contains("Zażółć gęślą jaźń", json);       // bez ż itp.
        Assert.Contains("\"shape\": \"pill\"", json);     // wyliczenia jako tekst
        Assert.Contains("\"format\": \"MapoMyslacz\"", json);
        Assert.DoesNotContain("\"collapsed\"", json);      // wartości domyślne pomijane
        Assert.DoesNotContain("\"note\"", json);
    }

    [Fact]
    public void Deserialize_AcceptsCommentsAndTrailingCommas()
    {
        var json = """
            {
              // ręcznie poprawiony plik
              "format": "MapoMyslacz",
              "nodes": [ { "id": "a", "text": "A", "x": 5, }, ],
            }
            """;
        var map = MapSerializer.Deserialize(json);
        Assert.Equal(5, map.Nodes.Single().X);
    }

    [Fact]
    public void Deserialize_RejectsGarbageAndOtherFormats()
    {
        Assert.Throws<InvalidDataException>(() => MapSerializer.Deserialize("to nie jest json"));
        Assert.Throws<InvalidDataException>(() => MapSerializer.Deserialize("""{ "format": "InnyProgram", "nodes": [] }"""));
    }

    [Fact]
    public void Deserialize_RepairsBrokenReferences()
    {
        var json = """
            {
              "nodes": [
                { "id": "a", "text": "A", "parent": "nieistniejący" },
                { "id": "b", "text": "B", "parent": "c" },
                { "id": "c", "text": "C", "parent": "b" },
                { "id": "a", "text": "duplikat id" },
                { "id": "d", "parent": "d" }
              ],
              "links": [
                { "id": "l", "from": "a", "to": "zniknął" },
                { "id": "m", "from": "a", "to": "a" },
                { "id": "n", "from": "a", "to": "b" }
              ]
            }
            """;
        var map = MapSerializer.Deserialize(json);
        Assert.Equal(5, map.Nodes.Select(n => n.Id).Distinct().Count()); // duplikat dostał nowe id
        Assert.Null(map.Nodes[0].Parent);                                   // brak rodzica
        Assert.True(map.Get("b").Parent is null || map.Get("c").Parent is null); // pętla przecięta
        Assert.Null(map.Get("d").Parent);                                   // sam sobie rodzicem
        Assert.Equal("", map.Get("d").Text);
        Assert.Equal("n", map.Links.Single().Id);                           // zostaje tylko poprawna strzałka
    }

    [Fact]
    public void Save_WritesAtomicallyAndLoadReadsBack()
    {
        var dir = Directory.CreateTempSubdirectory("mapo-test");
        try
        {
            var path = Path.Combine(dir.FullName, "mapa.mapo");
            MapSerializer.Save(TestData.Tree(), path);
            MapSerializer.Save(TestData.Tree(), path); // nadpisanie
            Assert.Single(Directory.GetFiles(dir.FullName)); // bez plików tymczasowych
            Assert.Equal(5, MapSerializer.Load(path).Nodes.Count);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Fragment_RoundTripAndRejectsOtherJson()
    {
        var frag = MapOps.Copy(TestData.Tree(), ["b"]);
        var back = MapSerializer.TryDeserializeFragment(MapSerializer.SerializeFragment(frag));
        Assert.NotNull(back);
        Assert.Equal(2, back!.Nodes.Count);
        Assert.Null(MapSerializer.TryDeserializeFragment("""{ "nodes": [] }"""));
        Assert.Null(MapSerializer.TryDeserializeFragment("zwykły tekst"));
    }
}
