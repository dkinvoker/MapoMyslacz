using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace MapoMyslacz.Editor;

/// <summary>Zamiana typów z rdzenia (kolory, punkty, linie przerywane) na typy XAML.</summary>
internal static class Xaml
{
    private static readonly Dictionary<Rgba, SolidColorBrush> Brushes = [];

    public static Color ToColor(this Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    /// <summary>Pędzel z pamięci podręcznej (te same kolory powtarzają się na setkach kafelków).</summary>
    public static SolidColorBrush Brush(Rgba c)
    {
        if (!Brushes.TryGetValue(c, out var b)) Brushes[c] = b = new SolidColorBrush(c.ToColor());
        return b;
    }

    public static Point P(Vec v) => new(v.X, v.Y);
    public static Vec V(Point p) => new(p.X, p.Y);

    /// <summary>Wzór kreskowania (w jednostkach grubości linii).</summary>
    public static DoubleCollection? Dashes(LinePattern pattern, double thickness)
    {
        if (pattern == LinePattern.Solid) return null;
        var dc = new DoubleCollection();
        if (pattern == LinePattern.Dashed)
        {
            double dash = thickness >= 4 ? 2.5 : 4, gap = thickness >= 4 ? 1.6 : 2.5;
            dc.Add(dash);
            dc.Add(gap);
        }
        else
        {
            dc.Add(0.01);
            dc.Add(thickness >= 4 ? 1.6 : 2.2);
        }
        return dc;
    }

    public static PathGeometry Geometry(IReadOnlyList<Cubic> curves)
    {
        var fig = new PathFigure { StartPoint = P(curves[0].P0), IsClosed = false, IsFilled = false };
        foreach (var c in curves) fig.Segments.Add(new BezierSegment { Point1 = P(c.P1), Point2 = P(c.P2), Point3 = P(c.P3) });
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    public static PointCollection Points(IEnumerable<Vec> pts)
    {
        var pc = new PointCollection();
        foreach (var p in pts) pc.Add(P(p));
        return pc;
    }
}
