using MapoMyslacz.Core.Model;

namespace MapoMyslacz.Core.Export;

public enum PaperSize { A4, A3, A5, Letter }

public enum PageOrientation { Auto, Portrait, Landscape }

public enum PrintScaling
{
    /// <summary>Cała mapa na jednej stronie (zmniejszona albo powiększona do granic marginesów).</summary>
    FitToPage,

    /// <summary>Stała skala, mapa dzielona na tyle stron, ile potrzeba (plakat do sklejenia).</summary>
    Tiles,
}

/// <summary>Ustawienia wydruku i eksportu PDF.</summary>
public sealed class PrintOptions
{
    public PaperSize Paper { get; set; } = PaperSize.A4;
    public PageOrientation Orientation { get; set; } = PageOrientation.Auto;
    public PrintScaling Scaling { get; set; } = PrintScaling.FitToPage;

    /// <summary>Skala przy podziale na strony: 100% = kafelek ma na papierze taki rozmiar jak na ekranie w 100%.</summary>
    public double ScalePercent { get; set; } = 100;

    public double MarginMm { get; set; } = 10;

    /// <summary>Tytuł mapy u góry strony.</summary>
    public bool Header { get; set; } = true;

    /// <summary>Białe tło zamiast koloru płótna (oszczędza tusz, ciemne motywy są czytelne na papierze).</summary>
    public bool WhiteBackground { get; set; } = true;

    /// <summary>Rozdzielczość obrazu strony (PDF i druk są rastrowe).</summary>
    public int Dpi { get; set; } = 300;

    public PrintOptions Clone() => (PrintOptions)MemberwiseClone();
}

/// <summary>Jedna strona: jaki fragment mapy (w jednostkach mapy) trafia w jakie miejsce kartki (w mm od lewego górnego rogu).</summary>
public sealed record PageTile(double PageWidthMm, double PageHeightMm, Box World, Box PlacementMm, int Row, int Column);

public sealed record PrintPlan(IReadOnlyList<PageTile> Pages, double MmPerUnit, bool Landscape, int Rows, int Columns)
{
    /// <summary>Skala względem 100% (do pokazania użytkownikowi).</summary>
    public double ScalePercent => MmPerUnit / PrintLayout.MmPerUnitAt100 * 100;
}

/// <summary>Rozkład mapy na stronach — wspólny dla PDF i drukarki.</summary>
public static class PrintLayout
{
    /// <summary>Jednostka mapy to piksel ekranu przy 100% (1/96 cala).</summary>
    public const double MmPerUnitAt100 = 25.4 / 96;

    /// <summary>Miejsce na nagłówek z tytułem.</summary>
    public const double HeaderMm = 10;

    /// <summary>Największe powiększenie małej mapy przy „dopasuj do strony” (żeby 3 kafelki nie były wielkości dłoni).</summary>
    public const double MaxFitPercent = 250;

    public static (double W, double H) PaperMm(PaperSize p) => p switch
    {
        PaperSize.A3 => (297, 420),
        PaperSize.A5 => (148, 210),
        PaperSize.Letter => (215.9, 279.4),
        _ => (210, 297),
    };

    public static string PaperName(PaperSize p) => p switch
    {
        PaperSize.Letter => "Letter",
        _ => p.ToString(),
    };

    /// <param name="content">Granice mapy (jednostki mapy).</param>
    /// <param name="paperW">Szerokość kartki w orientacji pionowej (mm).</param>
    /// <param name="paperH">Wysokość kartki w orientacji pionowej (mm).</param>
    public static PrintPlan Plan(Box content, double paperW, double paperH, PrintOptions o)
    {
        double shortSide = Math.Min(paperW, paperH), longSide = Math.Max(paperW, paperH);
        bool landscape = o.Orientation switch
        {
            PageOrientation.Landscape => true,
            PageOrientation.Portrait => false,
            _ => content.Width > content.Height,
        };
        double pw = landscape ? longSide : shortSide, ph = landscape ? shortSide : longSide;
        double margin = Math.Clamp(o.MarginMm, 0, Math.Min(pw, ph) / 3);
        double header = o.Header ? HeaderMm : 0;
        double areaX = margin, areaY = margin + header;
        double availW = Math.Max(10, pw - 2 * margin), availH = Math.Max(10, ph - 2 * margin - header);
        double cw = Math.Max(1, content.Width), ch = Math.Max(1, content.Height);

        if (o.Scaling == PrintScaling.FitToPage)
        {
            double s = Math.Min(availW / cw, availH / ch);
            s = Math.Min(s, MmPerUnitAt100 * MaxFitPercent / 100);
            double w = cw * s, h = ch * s;
            var place = new Box(areaX + (availW - w) / 2, areaY + (availH - h) / 2, w, h);
            return new PrintPlan([new PageTile(pw, ph, content, place, 0, 0)], s, landscape, 1, 1);
        }

        double scale = MmPerUnitAt100 * Math.Clamp(o.ScalePercent, 5, 1000) / 100;
        double totalW = cw * scale, totalH = ch * scale;
        int cols = Math.Max(1, (int)Math.Ceiling(totalW / availW - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling(totalH / availH - 1e-9));
        // całość wyśrodkowana na siatce stron (równe marginesy dookoła plakatu)
        double offX = (cols * availW - totalW) / 2, offY = (rows * availH - totalH) / 2;
        var pages = new List<PageTile>();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                // fragment siatki (mm w układzie plakatu) → fragment mapy
                double gx = c * availW - offX, gy = r * availH - offY;
                var world = new Box(content.X + gx / scale, content.Y + gy / scale, availW / scale, availH / scale);
                pages.Add(new PageTile(pw, ph, world, new Box(areaX, areaY, availW, availH), r, c));
            }
        }
        return new PrintPlan(pages, scale, landscape, rows, cols);
    }
}
