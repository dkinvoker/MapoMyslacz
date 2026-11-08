using System.IO.Compression;
using System.Text;
using MapoMyslacz.Core.Import;

namespace MapoMyslacz.Tests;

/// <summary>Import z XMind (content.json w archiwum ZIP) i wklejanie konspektu.</summary>
public class ImportTests
{
    private const string Content = """
        [{
          "id": "sheet1", "class": "sheet", "title": "Mind Map",
          "theme": {
            "map": { "properties": { "svg:fill": "#ffffff" } },
            "centralTopic": { "properties": { "fo:font-size": "30pt", "fo:font-weight": "800", "fill-pattern": "none", "border-line-width": "0pt" } },
            "floatingTopic": { "properties": { "fo:font-size": "14pt", "svg:fill": "#EEEBEE", "fill-pattern": "solid" } },
            "importantTopic": { "properties": { "fo:font-weight": "bold", "svg:fill": "#7F00AC", "fill-pattern": "solid" } },
            "relationship": { "properties": { "line-color": "#00000066", "line-pattern": "dash", "arrow-end-class": "org.xmind.arrowShape.triangle" } }
          },
          "rootTopic": {
            "id": "root", "title": "Wioska",
            "style": { "properties": { "fo:font-size": "36pt" } },
            "children": {
              "attached": [ { "id": "c1", "title": "Dziecko 1" }, { "id": "c2", "title": "Dziecko 2", "branch": "folded",
                              "children": { "attached": [ { "id": "c21", "title": "Wnuk" } ] } } ],
              "detached": [
                { "id": "f1", "class": "importantTopic", "title": "Sołtys\nEmyr", "position": { "x": -49.4, "y": -165 },
                  "style": { "properties": { "fo:font-size": "18pt" } } },
                { "id": "f2", "title": "Mauzoleum", "position": { "x": -174, "y": -366 },
                  "style": { "properties": { "svg:fill": "#FF9595FF", "shape-class": "org.xmind.topicShape.diamond" } },
                  "notes": { "plain": { "content": "Stare groby" } }, "labels": ["lokacja"] },
                { "id": "f3", "attributedTitle": [ { "text": "Cel: " }, { "text": "przetrwać" } ], "position": { "x": 300, "y": 10 },
                  "style": { "properties": { "border-line-color": "#000000FF", "border-line-width": "2pt", "border-line-pattern": "dash",
                                             "shape-class": "org.xmind.topicShape.ellipserect" } } }
              ]
            }
          },
          "relationships": [
            { "id": "r1", "end1Id": "f1", "end2Id": "f2", "title": "Potrzebny do ofiary", "controlPoints": { "1": { "x": 68, "y": 42 } } },
            { "id": "r2", "end1Id": "f2", "end2Id": "f3", "style": { "properties": { "line-pattern": "solid", "arrow-begin-class": "org.xmind.arrowShape.triangle" } } },
            { "id": "r3", "end1Id": "f1", "end2Id": "f3", "style": { "properties": { "shape-class": "org.xmind.relationshipShape.flexible.curved" } },
              "flexibleControlPoints": [ { "x": 0, "y": 500 }, { "x": 200, "y": 500 } ] },
            { "id": "r4", "end1Id": "f1", "end2Id": "nie-ma" }
          ]
        }]
        """;

