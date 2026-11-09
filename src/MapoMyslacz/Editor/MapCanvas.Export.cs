using System.Numerics;
using MapoMyslacz.Core.Export;
using MapoMyslacz.Core.Model;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using static MapoMyslacz.Editor.MapScene;

namespace MapoMyslacz.Editor;

/// <summary>Eksport: PNG, strony do PDF i do druku. Rysuje ta sama scena Win2D co na ekranie (bez zaznaczeń i uchwytów).</summary>
public sealed partial class MapCanvas
{
    /// <summary>Granice mapy do eksportu (null = pusta mapa).</summary>
    public Box? ExportBounds
    {
        get
        {
            CommitEdit();
            return ContentBounds();
        }
    }

    /// <summary>Cała mapa jako PNG. <paramref name="scale"/> 2 = ostro także na ekranach HiDPI i w druku.</summary>
    public async Task ExportPngAsync(string path, double scale = 2, double margin = 40)
    {
        if (ExportBounds is not { } content) throw new InvalidOperationException("Mapa jest pusta — nie ma czego eksportować.");
        var b = content.Inflate(margin);
        var dev = SharedDevice;
        double maxPx = dev.MaximumBitmapSizeInPixels * 0.98;
        scale = Math.Min(scale, maxPx / Math.Max(b.Width, b.Height));
        using var rt = new CanvasRenderTarget(dev, (float)b.Width, (float)b.Height, (float)(96 * scale));
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(C(_canvasColor));
            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            ds.Transform = Matrix3x2.CreateTranslation((float)-b.X, (float)-b.Y);
            _scene.Draw(ds, b, scale);
        }
        await rt.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    /// <summary>Scena do druku: kolor tła z ustawień (białe tło zmienia też automatyczne kolory tekstu).</summary>
    internal MapScene CreatePrintScene(PrintOptions o)
    {
        var scene = new MapScene();
        scene.Build(_session.Map, _session.Index, o.WhiteBackground ? Rgba.White : _canvasColor);
        return scene;
    }

    /// <summary>
    /// Renderuje całą kartkę (marginesy, nagłówek, fragment mapy) do bitmapy BGRA w zadanej rozdzielczości.
    /// Ten sam obraz trafia do PDF i na drukarkę.
    /// </summary>
    internal static (byte[] Bgra, int Width, int Height) RenderPage(MapScene scene, PageTile page, PrintPlan plan, PrintOptions o,
        string title, int pageNumber, int pageCount, double dpi)
    {
        const double dipPerMm = 96 / 25.4;
        var dev = SharedDevice;
        float wDip = (float)(page.PageWidthMm * dipPerMm), hDip = (float)(page.PageHeightMm * dipPerMm);
        double maxPx = dev.MaximumBitmapSizeInPixels * 0.98;
        dpi = Math.Min(dpi, Math.Min(maxPx / (wDip / 96), maxPx / (hDip / 96)));
        using var rt = new CanvasRenderTarget(dev, wDip, hDip, (float)dpi);
        var bg = scene.Canvas;
        using (var ds = rt.CreateDrawingSession())
        {
            ds.Clear(o.WhiteBackground ? Microsoft.UI.Colors.White : C(bg));
            ds.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            var ink = bg.PrefersLightText ? Rgba.Parse("#E4E1EC", Rgba.White) : Rgba.Parse("#3A3744", Rgba.Black);

            if (o.Header)
            {
                double m = Math.Clamp(o.MarginMm, 0, 100) * dipPerMm;
                var headerBox = new Rect(m, m, wDip - 2 * m, PrintLayout.HeaderMm * dipPerMm * 0.8);
                using var tf = new CanvasTextFormat
                {
                    FontFamily = MapScene.FontFamily,
                    FontSize = 15,
                    FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 },
                    VerticalAlignment = CanvasVerticalAlignment.Center,
                    WordWrapping = CanvasWordWrapping.NoWrap,
                    TrimmingSign = CanvasTrimmingSign.Ellipsis,
                    TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
                };
                ds.DrawText(title, headerBox, C(ink), tf);
                if (pageCount > 1)
                {
                    using var pf = new CanvasTextFormat
                    {
                        FontFamily = MapScene.FontFamily,
                        FontSize = 11,
                        HorizontalAlignment = CanvasHorizontalAlignment.Right,
                        VerticalAlignment = CanvasVerticalAlignment.Center,
                    };
                    ds.DrawText($"strona {pageNumber}/{pageCount} (wiersz {page.Row + 1}, kolumna {page.Column + 1})", headerBox, C(ink.WithAlpha(170)), pf);
                }
                float lineY = (float)(headerBox.Bottom + 2);
                ds.DrawLine((float)m, lineY, (float)(wDip - m), lineY, C(ink.WithAlpha(60)), 0.75f);
            }

            // fragment mapy w swoim miejscu na kartce, przycięty do niego
            var place = page.PlacementMm;
            var clip = new Rect(place.X * dipPerMm, place.Y * dipPerMm, place.Width * dipPerMm, place.Height * dipPerMm);
            double s = plan.MmPerUnit * dipPerMm; // jednostki mapy → DIP kartki
            using (ds.CreateLayer(1f, clip))
            {
                ds.Transform = Matrix3x2.CreateTranslation((float)-page.World.X, (float)-page.World.Y)
                               * Matrix3x2.CreateScale((float)s)
                               * Matrix3x2.CreateTranslation((float)clip.X, (float)clip.Y);
                scene.Draw(ds, page.World.Inflate(20), s * dpi / 96);
            }
        }
        var bytes = rt.GetPixelBytes();
        return (bytes, (int)rt.SizeInPixels.Width, (int)rt.SizeInPixels.Height);
    }

    /// <summary>Zapisuje mapę jako PDF według ustawień. Zwraca liczbę stron.</summary>
    public async Task<int> ExportPdfAsync(string path, PrintOptions o, string title, string producer)
    {
        if (ExportBounds is not { } content) throw new InvalidOperationException("Mapa jest pusta — nie ma czego eksportować.");
        var (pw, ph) = PrintLayout.PaperMm(o.Paper);
        var plan = PrintLayout.Plan(content.Inflate(4), pw, ph, o);
        using var scene = CreatePrintScene(o);
        var pages = new List<PdfPage>();
        int n = 0;
        foreach (var page in plan.Pages)
        {
            var (bgra, w, h) = RenderPage(scene, page, plan, o, title, ++n, plan.Pages.Count, o.Dpi);
            pages.Add(new PdfPage(page.PageWidthMm, page.PageHeightMm, w, h, bgra));
        }
        await Task.Run(() =>
        {
            var temp = path + ".tmp";
            using (var fs = File.Create(temp)) PdfWriter.Write(fs, pages, title, producer);
            File.Move(temp, path, overwrite: true);
        });
        return pages.Count;
    }
}
