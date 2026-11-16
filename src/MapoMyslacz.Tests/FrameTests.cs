using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Import;

namespace MapoMyslacz.Tests;

/// <summary>Ramki wokół grup kafelków: model, usuwanie, schowek, naprawa pliku, import z XMind.</summary>
public class FrameTests
{
    [Fact]
    public void AddFrame_SkipsMissingNodes_AndCoversBranches()
    {
        var map = TestData.Tree();
        var f = MapOps.AddFrame(map, ["b", "nie-ma", "b"], "Wioska")!;
        Assert.Equal(["b"], f.Nodes);
        Assert.Equal("Wioska", f.Title);
        Assert.Same(f, Assert.Single(map.Frames!));
        // ramka obejmuje gałąź kafelka
        Assert.Equal(new HashSet<string> { "b", "b1" }, MapOps.FrameContent(new MapIndex(map), f));
        Assert.Null(MapOps.AddFrame(map, ["nie-ma"]));
    }

    [Fact]
    public void Delete_Frame_KeepsNodes_DeletingAllNodes_RemovesFrame()
    {
        var map = TestData.Tree();
        var f1 = MapOps.AddFrame(map, ["a", "free"])!;
        var f2 = MapOps.AddFrame(map, ["b"])!;

        MapOps.Delete(map, [], null, [f1.Id]);
        Assert.Equal(5, map.Nodes.Count);
        Assert.Equal([f2.Id], map.Frames!.Select(f => f.Id));

        MapOps.Delete(map, ["b"]); // b z gałęzią — ramka bez kafelków znika
        Assert.Null(map.Frames);
    }

    [Fact]
    public void AddToFrame_AndRemoveFromFrames()
    {
        var map = TestData.Tree();
        var f = MapOps.AddFrame(map, ["a"])!;
        MapOps.AddToFrame(f, ["free", "a"]);
        Assert.Equal(["a", "free"], f.Nodes);
        MapOps.RemoveFromFrames(map, ["a"]);
        Assert.Equal(["free"], f.Nodes);
        MapOps.RemoveFromFrames(map, ["free"]);
        Assert.Null(map.Frames);
    }

    [Fact]
    public void CopyPaste_TakesFrameOnlyWithAllItsNodes()
    {
        var map = TestData.Tree();
        MapOps.AddFrame(map, ["a", "b"], "Cała grupa");
        MapOps.AddFrame(map, ["a", "free"], "Częściowa");

        var frag = MapOps.Copy(map, ["root"]); // root z gałęzią: a, b, b1 — bez „free”
        Assert.Equal("Cała grupa", Assert.Single(frag.Frames!).Title);

        var added = MapOps.Paste(map, frag, offset: new Vec(500, 0));
        var pasted = map.Frames!.Last();
        Assert.Equal("Cała grupa", pasted.Title);
        Assert.Equal(3, map.Frames!.Count);
        Assert.All(pasted.Nodes, id => Assert.Contains(added, n => n.Id == id));
        Assert.DoesNotContain(pasted.Id, map.Frames!.Take(2).Select(f => f.Id));
    }

    [Fact]
    public void Serialize_RoundTrip_AndOldFilesWithoutFrames()
    {
        var map = TestData.Tree();
        MapOps.AddFrame(map, ["a"], "Las")!.Color = "#30A46C";
        var back = MapSerializer.Deserialize(MapSerializer.Serialize(map));
        var f = Assert.Single(back.Frames!);
        Assert.Equal(("Las", "#30A46C"), (f.Title, f.Color));

        // mapa bez ramek nie ma w pliku pola „frames”
        Assert.DoesNotContain("frames", MapSerializer.Serialize(TestData.Tree()));
    }

    [Fact]
    public void Repair_DropsMissingNodesAndEmptyFrames()
    {
        var json = MapSerializer.Serialize(TestData.Tree()).TrimEnd().TrimEnd('}') +
                   """, "frames": [ { "id": "f", "nodes": ["a", "zgubiony", "a"] }, { "id": "f", "nodes": ["duch"] } ] }""";
        var map = MapSerializer.Deserialize(json);
        Assert.Equal(["a"], Assert.Single(map.Frames!).Nodes);
    }

    [Fact]
    public void Session_FrameSelection()
    {
        var map = TestData.Tree();
        var f = MapOps.AddFrame(map, ["a"])!;
        var s = new MapSession(map);
        s.SelectFrame(f.Id);
        Assert.Same(f, s.SingleFrame);
        Assert.Null(s.SingleNode);
        Assert.Equal(1, s.SelectionCount);
        s.SelectNode("a", add: true);
        Assert.Null(s.SingleFrame);
        s.Edit(m => MapOps.Delete(m, [], null, [f.Id]));
        Assert.Empty(s.SelectedFrames); // zaznaczenie usuniętej ramki znika
        Assert.Equal(1, s.SelectionCount);
    }

    [Fact]
    public void Xmind_BoundariesBecomeFrames()
    {
        const string json = """
            [{ "id": "s", "class": "sheet", "title": "Arkusz",
               "rootTopic": { "id": "root", "title": "Kampania",
                 "children": { "attached": [ { "id": "c0", "title": "Wioska" }, { "id": "c1", "title": "Las" }, { "id": "c2", "title": "Zamek" } ] },
                 "boundaries": [
                   { "id": "b1", "range": "(0,1)", "title": "Okolice",
                     "style": { "properties": { "svg:fill": "#0090FF33" } } },
                   { "id": "b2", "range": "master" },
                   { "id": "b3", "range": "(5,9)" } ] } }]
            """;
        var map = XmindImporter.ImportContentJson(json)[0];
        Assert.Equal(2, map.Frames!.Count);
        var okolice = map.Frames[0];
        Assert.Equal("Okolice", okolice.Title);
        Assert.Equal(["c0", "c1"], okolice.Nodes);
        Assert.Equal("#0090FF", okolice.Color);
        Assert.Equal(["root"], map.Frames[1].Nodes);
        Assert.Null(map.Frames[1].Title);
    }
}