    private static MemoryStream Zip(params (string Name, string Text)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in entries)
                using (var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8))
                    w.Write(text);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Xmind_ImportsTopicsStylesAndRelationships()
    {
        var map = XmindImporter.Import(Zip(("content.json", Content))).Single();
        Assert.Null(map.Title); // „Mind Map” to nazwa domyślna — tytuł weźmie się z nazwy pliku
        Assert.Equal("#FFFFFF", map.Background);

        var root = map.Get("root");
        Assert.Equal("Wioska", root.Text);
        Assert.Equal(NodeShape.None, root.Style!.Shape);
        Assert.Equal(36, root.Style.FontSize);
        Assert.True(root.Style.Bold);

        var soltys = map.Get("f1");
        Assert.Equal("Sołtys\nEmyr", soltys.Text);
        Assert.Null(soltys.Parent);
        Assert.Equal((-49.4, -165.0), (soltys.X, soltys.Y));
        Assert.Equal("#7F00AC", soltys.Style!.Fill);
        Assert.True(soltys.Style.Bold);
        Assert.Equal(18, soltys.Style.FontSize);

        var mauzoleum = map.Get("f2");
        Assert.Equal(NodeShape.Diamond, mauzoleum.Style!.Shape);
        Assert.Equal("#FF9595", mauzoleum.Style.Fill);
        Assert.Contains("Stare groby", mauzoleum.Note);
        Assert.Contains("lokacja", mauzoleum.Note);
        Assert.Null(map.Get("f1").Style!.Shape); // zaokrąglony = domyślny

        var cel = map.Get("f3");
        Assert.Equal("Cel: przetrwać", cel.Text);
        Assert.Equal(NodeShape.Pill, cel.Style!.Shape);
        Assert.Equal(LinePattern.Dashed, cel.Style.BorderPattern);
        Assert.Equal("#000000", cel.Style.Border);
        Assert.True(cel.Style.BorderWidth > 0);
        Assert.Null(cel.Style.Fill); // domyślne szare tło XMind ≈ nasze domyślne

        // dzieci bez pozycji — ułożone drzewem po obu stronach tematu głównego
        var c1 = map.Get("c1");
        var c2 = map.Get("c2");
        Assert.Equal("root", c1.Parent);
        Assert.Equal(-1, Math.Sign(c1.X) * Math.Sign(c2.X));
        Assert.True(c2.Collapsed);
        Assert.Equal("c2", map.Get("c21").Parent);

        Assert.Equal(3, map.Links.Count); // strzałka do nieistniejącego tematu pominięta
        var r1 = map.Links.Single(l => l.Id == "r1");
        Assert.Equal(("f1", "f2"), (r1.From, r1.To));
        Assert.Equal("Potrzebny do ofiary", r1.Label);
        Assert.Null(r1.C1);
        Assert.Equal(new Vec(68, 42), r1.C2);
        Assert.Equal("#00000066", r1.Style!.Color);
        Assert.Null(r1.Style.Pattern); // przerywana = domyślna

        var r2 = map.Links.Single(l => l.Id == "r2");
        Assert.Equal(LinePattern.Solid, r2.Style!.Pattern);
        Assert.True(r2.Style.ArrowStart);

        var r3 = map.Links.Single(l => l.Id == "r3");
        Assert.Equal(2, r3.Via!.Count);
    }

    [Fact]
    public void Xmind_ResultSurvivesSaveAndLoad()
    {
        var map = XmindImporter.Import(Zip(("content.json", Content))).Single();
        var back = MapSerializer.Deserialize(MapSerializer.Serialize(map));
        Assert.Equal(map.Nodes.Count, back.Nodes.Count);
        Assert.Equal(map.Links.Count, back.Links.Count);
    }

    [Fact]
    public void Xmind_MultipleSheets()
    {
        var two = $"[{Content.Trim()[1..^1]}, {{ \"title\": \"Druga plansza\", \"rootTopic\": {{ \"id\": \"x\", \"title\": \"X\" }} }}]";
        var maps = XmindImporter.Import(Zip(("content.json", two)));
        Assert.Equal(2, maps.Count);
        Assert.Equal("Druga plansza", maps[1].Title);
    }

    [Fact]
    public void Xmind_OldFormatAndGarbage_GiveClearErrors()
    {
        var old = Assert.Throws<InvalidDataException>(() => XmindImporter.Import(Zip(("content.xml", "<xmap-content/>"))));
        Assert.Contains("XMind 8", old.Message);
        Assert.Throws<InvalidDataException>(() => XmindImporter.Import(new MemoryStream(Encoding.UTF8.GetBytes("nie zip"))));
    }

    [Fact]
    public void Outline_IndentationBecomesBranches()
    {
        var text = "Kampania\n\t- Postacie\n\t\tSołtys\n\t\tHekate\n\t• Miejsca\n\n  1. Karczma\nDrugi temat";
        var frag = OutlineParser.Parse(text);
        string ParentOf(string t) => frag.Nodes.Single(n => n.Text == t).Parent is { } p ? frag.Nodes.Single(n => n.Id == p).Text : "";
        Assert.Equal(7, frag.Nodes.Count);
        Assert.Equal("", ParentOf("Kampania"));
        Assert.Equal("Kampania", ParentOf("Postacie"));
        Assert.Equal("Postacie", ParentOf("Sołtys"));
        Assert.Equal("Kampania", ParentOf("Miejsca"));
        Assert.Equal("Kampania", ParentOf("Karczma"));
        Assert.Equal("", ParentOf("Drugi temat"));
        // drugi temat pod pierwszym drzewem, gałęzie w prawo
        Assert.True(frag.Nodes.Single(n => n.Text == "Drugi temat").Y > frag.Nodes.Where(n => n.Text != "Drugi temat").Max(n => n.Y));
        Assert.True(frag.Nodes.Single(n => n.Text == "Sołtys").X > frag.Nodes.Single(n => n.Text == "Postacie").X);
    }

    [Fact]
    public void Outline_MarkdownHeadings()
    {
        var frag = OutlineParser.Parse("# Główny\n## Pod\n### Głębiej\n## Drugi");
        var byText = frag.Nodes.ToDictionary(n => n.Text);
        Assert.Null(byText["Główny"].Parent);
        Assert.Equal(byText["Główny"].Id, byText["Pod"].Parent);
        Assert.Equal(byText["Pod"].Id, byText["Głębiej"].Parent);
        Assert.Equal(byText["Główny"].Id, byText["Drugi"].Parent);
    }
}
