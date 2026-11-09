using System.IO.Compression;
using System.Text;
using MapoMyslacz.Core.Export;
using MapoMyslacz.Core.Import;
using MapoMyslacz.Core.Updates;

namespace MapoMyslacz.Tests;

/// <summary>Znaczniki, linki w kafelkach, rozkład stron do druku/PDF i zapis PDF.</summary>
public class MarkersLinksExportTests
{
    // ================================================================== znaczniki

    [Fact]
    public void Markers_ToggleAndExclusiveGroups()
    {
        var n = new Node();
        Markers.Toggle(n, "🏰");
        Markers.Toggle(n, "priority-2");
        Markers.Toggle(n, "progress-50");
        Assert.Equal(["priority-2", "progress-50", "🏰"], n.Icons); // priorytet, postęp, symbole

        Markers.Toggle(n, "priority-1"); // zastępuje priorytet 2
        Assert.Equal(["priority-1", "progress-50", "🏰"], n.Icons);

        Markers.Toggle(n, "🏰");
        Markers.Toggle(n, "priority-1");
        Markers.Toggle(n, "progress-50");
        Assert.Null(n.Icons); // pusta lista nie zostaje w pliku
    }

    [Fact]
    public void Markers_FindValuesAndCustomEmoji()
    {
        Assert.True(Markers.IsDrawn("priority-3"));
        Assert.False(Markers.IsDrawn("⚠️"));
        Assert.Equal(75, Markers.ProgressValue("progress-75"));
        Assert.Equal(3, Markers.PriorityValue("priority-3"));
        var custom = Markers.Find("🦄");
        Assert.Equal(MarkerGroup.Symbol, custom.Group);
        Assert.Equal("🦄", custom.Glyph);
        Assert.Equal(Markers.All.Count(), Markers.All.Select(m => m.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("priority-1", "priority-1")]
    [InlineData("priority-9", "priority-5")]
    [InlineData("task-half", "progress-50")]
    [InlineData("task-done", "progress-100")]
    [InlineData("flag-red", "🚩")]
    [InlineData("nieznany", null)]
    public void Markers_FromXmind(string xmind, string? expected) => Assert.Equal(expected, Markers.FromXmind(xmind));

    [Fact]
    public void Metrics_IconsMakeNodeWider()
    {
        var plain = new Node { Text = "Zamek" };
        var withIcons = new Node { Text = "Zamek", Icons = ["priority-1", "🏰"] };
        var s = Styles.Resolve(null, Rgba.Black);
        Assert.True(NodeMetrics.Estimate(withIcons, s).X > NodeMetrics.Estimate(plain, s).X + 2 * NodeMetrics.IconSize(15) - 1);
        var (w, h) = NodeMetrics.WithIcons(0, 0, 1, 15, hasText: false);
        Assert.Equal(NodeMetrics.IconSize(15), w);
        Assert.Equal(NodeMetrics.IconSize(15), h);
    }

    [Fact]
    public void IconsAndLink_SurviveSaveCopyAndPaste()
    {
        var map = TestData.Tree();
        map.Get("a").Icons = ["priority-1", "🗡️"];
        map.Get("a").Link = "Sesja 2.mapo";
        var back = MapSerializer.Deserialize(MapSerializer.Serialize(map));
        Assert.Equal(["priority-1", "🗡️"], back.Get("a").Icons);
        Assert.Equal("Sesja 2.mapo", back.Get("a").Link);
        Assert.Contains("🗡️", MapSerializer.Serialize(map)); // emoji zapisane wprost

        // tekst z dosłownym „\uD83D…” (ukośnik w treści) nie może się zmienić
        map.Get("b").Text = @"ścieżka 🗡 i 🐉";
        var json = MapSerializer.Serialize(map);
        Assert.Equal(@"ścieżka 🗡 i 🐉", MapSerializer.Deserialize(json).Get("b").Text);

        var pasted = MapOps.Paste(map, MapOps.Copy(map, ["a"])).Single();
        Assert.Equal(["priority-1", "🗡️"], pasted.Icons);
        Assert.NotSame(map.Get("a").Icons, pasted.Icons);
        Assert.Equal("Sesja 2.mapo", pasted.Link);
    }

    // ================================================================== linki

    [Theory]
    [InlineData("https://example.com/a", LinkKind.Web, "https://example.com/a")]
    [InlineData("www.example.com", LinkKind.Web, "https://www.example.com")]
    [InlineData("mailto:ktos@example.com", LinkKind.Web, "mailto:ktos@example.com")]
    [InlineData("", LinkKind.None, "")]
    public void Links_Web(string link, LinkKind kind, string target)
    {
        var t = LinkTargets.Resolve(link, @"C:\Mapy\kampania.mapo");
        Assert.Equal(kind, t.Kind);
        Assert.Equal(target, t.Target);
    }

    [Fact]
    public void Links_RelativeFilesResolveAgainstMap()
    {
        var map = @"C:\Mapy\Kampania\główna.mapo";
        var t = LinkTargets.Resolve("Sesje\\sesja 1.mapo", map);
        Assert.Equal(LinkKind.Map, t.Kind);
        Assert.Equal(@"C:\Mapy\Kampania\Sesje\sesja 1.mapo", t.Target);

        Assert.Equal(LinkKind.Map, LinkTargets.Resolve("..\\stara.xmind", map).Kind);
        var doc = LinkTargets.Resolve(@"D:\Notatki\postacie.pdf", map);
        Assert.Equal(LinkKind.File, doc.Kind);
        Assert.Equal(@"D:\Notatki\postacie.pdf", doc.Target);
        Assert.Equal(@"C:\x\plik.mapo", LinkTargets.Resolve("file:///C:/x/plik.mapo", map).Target);
    }

    [Fact]
    public void Links_StoredRelativeOnSameDrive()
    {
        var map = @"C:\Mapy\Kampania\główna.mapo";
        Assert.Equal(@"Sesje\sesja 1.mapo", LinkTargets.ToStored(@"C:\Mapy\Kampania\Sesje\sesja 1.mapo", map));
        Assert.Equal(@"..\inne.mapo", LinkTargets.ToStored(@"C:\Mapy\inne.mapo", map));
        Assert.Equal(@"D:\x.mapo", LinkTargets.ToStored(@"D:\x.mapo", map));     // inny dysk — pełna ścieżka
        Assert.Equal(@"C:\a\b.mapo", LinkTargets.ToStored(@"C:\a\b.mapo", null)); // mapa jeszcze niezapisana
        Assert.Equal("sesja 1.mapo", LinkTargets.Describe(@"Sesje\sesja 1.mapo"));
        Assert.Equal("https://example.com", LinkTargets.Describe("https://example.com"));
    }

    [Fact]
    public void Xmind_HrefAndMarkers()
    {
        const string json = """
            [{ "rootTopic": { "id": "r", "title": "R", "href": "https://example.com",
               "markers": [ { "markerId": "task-half" }, { "markerId": "priority-2" }, { "markerId": "flag-red" } ] } }]
            """;
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("content.json").Open(), Encoding.UTF8))
            w.Write(json);
        ms.Position = 0;
        var n = XmindImporter.Import(ms).Single().Nodes.Single();
        Assert.Equal("https://example.com", n.Link);
        Assert.Equal(["priority-2", "progress-50", "🚩"], n.Icons);
        Assert.Null(n.Note);
    }

