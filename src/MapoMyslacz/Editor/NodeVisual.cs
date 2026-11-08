using MapoMyslacz.Core.Model;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace MapoMyslacz.Editor;

/// <summary>
/// Wygląd jednego kafelka: kształt (prostokąt, owal, romb…) + tekst + znaczki (zwinięta gałąź, notatka).
/// Rozmiar wynika ze zmierzonego tekstu i zasad z <see cref="NodeMetrics"/>; pozycja to środek kafelka.
/// </summary>
internal sealed class NodeVisual
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI, Segoe UI Emoji");

    public readonly XCanvas Root = new();
    private Shape? _shape;
    private NodeShape? _shapeKind;
    private readonly TextBlock _text = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontFamily = Font,
        IsTextScaleFactorEnabled = false,
        IsHitTestVisible = false,
    };
    private TextBox? _editor;
    private Border? _badge;
    private TextBlock? _badgeText;
    private Border? _noteMark;
    private ResolvedNodeStyle? _style;
    private string? _measureKey;
    private double _textW, _textH;

    public string Id { get; }
    public double X { get; private set; }
    public double Y { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    public NodeShape Shape => _style?.Shape ?? NodeShape.Rounded;
    public ResolvedNodeStyle? Style => _style;
    public Box Bounds => Box.FromCenter(X, Y, Width, Height);
    public NodeOutline Outline => new(Shape, Bounds);
    public Vec Size => new(Width, Height);
    public TextBox? Editor => _editor;

    /// <summary>Prostokąt znaczka „+N” zwiniętej gałęzi (w układzie mapy) — kliknięcie rozwija.</summary>
    public Box? BadgeBounds { get; private set; }

    public NodeVisual(string id)
    {
        Id = id;
        Root.Children.Add(_text);
    }

    public void Update(Node node, ResolvedNodeStyle s, int hiddenDescendants, int badgeSide)
    {
        _style = s;
        ApplyFont(_text, s);
        if (_editor is null) _text.Text = node.Text;
        else ApplyFont(_editor, s);

        double wrap = s.WrapWidth(node);
        var key = $"{node.Text}\u0001{s.FontSize}\u0001{s.Bold}\u0001{s.Italic}\u0001{wrap}\u0001{_editor is not null}";
        if (key != _measureKey)
        {
            _measureKey = key;
            MeasureText(wrap);
        }

        var (w, h) = NodeMetrics.OuterSize(s.Shape, s.FontSize, _textW, _textH);
        Width = Math.Ceiling(w);
        Height = Math.Ceiling(h);
        Root.Width = Width;
        Root.Height = Height;

        UpdateShape(s);
        UIElement textElement = _editor is not null ? _editor : _text;
        XCanvas.SetLeft(textElement, Math.Round((Width - _textW) / 2));
        XCanvas.SetTop(textElement, Math.Round((Height - _textH) / 2));

        UpdateBadge(hiddenDescendants, badgeSide, s);
        UpdateNoteMark(!string.IsNullOrWhiteSpace(node.Note));
        SetCenter(node.X, node.Y);
    }

    private static void ApplyFont(TextBlock t, ResolvedNodeStyle s)
    {
        t.FontSize = s.FontSize;
        t.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        t.FontStyle = s.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        t.TextDecorations = s.Strike ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
        t.Foreground = Xaml.Brush(s.TextColor);
        t.TextAlignment = Align(s.Align);
    }

    private static void ApplyFont(TextBox t, ResolvedNodeStyle s)
    {
        t.FontSize = s.FontSize;
        t.FontWeight = s.Bold ? FontWeights.Bold : FontWeights.Normal;
        t.FontStyle = s.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        t.Foreground = Xaml.Brush(s.TextColor);
        t.TextAlignment = Align(s.Align);
        foreach (var k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            t.Resources[k] = Xaml.Brush(s.TextColor);
    }

    private static TextAlignment Align(TextAlign a) => a switch
    {
        TextAlign.Left => TextAlignment.Left,
        TextAlign.Right => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    private void MeasureText(double wrap)
    {
        if (_editor is { } ed)
        {
            ed.Width = double.NaN;
            ed.MaxWidth = wrap;
            ed.Measure(new Size(wrap, double.PositiveInfinity));
            _textW = Math.Ceiling(Math.Max(ed.DesiredSize.Width, 12));
            _textH = Math.Ceiling(ed.DesiredSize.Height);
            ed.Width = _textW;
            return;
        }
        _text.Width = double.NaN;
        _text.MaxWidth = wrap;
        _text.Measure(new Size(wrap, double.PositiveInfinity));
        _textW = Math.Ceiling(_text.DesiredSize.Width);
        _textH = Math.Ceiling(_text.DesiredSize.Height);
        // stała szerokość, żeby wyrównanie do lewej/prawej działało w obrębie kafelka
        _text.Width = _textW + 1;
    }

    private void UpdateShape(ResolvedNodeStyle s)
    {
        if (_shapeKind != s.Shape)
        {
            if (_shape is not null) Root.Children.Remove(_shape);
            _shape = s.Shape switch
            {
                NodeShape.None => null,
                NodeShape.Ellipse => new Ellipse(),
                NodeShape.Diamond or NodeShape.Hexagon => new Polygon(),
                _ => new Rectangle(),
            };
            if (_shape is not null)
            {
                _shape.IsHitTestVisible = false;
                Root.Children.Insert(0, _shape);
            }
            _shapeKind = s.Shape;
        }
        if (_shape is null) return;

        _shape.Width = Width;
        _shape.Height = Height;
        _shape.Fill = Xaml.Brush(s.Fill);
        if (s.BorderWidth > 0)
        {
            _shape.Stroke = Xaml.Brush(s.Border);
            _shape.StrokeThickness = s.BorderWidth;
            if (Xaml.Dashes(s.BorderPattern, s.BorderWidth) is { } dashes) _shape.StrokeDashArray = dashes;
            else _shape.ClearValue(Microsoft.UI.Xaml.Shapes.Shape.StrokeDashArrayProperty);
            _shape.StrokeDashCap = s.BorderPattern == LinePattern.Dotted ? PenLineCap.Round : PenLineCap.Flat;
            _shape.StrokeLineJoin = PenLineJoin.Round;
        }
        else
        {
            _shape.Stroke = null;
            _shape.StrokeThickness = 0;
        }

        switch (_shape)
        {
            case Rectangle r:
                double radius = s.Shape switch
                {
                    NodeShape.Pill => Height / 2,
                    NodeShape.Rectangle => 2,
                    _ => Math.Min(Height / 2, 5 + s.FontSize * 0.25),
                };
                r.RadiusX = r.RadiusY = radius;
                break;
            case Polygon p:
                // obrys rysowany jest na krawędzi — cofamy wierzchołki o pół grubości, żeby nie wystawał
                double inset = s.BorderWidth / 2;
                var box = new Box(inset, inset, Math.Max(1, Width - 2 * inset), Math.Max(1, Height - 2 * inset));
                p.Points = Xaml.Points(s.Shape == NodeShape.Diamond
                    ? [new(box.Center.X, box.Top), new(box.Right, box.Center.Y), new(box.Center.X, box.Bottom), new(box.Left, box.Center.Y)]
                    : GeometryMath.HexagonPoints(box));
                break;
        }
    }

    private void UpdateBadge(int hidden, int side, ResolvedNodeStyle s)
    {
        if (hidden <= 0)
        {
            if (_badge is not null) _badge.Visibility = Visibility.Collapsed;
            BadgeBounds = null;
            return;
        }
        if (_badge is null)
        {
            _badgeText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Xaml.Brush(Rgba.White), IsTextScaleFactorEnabled = false };
            _badge = new Border
            {
                Child = _badgeText,
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(6, 1, 6, 2),
                MinWidth = 22,
                Height = 18,
                IsHitTestVisible = false,
            };
            Root.Children.Add(_badge);
        }
        _badge.Visibility = Visibility.Visible;
        _badge.Background = Xaml.Brush(s.BranchColor);
        _badgeText!.Text = $"+{hidden}";
        _badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = Math.Max(22, _badge.DesiredSize.Width);
        double left = side < 0 ? -bw - 6 : Width + 6;
        XCanvas.SetLeft(_badge, left);
        XCanvas.SetTop(_badge, Height / 2 - 9);
        BadgeLocal = new Box(left, Height / 2 - 9, bw, 18);
    }

    private Box BadgeLocal { get; set; }

    private void UpdateNoteMark(bool hasNote)
    {
        if (!hasNote)
        {
            if (_noteMark is not null) _noteMark.Visibility = Visibility.Collapsed;
            return;
        }
        if (_noteMark is null)
        {
            _noteMark = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = Xaml.Brush(Rgba.Parse("#FFF4B8", Rgba.White)),
                BorderBrush = Xaml.Brush(Rgba.Parse("#C9A400", Rgba.Black)),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Child = new FontIcon
                {
                    Glyph = "",
                    FontSize = 10,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    Foreground = Xaml.Brush(Rgba.Parse("#6B5600", Rgba.Black)),
                },
            };
            Root.Children.Add(_noteMark);
        }
        _noteMark.Visibility = Visibility.Visible;
        XCanvas.SetLeft(_noteMark, Width - 12);
        XCanvas.SetTop(_noteMark, -7);
    }

    public void SetCenter(double x, double y)
    {
        X = x;
        Y = y;
        XCanvas.SetLeft(Root, x - Width / 2);
        XCanvas.SetTop(Root, y - Height / 2);
        if (_badge is { Visibility: Visibility.Visible })
            BadgeBounds = BadgeLocal.Offset(new Vec(x - Width / 2, y - Height / 2));
    }

    public bool HitTest(Vec world) => GeometryMath.ShapeContains(Shape, Bounds, world);

    // ------------------------------------------------------------------ edycja tekstu w miejscu

    /// <summary>Podmienia tekst na pole edycji o identycznym wyglądzie (kafelek rośnie razem z tekstem).</summary>
    public TextBox BeginEdit(string text)
    {
        var s = _style!;
        var tb = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Font,
            IsTextScaleFactorEnabled = false,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Background = Xaml.Brush(Rgba.Transparent),
            IsSpellCheckEnabled = true,
        };
        var clear = Xaml.Brush(Rgba.Transparent);
        foreach (var k in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                 })
            tb.Resources[k] = clear;
        ApplyFont(tb, s);
        _editor = tb;
        _text.Visibility = Visibility.Collapsed;
        Root.Children.Add(tb);
        _measureKey = null;
        return tb;
    }

    public void EndEdit()
    {
        if (_editor is null) return;
        Root.Children.Remove(_editor);
        _editor = null;
        _text.Visibility = Visibility.Visible;
        _measureKey = null;
    }
}
