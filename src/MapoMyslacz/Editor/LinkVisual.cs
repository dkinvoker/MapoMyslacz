using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using XPath = Microsoft.UI.Xaml.Shapes.Path;

namespace MapoMyslacz.Editor;

/// <summary>Strzałka między kafelkami: krzywa, groty i podpis na środku.</summary>
internal sealed class LinkVisual
{
    public readonly XPath Line = new() { IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round };
    public readonly Polygon Head = new() { IsHitTestVisible = false };
    public readonly Polygon Tail = new() { IsHitTestVisible = false };
    public readonly Border Label;
    private readonly TextBlock _labelText;

    public string Id { get; }
    public LinkPath? Route { get; private set; }
    public Box? LabelBounds { get; private set; }
    public ResolvedLinkStyle? Style { get; private set; }

    public LinkVisual(string id)
    {
        Id = id;
        _labelText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 260,
            FontFamily = NodeVisual.Font,
            IsTextScaleFactorEnabled = false,
        };
        Label = new Border
        {
            Child = _labelText,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 2),
            IsHitTestVisible = false,
        };
    }

    public void AddTo(Panel layer)
    {
        layer.Children.Add(Line);
        layer.Children.Add(Head);
        layer.Children.Add(Tail);
        layer.Children.Add(Label);
    }

    public void RemoveFrom(Panel layer)
    {
        layer.Children.Remove(Line);
        layer.Children.Remove(Head);
        layer.Children.Remove(Tail);
        layer.Children.Remove(Label);
    }

    public static double ArrowLength(double width) => 8 + width * 2.2;
    public static double ArrowHalfWidth(double width) => 4 + width * 1.1;

    public void Update(Link link, ResolvedLinkStyle s, LinkPath path, Rgba canvas)
    {
        Route = path;
        Style = s;
        double len = ArrowLength(s.Width), half = ArrowHalfWidth(s.Width);

        // linia kończy się u podstawy grota, żeby nie wystawała spod jego czubka
        var curves = path.Curves.ToList();
        if (s.ArrowEnd)
        {
            var c = curves[^1];
            curves[^1] = c with { P3 = c.P3 - path.EndDir * (len * 0.8) };
        }
        if (s.ArrowStart)
        {
            var c = curves[0];
            curves[0] = c with { P0 = c.P0 - path.StartDir * (len * 0.8) };
        }

        var brush = Xaml.Brush(s.Color);
        Line.Data = Xaml.Geometry(curves);
        Line.Stroke = brush;
        Line.StrokeThickness = s.Width;
        if (Xaml.Dashes(s.Pattern, s.Width) is { } dashes) Line.StrokeDashArray = dashes;
        else Line.ClearValue(Shape.StrokeDashArrayProperty);
        Line.StrokeDashCap = s.Pattern == LinePattern.Dotted ? PenLineCap.Round : PenLineCap.Flat;

        Head.Visibility = s.ArrowEnd ? Visibility.Visible : Visibility.Collapsed;
        Tail.Visibility = s.ArrowStart ? Visibility.Visible : Visibility.Collapsed;
        Head.Fill = Tail.Fill = brush;
        if (s.ArrowEnd) Head.Points = Xaml.Points(Arrow(path.End, path.EndDir, len, half));
        if (s.ArrowStart) Tail.Points = Xaml.Points(Arrow(path.Start, path.StartDir, len, half));

        if (string.IsNullOrWhiteSpace(link.Label))
        {
            Label.Visibility = Visibility.Collapsed;
            LabelBounds = null;
            return;
        }
        Label.Visibility = Visibility.Visible;
        _labelText.Text = link.Label;
        _labelText.FontSize = s.FontSize;
        _labelText.Foreground = Xaml.Brush(canvas.PrefersLightText ? Rgba.Parse("#E4E1EC", Rgba.White) : Rgba.Parse("#3A3744", Rgba.Black));
        Label.Background = Xaml.Brush(canvas.WithAlpha(225));
        Label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = Label.DesiredSize;
        var box = Box.FromCenter(path.LabelAt.X, path.LabelAt.Y, size.Width, size.Height);
        XCanvas.SetLeft(Label, Math.Round(box.X));
        XCanvas.SetTop(Label, Math.Round(box.Y));
        LabelBounds = box;
    }

    public static Vec[] Arrow(Vec tip, Vec dir, double len, double half)
    {
        var b = tip - dir * len;
        var p = dir.Perpendicular;
        return [tip, b + p * half, b - p * half];
    }

    /// <summary>Czy punkt (w układzie mapy) trafia w strzałkę lub jej podpis.</summary>
    public bool HitTest(Vec world, double tolerance)
    {
        if (LabelBounds is { } lb && lb.Inflate(2).Contains(world)) return true;
        return Route is { } p && GeometryMath.DistanceToCurves(world, p.Curves) <= tolerance;
    }
}