    // ================================================================== druk i PDF

    [Fact]
    public void Plan_FitToPage_WideMapGoesLandscape()
    {
        var content = new Box(-1000, -300, 2000, 600);
        var plan = PrintLayout.Plan(content, 210, 297, new PrintOptions());
        Assert.True(plan.Landscape);
        var page = Assert.Single(plan.Pages);
        Assert.Equal(297, page.PageWidthMm);
        Assert.Equal(210, page.PageHeightMm);
        // mieści się w marginesach (10 mm) i pod nagłówkiem, proporcje zachowane
        Assert.True(page.PlacementMm.Left >= 10 - 1e-9 && page.PlacementMm.Right <= 287 + 1e-9);
        Assert.True(page.PlacementMm.Top >= 10 + PrintLayout.HeaderMm - 1e-9 && page.PlacementMm.Bottom <= 200 + 1e-9);
        Assert.Equal(content.Width / content.Height, page.PlacementMm.Width / page.PlacementMm.Height, 6);
        Assert.Equal(277, page.PlacementMm.Width, 6); // szerokość wyznacza skalę
    }

    [Fact]
    public void Plan_ForcedPortrait_AndNoHeader()
    {
        var plan = PrintLayout.Plan(new Box(0, 0, 2000, 600), 210, 297,
            new PrintOptions { Orientation = PageOrientation.Portrait, Header = false, MarginMm = 0 });
        Assert.False(plan.Landscape);
        Assert.Equal(210, plan.Pages[0].PlacementMm.Width, 6);
        Assert.Equal(0, plan.Pages[0].PlacementMm.Left, 6);
    }

