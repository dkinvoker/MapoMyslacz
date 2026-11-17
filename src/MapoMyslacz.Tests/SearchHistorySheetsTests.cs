using System.IO.Compression;
using System.Text;
using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Import;

namespace MapoMyslacz.Tests;

/// <summary>
/// Wersja 1.4: zamiana tekstu, szukanie we wszystkich arkuszach, konspekt wielu arkuszy, historia wersji, przenoszenie
/// kafelków między arkuszami, gałęzie łamane, obrazki z XMind.
/// </summary>
public sealed class SearchHistorySheetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mapo-140-" + Guid.NewGuid().ToString("N"));

    public SearchHistorySheetsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ------------------------------------------------------------------ zamiana

    [Theory]
    [InlineData("Żółw na łące", "zolw", "Smok", "Smok na łące", 1)]
    [InlineData("Brama i BRAMA, brama!", "brama", "Wrota", "Wrota i Wrota, Wrota!", 3)]
    [InlineData("Stary\nmłyn nad rzeką", "stary mlyn", "Młyn", "Młyn nad rzeką", 1)]
    [InlineData("Wieża   maga", "wieza maga", "Baszta", "Baszta", 1)]
    [InlineData("Las", "zamek", "X", "Las", 0)]
    [InlineData("🐉 Smok 🐉", "smok", "Wyvern", "🐉 Wyvern 🐉", 1)]
    public void Replace_LikeSearch_KeepsRestOfText(string text, string phrase, string with, string expected, int count)
    {
        Assert.Equal(expected, MapSearch.Replace(text, phrase, with, out int n));
        Assert.Equal(count, n);
    }

    [Fact]
    public void Replace_InTextAndNote_EmptyNoteRemoved()
    {
        var n = new Node { Text = "Karczma", Note = "karczma" };
        Assert.Equal(2, MapSearch.Replace(n, "karczma", "Gospoda"));
        Assert.Equal(("Gospoda", "Gospoda"), (n.Text, n.Note));
        Assert.Equal(1, MapSearch.Replace(new Node { Text = "a", Note = "x" } is var m ? m : m, "x", ""));
        Assert.Null(m.Note);
    }

    [Fact]
    public void FindInSheets_SheetBySheet()
    {
        var a = new MindMap { Nodes = [new Node { Text = "Zamek" }, new Node { Text = "Las" }] };
        var b = new MindMap { Nodes = [new Node { Text = "Zamek w górach", Y = 10 }] };
        var hits = MapSearch.FindInSheets([a, b], "zamek");
        Assert.Equal([0, 1], hits.Select(h => h.Sheet));
        Assert.Equal("Zamek w górach", hits[1].Node.Text);
    }

    // ------------------------------------------------------------------ konspekt wielu arkuszy

    [Fact]
    public void Outline_ManySheets_RoundTrip()
    {
        var s1 = new MindMap { Title = "Wioska", Nodes = [new Node { Id = "r", Text = "Sołtys" }, new Node { Text = "Córka", Parent = "r" }] };
        var s2 = new MindMap { Nodes = [new Node { Text = "Las" }] };
        var md = MarkdownOutline.WriteSheets([("Wioska", s1), ("Sesja 2", s2)]);
        Assert.Contains("# Wioska", md);
        Assert.Contains("# Sesja 2", md);

        var back = MarkdownOutline.ParseSheets(md);
        Assert.Equal(2, back.Count);
        Assert.Equal(("Wioska", "Sesja 2"), (back[0].Title, back[1].Title));
        Assert.Equal(["Sołtys", "Córka"], back[0].Nodes.Select(n => n.Text));
        Assert.Equal("Las", Assert.Single(back[1].Nodes).Text);
    }

    [Fact]
    public void Outline_OneHeadingOrTextBefore_StaysOneMap()
    {
        Assert.Single(MarkdownOutline.ParseSheets("# Mapa\n\n## A\n- b"));
        Assert.Single(MarkdownOutline.ParseSheets("Wstęp\n# A\n# B"));
        Assert.Single(MarkdownOutline.ParseSheets("```\n# nie nagłówek\n```\n# A\n## x"));
        Assert.Equal("# Wioska", MarkdownOutline.WriteSheets([("Wioska", new MindMap { Title = "Wioska" })]).Split('\n')[0].TrimEnd());
    }

    // ------------------------------------------------------------------ historia wersji

    private string NewMap(string name, string text, DateTime saved)
    {
        var path = Path.Combine(_dir, name);
        MapSerializer.Save(new MindMap { Nodes = [new Node { Text = text }] }, path);
        File.SetLastWriteTime(path, saved);
        return path;
    }

    [Fact]
    public void History_BacksUpPreviousVersion_NotTooOften_NotTwice()
    {
        var history = new MapHistory(Path.Combine(_dir, "historia"));
        var t0 = new DateTime(2026, 11, 10, 18, 0, 0);
        var path = NewMap("Sesja.mapo", "pierwsza", t0);
        Assert.Null(history.Backup(Path.Combine(_dir, "nie-ma.mapo"), t0));

        var v1 = history.Backup(path, t0.AddHours(1))!;
        Assert.Equal(t0, v1.Time);
        Assert.Contains("pierwsza", File.ReadAllText(v1.Path));
        Assert.Null(history.Backup(path, t0.AddHours(1).AddMinutes(2))); // za szybko po poprzedniej kopii

        File.SetLastWriteTime(path, t0.AddHours(2));
        Assert.Null(history.Backup(path, t0.AddHours(3))); // ta sama zawartość — bez nowej kopii

        MapSerializer.Save(new MindMap { Nodes = [new Node { Text = "druga" }] }, path);
        File.SetLastWriteTime(path, t0.AddHours(4));
        Assert.NotNull(history.Backup(path, t0.AddHours(5)));
        var list = history.List(path);
        Assert.Equal(2, list.Count);
        Assert.True(list[0].Time > list[1].Time); // najnowsza pierwsza
        Assert.Equal(Path.GetFullPath(path), File.ReadAllText(Path.Combine(history.FolderFor(path), "mapa.txt")));
    }

    [Fact]
    public void History_SameNameDifferentFolders_Separate()
    {
        var h = new MapHistory(Path.Combine(_dir, "historia"));
        Assert.NotEqual(h.FolderFor(@"C:\A\Sesja.mapo"), h.FolderFor(@"C:\B\Sesja.mapo"));
        Directory.CreateDirectory(h.FolderFor(@"C:\A\Sesja.mapo"));
        Assert.Equal(h.FolderFor(@"C:\A\Sesja.mapo"), h.FolderFor(@"c:\a\SESJA.mapo")); // Windows nie rozróżnia wielkości liter
        Assert.StartsWith("Sesja-", Path.GetFileName(h.FolderFor(@"C:\A\Sesja.mapo")));
    }

    [Fact]
    public void History_Prune_KeepsNewestAndOnePerDay()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0);
        var versions = new List<MapHistory.Version>();
        // 30 wersji dzisiaj (co 10 minut) i po 3 dziennie przez 100 dni wstecz
        for (int i = 0; i < 30; i++) versions.Add(new($"d{i}", now.AddMinutes(-10 * i), 1));
        for (int d = 1; d <= 100; d++)
            for (int k = 0; k < 3; k++) versions.Add(new($"o{d}-{k}", now.Date.AddDays(-d).AddHours(20 - k), 1));
        var remove = MapHistory.ToRemove(versions, now).ToHashSet();
        var kept = versions.Where(v => !remove.Contains(v)).ToList();

        Assert.Equal(MapHistory.KeepNewest, kept.Count(v => v.Time.Date == now.Date)); // dzisiejsze: 20 najnowszych
        var older = kept.Where(v => v.Time.Date < now.Date).ToList();
        Assert.All(older.GroupBy(v => v.Time.Date), g => Assert.Single(g)); // po jednej z dnia
        Assert.All(older, v => Assert.Equal(20, v.Time.Hour));               // ta ostatnia z dnia
        Assert.DoesNotContain(older, v => (now - v.Time).TotalDays > MapHistory.KeepDays);
    }

    // ------------------------------------------------------------------ przenoszenie między arkuszami

    [Fact]
    public void MoveToMap_KeepsIdsLinksFramesImages()
    {
        var from = TestData.Tree();
        from.Links.Add(new Link { Id = "lb", From = "b", To = "b1" });
        from.Frames = [new Frame { Id = "f", Title = "Gałąź B", Nodes = ["b"] }];
        MapImages.Attach(from, from.Get("b1"), "img-1", "data:image/png;base64,AA==", 10, 10);
        var to = new MindMap();

        var moved = MapOps.MoveToMap(from, to, ["b"]);
        Assert.Equal(["b", "b1"], moved.Select(n => n.Id)); // z gałęzią i tymi samymi id
        Assert.Null(to.Get("b").Parent);                     // rodzic został na starym arkuszu
        Assert.Equal((200d, 40d), (to.Get("b").X, to.Get("b").Y));
        Assert.Contains(to.Links, l => l.Id == "lb");
        Assert.Equal("f", Assert.Single(to.Frames!).Id);
        Assert.True(to.Images!.ContainsKey("img-1"));

        Assert.DoesNotContain(from.Nodes, n => n.Id is "b" or "b1");
        Assert.DoesNotContain(from.Links, l => l.Id == "lb");
        Assert.Null(from.Frames);
        Assert.Contains(from.Links, l => l.Id == "l1"); // strzałka między kafelkami, które zostały
    }

    [Fact]
    public void MoveToMap_IdTaken_GetsNewIds()
    {
        var from = TestData.Tree();
        var to = new MindMap { Nodes = [new Node { Id = "free", Text = "Inny" }] };
        var moved = MapOps.MoveToMap(from, to, ["free"]);
        Assert.NotEqual("free", Assert.Single(moved).Id);
        Assert.Equal(2, to.Nodes.Count);
        Assert.DoesNotContain(from.Nodes, n => n.Id == "free");
    }

    [Fact]
    public void NewSheet_LooksLikeNeighbour()
    {
        var doc = new MapDocument();
        doc.Sheets[0].Edit(m => { m.Theme = "pergamin"; m.BranchCurve = LinkCurve.Elbow; m.ArrowStyle = "kolorowe"; });
        var s = doc.Sheets[doc.AddSheet(0)].Map;
        Assert.Equal(("pergamin", LinkCurve.Elbow, "kolorowe"), (s.Theme, s.BranchCurve!.Value, s.ArrowStyle));
    }

    // ------------------------------------------------------------------ gałęzie łamane

    private static void Near(Vec expected, Vec actual) => Assert.True(Vec.Distance(expected, actual) < 0.01, $"{expected} ≠ {actual}");

    private static NodeOutline Box(double x, double y) => new(NodeShape.Rectangle, MapoMyslacz.Core.Model.Box.FromCenter(x, y, 80, 30));

    [Fact]
    public void BranchPath_Shapes()
    {
        Assert.Single(LinkRouting.BranchPath(Box(0, 0), Box(200, 80), LinkCurve.Curved));
        var straight = Assert.Single(LinkRouting.BranchPath(Box(0, 0), Box(200, 80), LinkCurve.Straight));
        Assert.Equal((40d, 160d), (straight.P0.X, straight.P3.X)); // od krawędzi do krawędzi

        // łamana w bok: poziomo do połowy drogi, pionowo, poziomo — dwoje dzieci w kolumnie dzieli pień
        var a = LinkRouting.BranchPath(Box(0, 0), Box(200, -60), LinkCurve.Elbow);
        var b = LinkRouting.BranchPath(Box(0, 0), Box(200, 60), LinkCurve.Elbow);
        double TrunkX(List<Cubic> path) => path.Where(c => Math.Abs(c.P0.X - c.P3.X) < 0.01 && Math.Abs(c.P0.Y - c.P3.Y) > 1).Single().P0.X;
        Assert.Equal(40 + LinkRouting.TrunkOffset, TrunkX(a), 1); // pień w stałej odległości od rodzica
        Assert.Equal(TrunkX(a), TrunkX(b), 1);
        Near(new Vec(160, 60), b[^1].P3);

        // dziecko pod rodzicem: pionowo w dół, poziomo, w dół
        var down = LinkRouting.BranchPath(Box(0, 0), Box(30, 150), LinkCurve.Elbow);
        Near(new Vec(0, 15), down[0].P0);
        Near(new Vec(30, 135), down[^1].P3);
        Assert.Contains(down, c => Math.Abs(c.P0.Y - 43) < 0.01 && Math.Abs(c.P3.Y - 43) < 0.01); // pozioma belka pod rodzicem

        // dziecko niżej i trochę w bok (bez wspólnego zakresu w poziomie) — też z dołu, jak w drzewie genealogicznym
        var family = LinkRouting.BranchPath(Box(0, 0), Box(-150, 120), LinkCurve.Elbow);
        Near(new Vec(0, 15), family[0].P0);
        Near(new Vec(-150, 105), family[^1].P3);

        // na jednej linii — zwykły odcinek
        Assert.Single(LinkRouting.BranchPath(Box(0, 0), Box(200, 0), LinkCurve.Elbow));
    }

    [Fact]
    public void BranchCurve_FromThemeOrMap_SavedInFile()
    {
        var map = TestData.Tree();
        Assert.Equal(LinkCurve.Curved, MapThemes.BranchCurveFor(map));
        map.Theme = "schemat";
        Assert.Equal(LinkCurve.Elbow, MapThemes.BranchCurveFor(map));
        map.BranchCurve = LinkCurve.Straight;
        Assert.Equal(LinkCurve.Straight, MapThemes.BranchCurveFor(map));
        var json = MapSerializer.Serialize(map);
        Assert.Contains("\"branchCurve\": \"straight\"", json);
        Assert.Equal(LinkCurve.Straight, MapSerializer.Deserialize(json).BranchCurve);
        Assert.DoesNotContain("branchCurve", MapSerializer.Serialize(TestData.Tree()));
    }

    // ------------------------------------------------------------------ obrazki z XMind

    /// <summary>Nagłówek PNG (sygnatura + IHDR) o zadanym rozmiarze — wystarczy do odczytu typu i rozmiaru.</summary>
    private static byte[] FakePng(int w, int h)
    {
        var b = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        b[16] = (byte)(w >> 24); b[17] = (byte)(w >> 16); b[18] = (byte)(w >> 8); b[19] = (byte)w;
        b[20] = (byte)(h >> 24); b[21] = (byte)(h >> 16); b[22] = (byte)(h >> 8); b[23] = (byte)h;
        return b;
    }

    [Fact]
    public void ImageHeaders_TypeAndSize()
    {
        Assert.Equal(("image/png", (800, 600)), (MapImages.MediaTypeOf(FakePng(800, 600)), MapImages.PixelSize(FakePng(800, 600))));
        byte[] gif = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x40, 0x01, 0xC8, 0x00];
        Assert.Equal((320, 200), MapImages.PixelSize(gif));
        // JPEG: SOI, APP0 (długość 4), SOF0 z wysokością 300 i szerokością 500
        byte[] jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0x2C, 0x01, 0xF4, 0x03];
        Assert.Equal("image/jpeg", MapImages.MediaTypeOf(jpg));
        Assert.Equal((500, 300), MapImages.PixelSize(jpg));
        Assert.Null(MapImages.MediaTypeOf("tekst"u8));
    }

    [Fact]
    public void Xmind_TopicImages_BecomeTileImages()
    {
        const string content = """
            [{ "id": "s1", "title": "Mapa", "rootTopic": { "id": "root", "title": "Zamek",
               "image": { "src": "xap:resources/zamek.png", "width": 120, "height": 90 },
               "children": { "attached": [
                 { "id": "c1", "title": "Bez rozmiaru", "image": { "src": "xap:resources/las.png" } },
                 { "id": "c2", "title": "Brak pliku", "image": { "src": "xap:resources/nie-ma.png" } } ] } } }]
            """;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, byte[] data)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(data);
            }
            Add("content.json", Encoding.UTF8.GetBytes(content));
            Add("resources/zamek.png", FakePng(400, 300));
            Add("resources/las.png", FakePng(800, 400));
        }
        ms.Position = 0;
        var map = Assert.Single(XmindImporter.Import(ms));
        var root = map.Get("root");
        Assert.Equal((120d, 90d), (root.Image!.Width, root.Image.Height)); // rozmiar z XMind
        Assert.Equal((240d, 120d), (map.Get("c1").Image!.Width, map.Get("c1").Image!.Height)); // z nagłówka, jak przy wklejaniu
        Assert.StartsWith("data:image/png;base64,", map.Images![root.Image.Id]);
        Assert.Null(map.Get("c2").Image);
        Assert.Contains("nie udało się go przenieść", map.Get("c2").Note);
        Assert.Null(root.Note);
    }
}
