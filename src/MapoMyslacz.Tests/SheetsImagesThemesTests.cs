using MapoMyslacz.Core.Editing;

namespace MapoMyslacz.Tests;

/// <summary>Wersja 1.3.1: klikalne gałęzie, obrazki w kafelkach, arkusze w jednym pliku, motywy map.</summary>
public sealed class SheetsImagesThemesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mapo-131-" + Guid.NewGuid().ToString("N"));

    public SheetsImagesThemesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private const string Png = "data:image/png;base64,iVBORw0KGgo=";

    // ------------------------------------------------------------------ gałęzie

    [Fact]
    public void Session_SelectsBranchesByChild()
    {
        var s = new MapSession(TestData.Tree());
        s.SelectBranch("b1");
        Assert.Equal(1, s.SelectionCount);
        Assert.Equal(["b1"], s.SelectedBranchChildren.Select(n => n.Id));
        Assert.Null(s.SingleNode);

        s.SelectBranch("root"); // temat główny nie ma gałęzi do rodzica
        Assert.Equal(0, s.SelectionCount);

        s.SelectBranch("b1");
        s.Edit(m => MapOps.Reparent(m, "b1", null)); // gałąź znikła — zaznaczenie też
        Assert.Empty(s.SelectedBranches);
    }

    [Fact]
    public void BranchToLink_DetachesAndKeepsColor()
    {
        var map = TestData.Tree();
        var color = Styles.BranchColorFor(new MapIndex(map), map.Get("b1"));
        var link = MapOps.BranchToLink(map, "b1")!;
        Assert.Null(map.Get("b1").Parent);
        Assert.Equal(("b", "b1"), (link.From, link.To));
        Assert.Equal(color.ToString(), link.Style!.Color);
        Assert.Equal(LinePattern.Solid, link.Style.Pattern);
        Assert.Null(MapOps.BranchToLink(map, "root"));
    }

    // ------------------------------------------------------------------ obrazki

    [Fact]
    public void Images_DataUriIdsAndSizes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var uri = MapImages.ToDataUri(bytes, "image/jpeg");
        Assert.Equal(bytes, MapImages.FromDataUri(uri));
        Assert.Equal(".jpg", MapImages.ExtensionOf(uri));
        Assert.Null(MapImages.FromDataUri("https://example.com/a.png"));
        Assert.Equal(MapImages.IdFor(bytes), MapImages.IdFor([1, 2, 3, 4])); // ta sama zawartość — to samo id
        Assert.Equal((240, 120), MapImages.FitSize(800, 400));
        Assert.Equal((100, 50), MapImages.FitSize(100, 50));

        var img = new NodeImage { Width = 200, Height = 100 };
        MapImages.Resize(img, 100);
        Assert.Equal((100d, 50d), (img.Width, img.Height));
        MapImages.Resize(img, 2);
        Assert.Equal(MapImages.MinWidth, img.Width);
    }

    [Fact]
    public void Images_OutsideUndoSnapshots_UndoRedoKeepData()
    {
        var s = new MapSession(TestData.Tree());
        s.Edit(m => MapImages.Attach(m, m.Get("a"), "img-1", Png, 120, 60));
        Assert.Equal("img-1", s.Map.Get("a").Image!.Id);

        Assert.True(s.Undo());
        Assert.Null(s.Map.Get("a").Image);
        Assert.True(s.Redo());
        Assert.Equal("img-1", s.Map.Get("a").Image!.Id);
        Assert.Same(s.Images, s.Map.Images); // po cofnięciu mapa nadal widzi wspólny magazyn
        Assert.True(s.Images.ContainsKey("img-1"));
    }

    [Fact]
    public void Images_SaveWritesOnlyReferenced_AndRepairDropsMissing()
    {
        var s = new MapSession(TestData.Tree());
        s.Edit(m => MapImages.Attach(m, m.Get("a"), "img-1", Png, 120, 60));
        s.Edit(m => MapImages.Attach(m, m.Get("b"), "img-2", Png, 120, 60));
        s.Edit(m => m.Get("b").Image = null); // img-2 nieużywany
        var path = Path.Combine(_dir, "obrazki.mapo");
        s.SaveAs(path, null);

        var loaded = MapSerializer.Load(path);
        Assert.Equal(["img-1"], loaded.Images!.Keys);
        Assert.Equal(120, loaded.Get("a").Image!.Width);

        var json = File.ReadAllText(path).Replace("\"img-1\": \"", "\"img-zly\": \"");
        var broken = MapSerializer.Deserialize(json);
        Assert.Null(broken.Get("a").Image); // odwołanie do obrazka, którego nie ma w pliku
    }

    [Fact]
    public void Images_TravelWithCopyPasteToAnotherMap()
    {
        var src = new MapSession(TestData.Tree());
        src.Edit(m => MapImages.Attach(m, m.Get("a"), "img-1", Png, 120, 60));
        var frag = MapOps.Copy(src.Map, ["a"]);
        Assert.Equal(Png, frag.Images!["img-1"]);

        var target = new MapSession(new MindMap());
        List<Node> added = [];
        target.Edit(m => added = MapOps.Paste(m, frag, new Vec(0, 0)));
        Assert.Equal("img-1", added.Single().Image!.Id);
        Assert.True(target.Images.ContainsKey("img-1"));
    }

    [Fact]
    public void Metrics_ImageAboveText()
    {
        var plain = new Node { Text = "Zamek" };
        var withImage = new Node { Text = "Zamek", Image = new NodeImage { Id = "x", Width = 200, Height = 100 } };
        var onlyImage = new Node { Text = "", Image = new NodeImage { Id = "x", Width = 200, Height = 100 } };
        var a = MapOps.EstimatingSizer(plain);
        var b = MapOps.EstimatingSizer(withImage);
        var c = MapOps.EstimatingSizer(onlyImage);
        Assert.True(b.X >= 200 && b.Y >= a.Y + 100);
        Assert.True(c.X >= 200 && c.Y < b.Y); // sam obrazek — bez wiersza tekstu
    }

    // ------------------------------------------------------------------ arkusze

    [Fact]
    public void Document_SheetsRoundTrip_FirstSheetIsRoot()
    {
        var doc = new MapDocument();
        doc.Sheets[0].Edit(m => m.Title = "Sesja 1");
        int second = doc.AddSheet(0, "Sesja 2");
        doc.Sheets[second].Edit(m => MapImages.Attach(m, m.Nodes[0], "img-1", Png, 50, 50));
        Assert.True(doc.IsDirty);

        var path = Path.Combine(_dir, "kampania.mapo");
        doc.Save(path, s => new ViewState { X = 1, Y = 2, Zoom = 0.5 });
        Assert.False(doc.IsDirty);

        var raw = MapSerializer.Load(path);
        Assert.Equal("Sesja 1", raw.Title);              // starsze wersje programu widzą pierwszy arkusz
        Assert.Equal("Sesja 2", Assert.Single(raw.Sheets!).Title);
        Assert.Equal(["img-1"], raw.Images!.Keys);        // obrazki wspólne, w korzeniu pliku

        var back = MapDocument.Open(path);
        Assert.Equal(2, back.Sheets.Count);
        Assert.Equal("Sesja 2", back.SheetName(1));
        Assert.Equal("img-1", back.Sheets[1].Map.Nodes[0].Image!.Id);
        Assert.Same(back.Images, back.Sheets[1].Images);
        Assert.Equal(0.5, back.Sheets[1].InitialView!.Zoom);
        Assert.Equal(1, back.SheetOfNode(back.Sheets[1].Map.Nodes[0].Id));
    }

    [Fact]
    public void Document_AddMoveDuplicateRemove()
    {
        var doc = new MapDocument();
        int changes = 0;
        doc.SheetsChanged += () => changes++;
        doc.AddSheet(0, "B");
        doc.AddSheet(1, "C");
        Assert.Equal(["Arkusz 1", "B", "C"], Enumerable.Range(0, 3).Select(doc.SheetName));
        Assert.True(doc.MoveSheet(2, 0));
        Assert.Equal("C", doc.SheetName(0));
        Assert.Equal(3, doc.DuplicateSheet(2));
        Assert.Equal("B (kopia)", doc.SheetName(3));
        Assert.True(doc.RemoveSheet(0));
        Assert.Equal(5, changes); // 2 × dodanie, przesunięcie, kopia, usunięcie
        while (doc.Sheets.Count > 1) doc.RemoveSheet(0);
        Assert.False(doc.RemoveSheet(0)); // ostatniego arkusza nie usuwamy
    }

    [Fact]
    public void Document_FromXmindLikeMaps_MarkDirty()
    {
        var doc = MapDocument.FromMaps([TestData.Tree(), new MindMap { Title = "Drugi" }]);
        Assert.Equal(2, doc.Sheets.Count);
        Assert.False(doc.IsDirty);
        doc.MarkDirty();
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Backlinks_FindLinksInOtherSheets()
    {
        string P(string n) => Path.Combine(_dir, n);
        MapSerializer.Save(new MindMap { Nodes = [new Node { Text = "Cel" }] }, P("Cel.mapo"));
        var withSheet = new MindMap { Nodes = [new Node { Text = "Pierwszy" }], Sheets = [new MindMap { Nodes = [new Node { Id = "x", Text = "Z arkusza", Link = "Cel.mapo" }] }] };
        MapSerializer.Save(withSheet, P("Atlas.mapo"));
        Assert.Equal("Z arkusza", Assert.Single(Backlinks.Find(P("Cel.mapo"))).NodeText);
    }

    // ------------------------------------------------------------------ motywy

    [Fact]
    public void DefaultTheme_LooksLikeBefore()
    {
        var plain = Styles.Resolve(null, Styles.BranchPalette[0], Rgba.White);
        Assert.Equal(Styles.DefaultFill, plain.Fill);
        Assert.Equal(0, plain.BorderWidth);
        var central = Styles.Resolve(null, Styles.BranchPalette[0], Rgba.White, MapThemes.Default, central: true);
        Assert.Equal(Styles.DefaultFill, central.Fill); // w domyślnym motywie temat główny bez zmian
        var link = Styles.Resolve((LinkStyle?)null);
        Assert.Equal((Styles.DefaultLinkColor, LinePattern.Dashed, LinkCurve.Curved), (link.Color, link.Pattern, link.Curve));
    }

    [Fact]
    public void Theme_FillsOnlyWhatTileDoesNotSet()
    {
        var parchment = MapThemes.Find("pergamin");
        var plain = Styles.Resolve(null, parchment.BranchPalette[0], parchment.Canvas, parchment);
        Assert.Equal(parchment.NodeFill, plain.Fill);
        Assert.Equal(parchment.NodeText, plain.TextColor);
        Assert.Equal(parchment.NodeBorderWidth, plain.BorderWidth);
        var central = Styles.Resolve(null, parchment.BranchPalette[0], parchment.Canvas, parchment, central: true);
        Assert.Equal(parchment.CentralFill, central.Fill);

        // własne wypełnienie zostaje, a tekst dobiera się automatycznie do niego (nie z motywu)
        var own = Styles.Resolve(new NodeStyle { Fill = "#000000" }, parchment.BranchPalette[0], parchment.Canvas, parchment);
        Assert.Equal(Rgba.Black, own.Fill);
        Assert.Equal(Styles.LightText, own.TextColor);
        Assert.Equal(MapThemes.Default, MapThemes.Find("nie-ma-takiego"));
    }

    [Fact]
    public void Theme_PaletteAndCentralDetection()
    {
        var map = TestData.Tree();
        var idx = new MapIndex(map);
        var night = MapThemes.Find("noc");
        Assert.Equal(night.BranchPalette[1], Styles.BranchColorFor(idx, map.Get("b"), night));
        Assert.True(MapThemes.IsCentral(idx, map.Get("root")));
        Assert.False(MapThemes.IsCentral(idx, map.Get("free")));
    }

    [Fact]
    public void ArrowStyle_MixesWithTheme_AndBranchColored()
    {
        var map = TestData.Tree();
        map.Theme = "pergamin";
        Assert.Equal(MapThemes.Find("pergamin").Links, MapThemes.LinksFor(map));

        map.ArrowStyle = "lamane";
        var elbow = MapThemes.LinksFor(map);
        Assert.Equal(LinkCurve.Elbow, elbow.Curve);
        Assert.Equal(MapThemes.Find("pergamin").Links.Color, elbow.Color); // kolor z motywu

        map.ArrowStyle = "kolorowe";
        var idx = new MapIndex(map);
        var look = MapThemes.ResolveLink(map, idx, map.Links[0]); // strzałka A → Wolny
        Assert.Equal(Styles.BranchColorFor(idx, map.Get("a"), MapThemes.Find("pergamin")), look.Color);

        map.Links[0].Style = new LinkStyle { Color = "#123456" }; // własny kolor wygrywa
        Assert.Equal(Rgba.Parse("#123456", Rgba.Black), MapThemes.ResolveLink(map, idx, map.Links[0]).Color);
    }

    [Fact]
    public void ClearCustomColors_KeepsShapesFontsAndBends()
    {
        var map = TestData.Tree();
        map.Background = "#FFFFFF";
        map.Get("a").Style = new NodeStyle { Fill = "#FF0000", Shape = NodeShape.Diamond, FontSize = 20, BranchColor = "#00FF00" };
        map.Links[0].Style = new LinkStyle { Color = "#FF0000", Pattern = LinePattern.Solid, ArrowStart = true };
        map.Links[0].C1 = new Vec(10, 10);
        Assert.Equal(2, MapThemes.ClearCustomColors(map));
        var s = map.Get("a").Style!;
        Assert.Equal((null, NodeShape.Diamond, 20.0, null), (s.Fill, s.Shape, s.FontSize!.Value, s.BranchColor));
        Assert.Equal((null, (LinePattern?)null, (bool?)true), (map.Links[0].Style!.Color, map.Links[0].Style!.Pattern, map.Links[0].Style!.ArrowStart));
        Assert.Equal(new Vec(10, 10), map.Links[0].C1);
        Assert.Null(map.Background);
    }

    [Fact]
    public void Theme_SavedInFile()
    {
        var map = TestData.Tree();
        map.Theme = "noc";
        map.ArrowStyle = "kolorowe";
        var back = MapSerializer.Deserialize(MapSerializer.Serialize(map));
        Assert.Equal(("noc", "kolorowe"), (back.Theme, back.ArrowStyle));
        Assert.DoesNotContain("theme", MapSerializer.Serialize(TestData.Tree()));
    }
}