    [Fact]
    public void Plan_SmallMapIsNotBlownUp()
    {
        var plan = PrintLayout.Plan(new Box(0, 0, 100, 50), 210, 297, new PrintOptions());
        Assert.Equal(PrintLayout.MaxFitPercent, plan.ScalePercent, 6);
    }

    [Fact]
    public void Plan_Tiles_CoverWholeMapWithoutGaps()
    {
        var content = new Box(0, 0, 3000, 1500);
        var o = new PrintOptions { Scaling = PrintScaling.Tiles, ScalePercent = 100, Orientation = PageOrientation.Landscape };
        var plan = PrintLayout.Plan(content, 210, 297, o);
        Assert.Equal(100, plan.ScalePercent, 6);
        Assert.Equal(plan.Rows * plan.Columns, plan.Pages.Count);
        Assert.True(plan.Pages.Count > 1);
        // sąsiednie strony stykają się (bez dziur i zakładek)
        var p00 = plan.Pages.Single(p => p is { Row: 0, Column: 0 });
        var p01 = plan.Pages.Single(p => p is { Row: 0, Column: 1 });
        Assert.Equal(p00.World.Right, p01.World.Left, 6);
        // cała mapa jest w siatce stron
        var all = Box.Union(plan.Pages.Select(p => p.World))!.Value;
        Assert.True(all.ContainsBox(content));
        // fragment mapy na stronie ma ten sam rozmiar co miejsce na kartce w danej skali
        Assert.Equal(p00.PlacementMm.Width, p00.World.Width * plan.MmPerUnit, 6);
    }

    [Fact]
    public void Paper_Sizes()
    {
        Assert.Equal((210, 297), PrintLayout.PaperMm(PaperSize.A4));
        Assert.Equal((297, 420), PrintLayout.PaperMm(PaperSize.A3));
    }

    [Fact]
    public void Pdf_IsWellFormed()
    {
        static PdfPage Page(int w, int h, byte gray) =>
            new(210, 297, w, h, Enumerable.Repeat(gray, w * h * 4).ToArray());

        using var ms = new MemoryStream();
        PdfWriter.Write(ms, [Page(20, 30, 255), Page(30, 20, 128)], "Mapa żółwia", "MapoMyślacz test");
        var bytes = ms.ToArray();
        var text = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Count 2", text);
        Assert.Equal(2, text.Split("/Type /Page ").Length - 1);
        Assert.Contains("/MediaBox [0 0 595.276 841.89]", text); // A4 w punktach

        // startxref wskazuje na tablicę xref, a każdy wpis — na początek obiektu
        int sx = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
        long xref = long.Parse(text[(sx + 10)..].Split('\n')[0]);
        Assert.StartsWith("xref", text[(int)xref..]);
        var entries = text[(int)xref..].Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
        for (int i = 0; i < entries.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", text[(int)long.Parse(entries[i][..10])..]);
    }

    [Fact]
    public void Pdf_RejectsEmptyAndShortData()
    {
        Assert.Throws<ArgumentException>(() => PdfWriter.Write(new MemoryStream(), [], "x", "y"));
        Assert.Throws<ArgumentException>(() => PdfWriter.Write(new MemoryStream(), [new PdfPage(10, 10, 10, 10, new byte[4])], "x", "y"));
    }

    [Fact]
    public void Updates_FallbackRepositories()
    {
        Assert.Equal(UpdateLogic.Repository, UpdateLogic.Repositories[0]);
        // nazwy, pod którą szukają wydań zainstalowane wersje 1.0–1.1, nie wolno zmienić
        Assert.Equal("dkinvoker/MapoMyslacz", UpdateLogic.Repository);
        Assert.Contains("dkinvoker/MapoMy-lacz", UpdateLogic.Repositories);
        Assert.Equal("https://api.github.com/repos/a/b/releases/latest", UpdateLogic.LatestReleaseApiFor("a/b"));
    }
}
