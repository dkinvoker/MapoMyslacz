using System.Numerics;
using MapoMyslacz.Core.Model;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;
using Windows.UI;

namespace MapoMyslacz.Editor;

/// <summary>Wyliczony wygląd kafelka: rozmiar, położenie treści, układ tekstu, znaczki.</summary>
internal sealed class NodeView
{
    public required string Id;
    public double X, Y, Width, Height;
    public ResolvedNodeStyle Style = null!;
    public CanvasTextLayout? Text;
    public Rect TextBounds;
    public double TextX, TextY, TextW, TextH;
    public double IconsX, IconsY, IconSize;
    public IReadOnlyList<string> Icons = [];
    public int Hidden, BadgeSide;
    public bool HasNote, HasLink, Editing;
    public string? BadgeText;
    public CanvasGeometry? ShapeGeometry;
    public string? ShapeKey;

    public NodeShape Shape => Style.Shape;
    public Box Bounds => Box.FromCenter(X, Y, Width, Height);
    public NodeOutline Outline => new(Shape, Bounds);
    public Vec Size => new(Width, Height);

    /// <summary>Lewy górny róg obszaru tekstu w układzie mapy (tu staje pole edycji).</summary>
    public Vec TextOrigin => new(X - Width / 2 + TextX, Y - Height / 2 + TextY);

    /// <summary>Znaczek „+N” zwiniętej gałęzi (klik rozwija).</summary>
    public Box? BadgeBounds => Hidden <= 0 ? null : BadgeLocal.Offset(new Vec(X - Width / 2, Y - Height / 2));
    public Box BadgeLocal;

    /// <summary>Znaczek linku w lewym górnym rogu (klik otwiera link).</summary>
    public Box? LinkMarkBounds => HasLink ? Box.FromCenter(X - Width / 2 + 5, Y - Height / 2 + 1, 18, 18) : null;

    /// <summary>Obszar z wszystkimi dodatkami (znaczki wystają poza kafelek) — do odcinania przy rysowaniu.</summary>
    public Box PaintBounds => Box.Union(Bounds.Inflate(12), BadgeBounds ?? Bounds);

    public bool HitTest(Vec w) => GeometryMath.ShapeContains(Shape, Bounds, w);
}

/// <summary>Strzałka: przebieg, styl, podpis.</summary>
internal sealed class LinkView
{
    public required string Id;
    public LinkPath? Route;
    public ResolvedLinkStyle? Style;
    public CanvasGeometry? Geometry;
    public Cubic[] GeometryCurves = [];
    public Vec[] Polyline = [];
    public Box CurveBounds;
    public CanvasTextLayout? Label;
    public Box? LabelBounds;
    public Rect LabelLayoutBounds;
    public CanvasGeometry? Head, Tail;
    public readonly StrokeCache Stroke = new();

    public Box PaintBounds => LabelBounds is { } lb ? Box.Union(CurveBounds, lb) : CurveBounds;

    public bool HitTest(Vec world, double tolerance)
    {
        if (LabelBounds is { } lb && lb.Inflate(2).Contains(world)) return true;
        if (!CurveBounds.Inflate(tolerance).Contains(world)) return false;
        for (int i = 1; i < Polyline.Length; i++)
            if (GeometryMath.DistanceToSegment(world, Polyline[i - 1], Polyline[i]) <= tolerance) return true;
        return false;
    }
}

/// <summary>Gałąź drzewa (od rodzica do dziecka).</summary>
internal sealed class BranchView
{
    public required string ChildId;
    public required string ParentId;
    public Cubic Curve;
    public Rgba Color;
    public double Width;
    public Box Bounds;
    public CanvasGeometry? Geometry;
    public readonly StrokeCache Stroke = new();
}

/// <summary>
/// Gotowy obrys linii (z kreskami) — Direct2D nie musi go liczyć w każdej klatce (przy długich przerywanych
/// strzałkach to tysiące odcinków). Liczony na nowo tylko po zmianie kształtu albo przy innym rzędzie powiększenia
/// (żeby krzywe były gładkie z bliska, a z daleka nie miały zbędnych szczegółów).
/// </summary>
internal sealed class StrokeCache
{
    private CanvasCachedGeometry? _cached;
    private double _bucket;
    private float _width;
    private CanvasStrokeStyle? _style;

    public CanvasCachedGeometry Get(CanvasGeometry geometry, float width, CanvasStrokeStyle style, double zoom)
    {
        double bucket = Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(zoom, 1.0 / 64))));
        if (_cached is null || bucket != _bucket || width != _width || !ReferenceEquals(style, _style))
        {
            _cached?.Dispose();
            _cached = CanvasCachedGeometry.CreateStroke(geometry, width, style, (float)(0.25 / bucket));
            _bucket = bucket;
            _width = width;
            _style = style;
        }
        return _cached;
    }

    public void Reset()
    {
        _cached?.Dispose();
        _cached = null;
    }
}

/// <summary>
/// Scena mapy rysowana przez Win2D: z modelu buduje widoki (z pamięcią układów tekstu i geometrii, więc przebudowa
/// jest tania), rysuje tylko to, co widać, i tak samo służy do eksportu PNG/PDF i druku.
/// </summary>
internal sealed class MapScene : IDisposable
{
    public const string FontFamily = "Segoe UI Variable Text";
    private const string EmojiFont = "Segoe UI Emoji";

    public readonly Dictionary<string, NodeView> Nodes = [];
    public readonly List<NodeView> NodeOrder = [];
    public readonly Dictionary<string, LinkView> Links = [];
    public readonly List<BranchView> Branches = [];
    public Rgba Canvas = Styles.CanvasBackground;

    private readonly Dictionary<string, CanvasTextLayout> _textCache = [];
    private readonly HashSet<string> _usedText = [];
    private readonly Dictionary<string, CanvasTextFormat> _formats = [];
    private static readonly CanvasDevice Device = CanvasDevice.GetSharedDevice();
    private static string? _iconFont;

    /// <summary>Kafelek edytowany w miejscu: jego tekst rysuje pole edycji (XAML), a rozmiar liczymy z bieżącej treści.</summary>
    public string? EditingId;

    public static CanvasDevice SharedDevice => Device;

    // ================================================================== budowanie

    public void Build(MindMap map, MapIndex idx, Rgba canvas)
    {
        Canvas = canvas;
        _usedText.Clear();
        var visible = new HashSet<string>();
        NodeOrder.Clear();
        foreach (var n in map.Nodes)
        {
            if (!idx.IsVisible(n)) continue;
            visible.Add(n.Id);
            if (!Nodes.TryGetValue(n.Id, out var v)) Nodes[n.Id] = v = new NodeView { Id = n.Id };
            LayoutNode(idx, n, v);
            NodeOrder.Add(v);
        }
        foreach (var id in Nodes.Keys.Where(k => !visible.Contains(k)).ToList())
        {
            Nodes[id].ShapeGeometry?.Dispose();
            Nodes.Remove(id);
        }

        // gałęzie: widoki zostają między przebudowami (z gotowymi obrysami), zmieniają się tylko poruszone
        var oldBranches = Branches.ToDictionary(b => b.ChildId);
        Branches.Clear();
        foreach (var n in map.Nodes)
        {
            if (n.Parent is null || !Nodes.TryGetValue(n.Id, out var cv) || !Nodes.TryGetValue(n.Parent, out var pv)) continue;
            var parent = idx.Get(n.Parent);
            double width = parent?.Parent is null ? 3 : 2;
            if (!oldBranches.Remove(n.Id, out var b) || b.ParentId != n.Parent || b.Width != width)
            {
                b?.Geometry?.Dispose();
                b?.Stroke.Reset();
                b = new BranchView { ChildId = n.Id, ParentId = n.Parent, Width = width };
            }
            b.Color = cv.Style.BranchColor;
            SetBranchCurve(b, pv, cv);
            Branches.Add(b);
        }
        foreach (var gone in oldBranches.Values)
        {
            gone.Geometry?.Dispose();
            gone.Stroke.Reset();
        }

        var want = new HashSet<string>();
        foreach (var l in map.Links)
        {
            if (!Nodes.ContainsKey(l.From) || !Nodes.ContainsKey(l.To)) continue;
            want.Add(l.Id);
            if (!Links.TryGetValue(l.Id, out var lv)) Links[l.Id] = lv = new LinkView { Id = l.Id };
            UpdateLink(l, lv);
        }
        foreach (var id in Links.Keys.Where(k => !want.Contains(k)).ToList())
        {
            Links[id].Geometry?.Dispose();
            Links[id].Head?.Dispose();
            Links[id].Tail?.Dispose();
            Links.Remove(id);
        }

        // nieużywane układy tekstu (zmieniony tekst, usunięte kafelki) — zwalniamy
        foreach (var key in _textCache.Keys.Where(k => !_usedText.Contains(k)).ToList())
        {
            _textCache[key].Dispose();
            _textCache.Remove(key);
        }
    }

    /// <summary>Przelicza wygląd jednego kafelka (np. w trakcie pisania). Zwraca widok.</summary>
    public NodeView? Relayout(MapIndex idx, Node n)
    {
        if (!Nodes.TryGetValue(n.Id, out var v)) return null;
        LayoutNode(idx, n, v);
        return v;
    }

    private void LayoutNode(MapIndex idx, Node n, NodeView v)
    {
        var s = Styles.Resolve(n.Style, Styles.BranchColorFor(idx, n), Canvas);
        v.Style = s;
        v.X = n.X;
        v.Y = n.Y;
        v.Editing = n.Id == EditingId;
        v.Icons = n.Icons ?? (IReadOnlyList<string>)[];
        v.HasNote = !string.IsNullOrWhiteSpace(n.Note);
        v.HasLink = !string.IsNullOrWhiteSpace(n.Link);

        double fs = s.FontSize;
        bool hasText = n.Text.Length > 0 || v.Editing || v.Icons.Count == 0;
        double tw = 0, th = 0;
        v.Text = null;
        if (hasText)
        {
            var layout = TextLayout(n.Text.Length == 0 ? "" : n.Text, s, s.WrapWidth(n));
            var lb = layout.LayoutBounds;
            tw = n.Text.Length == 0 ? 0 : Math.Ceiling(lb.Width);
            th = Math.Max(Math.Ceiling(lb.Height), NodeMetrics.LineHeight(fs));
            v.Text = n.Text.Length == 0 ? null : layout;
            v.TextBounds = lb;
        }
        v.TextW = tw;
        v.TextH = th;
        var (cw, ch) = NodeMetrics.WithIcons(tw, th, v.Icons.Count, fs, hasText);
        var (w, h) = NodeMetrics.OuterSize(s.Shape, fs, cw, ch);
        v.Width = Math.Ceiling(w);
        v.Height = Math.Ceiling(h);
        double cx = Math.Round((v.Width - Math.Max(cw, NodeMetrics.MinTextWidth)) / 2), cy = Math.Round((v.Height - ch) / 2);
        v.IconSize = NodeMetrics.IconSize(fs);
        v.IconsX = cx;
        v.IconsY = cy + (ch - v.IconSize) / 2;
        double iconsW = v.Icons.Count == 0 ? 0 : NodeMetrics.IconsWidth(v.Icons.Count, fs) + (hasText ? NodeMetrics.IconTextGap : 0);
        v.TextX = cx + iconsW;
        v.TextY = cy + (ch - th) / 2;

        int hidden = n.Collapsed ? idx.Descendants(n.Id).Count : 0;
        v.Hidden = hidden;
        v.BadgeSide = ChildrenSide(idx, n);
        if (hidden > 0)
        {
            v.BadgeText = $"+{hidden}";
            double bw = Math.Max(22, 12 + v.BadgeText.Length * 7);
            double left = v.BadgeSide < 0 ? -bw - 6 : v.Width + 6;
            v.BadgeLocal = new Box(left, v.Height / 2 - 9, bw, 18);
        }

        // geometria rombu/sześciokąta — tylko gdy zmienił się kształt albo rozmiar
        var key = $"{s.Shape}|{v.Width}|{v.Height}|{s.BorderWidth}";
        if (s.Shape is NodeShape.Diamond or NodeShape.Hexagon)
        {
            if (v.ShapeKey != key)
            {
                v.ShapeGeometry?.Dispose();
                double inset = s.BorderWidth / 2;
                var box = new Box(inset, inset, Math.Max(1, v.Width - 2 * inset), Math.Max(1, v.Height - 2 * inset));
                Vec[] pts = s.Shape == NodeShape.Diamond
                    ? [new(box.Center.X, box.Top), new(box.Right, box.Center.Y), new(box.Center.X, box.Bottom), new(box.Left, box.Center.Y)]
                    : GeometryMath.HexagonPoints(box);
                v.ShapeGeometry = CanvasGeometry.CreatePolygon(Device, pts.Select(V2).ToArray());
                v.ShapeKey = key;
            }
        }
        else if (v.ShapeGeometry is not null)
        {
            v.ShapeGeometry.Dispose();
            v.ShapeGeometry = null;
            v.ShapeKey = null;
        }
    }

    public static int ChildrenSide(MapIndex idx, Node n)
    {
        var kids = idx.ChildrenOf(n.Id);
        if (kids.Count > 0) return kids.Average(k => k.X) < n.X ? -1 : 1;
        return idx.ParentOf(n) is null ? 1 : MapOps.SideOf(idx, n);
    }

    /// <summary>Po przesunięciu kafelków: pozycje i zależne linie.</summary>
    public void Move(MapIndex idx, HashSet<string> moved, MindMap map)
    {
        foreach (var id in moved)
            if (Nodes.TryGetValue(id, out var v) && idx.Get(id) is { } n)
            {
                v.X = n.X;
                v.Y = n.Y;
            }
        foreach (var b in Branches)
            if (moved.Contains(b.ChildId) || moved.Contains(b.ParentId)) SetBranchCurve(b, Nodes[b.ParentId], Nodes[b.ChildId]);
        foreach (var l in map.Links)
            if ((moved.Contains(l.From) || moved.Contains(l.To)) && Links.TryGetValue(l.Id, out var lv)) UpdateLink(l, lv);
    }

    private static void SetBranchCurve(BranchView b, NodeView parent, NodeView child)
    {
        var curve = LinkRouting.Branch(parent.Outline, child.Outline);
        if (b.Geometry is not null && curve == b.Curve) return;
        b.Curve = curve;
        b.Geometry?.Dispose();
        b.Stroke.Reset();
        b.Geometry = PathGeometry([curve]);
        b.Bounds = Box.Union(curve.Flatten(8).Select(p => new Box(p.X, p.Y, 0, 0)))!.Value;
    }

    public void UpdateLink(Link l, LinkView lv)
    {
        var s = Styles.Resolve(l.Style);
        lv.Style = s;
        var path = LinkRouting.Route(l, s.Curve, Nodes[l.From].Outline, Nodes[l.To].Outline);
        lv.Route = path;

        // linia kończy się u podstawy grota, żeby nie wystawała spod jego czubka
        double len = ArrowLength(s.Width);
        var curves = path.Curves.ToArray();
        if (s.ArrowEnd) curves[^1] = curves[^1] with { P3 = curves[^1].P3 - path.EndDir * (len * 0.8) };
        if (s.ArrowStart) curves[0] = curves[0] with { P0 = curves[0].P0 - path.StartDir * (len * 0.8) };
        if (lv.Geometry is null || !curves.AsSpan().SequenceEqual(lv.GeometryCurves) || (lv.Head is null) == s.ArrowEnd || (lv.Tail is null) == s.ArrowStart)
        {
            lv.Geometry?.Dispose();
            lv.Head?.Dispose();
            lv.Tail?.Dispose();
            lv.Stroke.Reset();
            lv.Geometry = PathGeometry(curves);
            lv.GeometryCurves = curves;
            lv.Polyline = path.Curves.SelectMany((c, i) => c.Flatten(20).Skip(i == 0 ? 0 : 1)).ToArray();
            lv.CurveBounds = Box.Union(lv.Polyline.Select(p => new Box(p.X, p.Y, 0, 0)))!.Value;
            double half = ArrowHalfWidth(s.Width);
            lv.Head = s.ArrowEnd ? CanvasGeometry.CreatePolygon(Device, Arrow(path.End, path.EndDir, len, half).Select(V2).ToArray()) : null;
            lv.Tail = s.ArrowStart ? CanvasGeometry.CreatePolygon(Device, Arrow(path.Start, path.StartDir, len, half).Select(V2).ToArray()) : null;
        }

        if (string.IsNullOrWhiteSpace(l.Label))
        {
            lv.Label = null;
            lv.LabelBounds = null;
            return;
        }
        var key = $"L\u0001{l.Label}\u0001{s.FontSize}";
        _usedText.Add(key);
        if (!_textCache.TryGetValue(key, out var layout))
        {
            var fmt = Format(s.FontSize, false, false, TextAlign.Center);
            using var probe = new CanvasTextLayout(Device, l.Label, fmt, 260, float.MaxValue);
            float w = (float)Math.Ceiling(probe.LayoutBounds.Width) + 1;
            layout = new CanvasTextLayout(Device, l.Label, fmt, w, float.MaxValue);
            _textCache[key] = layout;
        }
        lv.Label = layout;
        var lb = layout.LayoutBounds;
        lv.LabelLayoutBounds = lb;
        lv.LabelBounds = Box.FromCenter(path.LabelAt.X, path.LabelAt.Y, Math.Ceiling(lb.Width) + 10, Math.Ceiling(lb.Height) + 3);
    }

    public static double ArrowLength(double width) => 8 + width * 2.2;
    public static double ArrowHalfWidth(double width) => 4 + width * 1.1;

    // ================================================================== tekst

    private CanvasTextFormat Format(double size, bool bold, bool italic, TextAlign align)
    {
        var key = $"{size}|{bold}|{italic}|{align}";
        if (_formats.TryGetValue(key, out var f)) return f;
        f = new CanvasTextFormat
        {
            FontFamily = FontFamily,
            FontSize = (float)size,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(bold ? 700 : 400) },
            FontStyle = italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            WordWrapping = CanvasWordWrapping.Wrap,
            HorizontalAlignment = align switch
            {
                TextAlign.Left => CanvasHorizontalAlignment.Left,
                TextAlign.Right => CanvasHorizontalAlignment.Right,
                _ => CanvasHorizontalAlignment.Center,
            },
            Options = CanvasDrawTextOptions.EnableColorFont,
        };
        _formats[key] = f;
        return f;
    }

    /// <summary>
    /// Układ tekstu kafelka: najpierw zawijanie do maksymalnej szerokości, potem drugi układ o szerokości najdłuższego
    /// wiersza — wtedy wyrównanie do lewej/środka/prawej działa w obrębie samego tekstu.
    /// </summary>
    private CanvasTextLayout TextLayout(string text, ResolvedNodeStyle s, double wrap)
    {
        var key = $"{text}\u0001{s.FontSize}\u0001{s.Bold}\u0001{s.Italic}\u0001{s.Strike}\u0001{s.Align}\u0001{wrap}";
        _usedText.Add(key);
        if (_textCache.TryGetValue(key, out var layout)) return layout;
        var fmt = Format(s.FontSize, s.Bold, s.Italic, s.Align);
        float width;
        using (var probe = new CanvasTextLayout(Device, text, fmt, (float)wrap, float.MaxValue))
            width = (float)Math.Ceiling(probe.LayoutBounds.Width) + 1;
        layout = new CanvasTextLayout(Device, text, fmt, Math.Max(1, width), float.MaxValue);
        if (s.Strike && text.Length > 0) layout.SetStrikethrough(0, text.Length, true);
        _textCache[key] = layout;
        return layout;
    }

    /// <summary>Szerokość i wysokość tekstu w kafelku (do pola edycji — liczone tak samo jak przy rysowaniu).</summary>
    public (double W, double H) MeasureText(string text, ResolvedNodeStyle s, double wrap)
    {
        var l = TextLayout(text, s, wrap);
        return (Math.Ceiling(l.LayoutBounds.Width), Math.Max(Math.Ceiling(l.LayoutBounds.Height), NodeMetrics.LineHeight(s.FontSize)));
    }

    private static string IconFont
    {
        get
        {
            if (_iconFont is null)
            {
                var families = CanvasTextFormat.GetSystemFontFamilies();
                _iconFont = families.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            }
            return _iconFont;
        }
    }

    // ================================================================== rysowanie

    private static readonly CanvasStrokeStyle Round = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };
    private static readonly CanvasStrokeStyle DashThin = new() { CustomDashStyle = [4f, 2.5f], LineJoin = CanvasLineJoin.Round };
    private static readonly CanvasStrokeStyle DashThick = new() { CustomDashStyle = [2.5f, 1.6f], LineJoin = CanvasLineJoin.Round };
    private static readonly CanvasStrokeStyle DotThin = new() { CustomDashStyle = [0.01f, 2.2f], DashCap = CanvasCapStyle.Round };
    private static readonly CanvasStrokeStyle DotThick = new() { CustomDashStyle = [0.01f, 1.6f], DashCap = CanvasCapStyle.Round };
    private static readonly CanvasStrokeStyle Solid = new() { LineJoin = CanvasLineJoin.Round };

    private static readonly Color LabelLight = C(Rgba.Parse("#E4E1EC", Rgba.White));
    private static readonly Color LabelDark = C(Rgba.Parse("#3A3744", Rgba.Black));

    public static CanvasStrokeStyle Stroke(LinePattern p, double width) => p switch
    {
        LinePattern.Dashed => width >= 4 ? DashThick : DashThin,
        LinePattern.Dotted => width >= 4 ? DotThick : DotThin,
        _ => Solid,
    };

    public static Color C(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);
    public static Vector2 V2(Vec v) => new((float)v.X, (float)v.Y);
    public static Rect R(Box b) => new(b.X, b.Y, b.Width, b.Height);

    public static CanvasGeometry PathGeometry(IReadOnlyList<Cubic> curves)
    {
        using var pb = new CanvasPathBuilder(Device);
        pb.BeginFigure(V2(curves[0].P0));
        foreach (var c in curves) pb.AddCubicBezier(V2(c.P1), V2(c.P2), V2(c.P3));
        pb.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(pb);
    }

    /// <summary>Rysuje całą mapę (to, co przecina <paramref name="visible"/>). <paramref name="zoom"/> decyduje o szczegółach.</summary>
    public void Draw(CanvasDrawingSession ds, Box visible, double zoom)
    {
        var sw = Profiler is null ? null : System.Diagnostics.Stopwatch.StartNew();
        int nb = 0, nl = 0, nn = 0;
        foreach (var b in Branches)
            if (b.Geometry is not null && b.Bounds.Inflate(b.Width).Intersects(visible))
            {
                ds.DrawCachedGeometry(b.Stroke.Get(b.Geometry, (float)b.Width, Round, zoom), C(b.Color));
                nb++;
            }
        double tb = sw?.Elapsed.TotalMilliseconds ?? 0;

        foreach (var lv in Links.Values)
            if (lv.Style is not null && lv.PaintBounds.Inflate(20).Intersects(visible))
            {
                DrawLink(ds, lv, zoom);
                nl++;
            }
        double tl = sw?.Elapsed.TotalMilliseconds ?? 0;

        foreach (var v in NodeOrder)
            if (v.PaintBounds.Intersects(visible))
            {
                DrawNode(ds, v, zoom);
                nn++;
            }
        if (sw is not null)
            Profiler!($"gałęzie {nb}: {tb:0.0} ms, strzałki {nl}: {tl - tb:0.0} ms, kafelki {nn}: {sw.Elapsed.TotalMilliseconds - tl:0.0} ms");
    }

    /// <summary>Pomiar czasu rysowania (włączany zmienną MAPOMYSLACZ_TRACE).</summary>
    public static Action<string>? Profiler;

    /// <summary>Ile elementów (gałęzi, strzałek, kafelków) przecina obszar — bez rysowania, do wyboru sposobu rysowania.</summary>
    public int CountVisible(Box visible, int stopAt)
    {
        int n = 0;
        foreach (var v in NodeOrder) if (v.Bounds.Intersects(visible) && ++n >= stopAt) return n;
        foreach (var l in Links.Values) if (l.CurveBounds.Intersects(visible) && ++n >= stopAt) return n;
        foreach (var b in Branches) if (b.Bounds.Intersects(visible) && ++n >= stopAt) return n;
        return n;
    }

    /// <summary>
    /// Wzór linii na danym powiększeniu. Przy dużym oddaleniu kreski miałyby ułamek piksela, a każda strzałka
    /// rozpadałaby się na setki odcinków do narysowania — wtedy rysujemy linię ciągłą, nieco jaśniejszą.
    /// </summary>
    private static (CanvasStrokeStyle Style, Color Color) Stroked(LinePattern p, double width, Rgba color, double zoom)
    {
        if (p == LinePattern.Solid) return (Solid, C(color));
        // długość kreski (albo odstępu między kropkami) na ekranie — wzór jest w jednostkach grubości linii
        double unit = p == LinePattern.Dashed ? (width >= 4 ? 2.5 : 4) : (width >= 4 ? 1.6 : 2.2);
        if (width * unit * zoom < 1.5) return (Solid, C(color.WithAlpha((byte)(color.A * 0.6))));
        return (Stroke(p, width), C(color));
    }

    private void DrawLink(CanvasDrawingSession ds, LinkView lv, double zoom)
    {
        var s = lv.Style!;
        var color = C(s.Color);
        if (lv.Geometry is not null)
        {
            var (style, c) = Stroked(s.Pattern, s.Width, s.Color, zoom);
            ds.DrawCachedGeometry(lv.Stroke.Get(lv.Geometry, (float)s.Width, style, zoom), c);
        }
        if (lv.Head is not null) ds.FillGeometry(lv.Head, color);
        if (lv.Tail is not null) ds.FillGeometry(lv.Tail, color);
        if (lv.Label is { } label && lv.LabelBounds is { } lb && s.FontSize * zoom >= 2.5)
        {
            ds.FillRoundedRectangle(R(lb), 4, 4, C(Canvas.WithAlpha(225)));
            var text = Canvas.PrefersLightText ? LabelLight : LabelDark;
            var b = lv.LabelLayoutBounds;
            ds.DrawTextLayout(label, (float)(lb.Center.X - b.Width / 2 - b.X), (float)(lb.Center.Y - b.Height / 2 - b.Y), text);
        }
    }

    public static Vec[] Arrow(Vec tip, Vec dir, double len, double half)
    {
        var b = tip - dir * len;
        var p = dir.Perpendicular;
        return [tip, b + p * half, b - p * half];
    }

    public static void FillTriangle(CanvasDrawingSession ds, Vec[] pts, Color color)
    {
        using var g = CanvasGeometry.CreatePolygon(ds, pts.Select(V2).ToArray());
        ds.FillGeometry(g, color);
    }

    private void DrawNode(CanvasDrawingSession ds, NodeView v, double zoom)
    {
        var s = v.Style;
        double left = v.X - v.Width / 2, top = v.Y - v.Height / 2;
        var box = new Rect(left, top, v.Width, v.Height);
        var fill = C(s.Fill);
        bool border = s.BorderWidth > 0;
        var stroke = Stroked(s.BorderPattern, s.BorderWidth, s.Border, zoom).Style;
        float bw = (float)s.BorderWidth;

        switch (s.Shape)
        {
            case NodeShape.None:
                break;
            case NodeShape.Ellipse:
            {
                var c = new Vector2((float)v.X, (float)v.Y);
                float rx = (float)v.Width / 2, ry = (float)v.Height / 2;
                if (s.Fill.A > 0) ds.FillEllipse(c, rx, ry, fill);
                if (border) ds.DrawEllipse(c, rx - bw / 2, ry - bw / 2, C(s.Border), bw, stroke);
                break;
            }
            case NodeShape.Diamond or NodeShape.Hexagon when v.ShapeGeometry is not null:
            {
                var o = new Vector2((float)left, (float)top);
                if (s.Fill.A > 0) ds.FillGeometry(v.ShapeGeometry, o, fill);
                if (border) ds.DrawGeometry(v.ShapeGeometry, o, C(s.Border), bw, stroke);
                break;
            }
            default:
            {
                float radius = (float)(s.Shape switch
                {
                    NodeShape.Pill => v.Height / 2,
                    NodeShape.Rectangle => 2,
                    _ => Math.Min(v.Height / 2, 5 + s.FontSize * 0.25),
                });
                if (s.Fill.A > 0) ds.FillRoundedRectangle(box, radius, radius, fill);
                if (border)
                {
                    var inner = new Rect(left + bw / 2, top + bw / 2, Math.Max(0, v.Width - bw), Math.Max(0, v.Height - bw));
                    ds.DrawRoundedRectangle(inner, Math.Max(0, radius - bw / 2), Math.Max(0, radius - bw / 2), C(s.Border), bw, stroke);
                }
                break;
            }
        }

        bool detailed = s.FontSize * zoom >= 2.5; // przy dużym oddaleniu tekst byłby nieczytelną kreską
        for (int i = 0; i < v.Icons.Count; i++)
        {
            double x = left + v.IconsX + i * (v.IconSize + NodeMetrics.IconGap);
            DrawMarker(ds, v.Icons[i], new Rect(x, top + v.IconsY, v.IconSize, v.IconSize), detailed);
        }

        if (v.Text is { } layout && !v.Editing)
        {
            if (detailed)
            {
                var lb = v.TextBounds;
                ds.DrawTextLayout(layout, (float)(left + v.TextX - lb.X), (float)(top + v.TextY - lb.Y), C(s.TextColor));
            }
            else
            {
                ds.FillRectangle(new Rect(left + v.TextX, v.Y - s.FontSize * 0.25, v.TextW, s.FontSize * 0.5), C(s.TextColor.WithAlpha(90)));
            }
        }

        if (v.BadgeBounds is { } badge)
        {
            ds.FillRoundedRectangle(R(badge), 9, 9, C(s.BranchColor));
            if (detailed)
                DrawCentered(ds, v.BadgeText!, badge, 11, true, Microsoft.UI.Colors.White, FontFamily);
        }
        if (v.HasNote && detailed)
            DrawMark(ds, new Vec(left + v.Width - 3, top + 1), "", Rgba.Parse("#FFF4B8", Rgba.White), Rgba.Parse("#C9A400", Rgba.Black), Rgba.Parse("#6B5600", Rgba.Black));
        if (v.HasLink && detailed)
            DrawMark(ds, new Vec(left + 5, top + 1), "", Rgba.Parse("#DCEBFF", Rgba.White), Rgba.Parse("#0090FF", Rgba.Black), Rgba.Parse("#0B4F9C", Rgba.Black));
    }

    private static void DrawMark(CanvasDrawingSession ds, Vec center, string glyph, Rgba fill, Rgba border, Rgba ink)
    {
        var c = V2(center);
        ds.FillCircle(c, 9, C(fill));
        ds.DrawCircle(c, 8.5f, C(border), 1);
        DrawCentered(ds, glyph, Box.FromCenter(center.X, center.Y, 18, 18), 10, false, C(ink), IconFont);
    }

    private static readonly Dictionary<string, CanvasTextFormat> SmallFormats = [];
    private static readonly Dictionary<string, CanvasTextLayout> SmallLayouts = [];

    /// <summary>
    /// Krótki napis wyśrodkowany w prostokącie (cyfra priorytetu, emoji, „+N”, ikona znaczka). Układy są zapamiętywane —
    /// DrawText tworzyłby je od nowa w każdej klatce, co przy setkach znaczników jest wyraźnie wolniejsze.
    /// </summary>
    private static void DrawCentered(CanvasDrawingSession ds, string text, Box box, double size, bool bold, Color color, string font)
    {
        var key = $"{font}|{size}|{bold}|{box.Width}|{box.Height}|{text}";
        if (!SmallLayouts.TryGetValue(key, out var layout))
        {
            var fkey = $"{font}|{size}|{bold}";
            if (!SmallFormats.TryGetValue(fkey, out var f))
            {
                SmallFormats[fkey] = f = new CanvasTextFormat
                {
                    FontFamily = font,
                    FontSize = (float)size,
                    FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(bold ? 700 : 400) },
                    HorizontalAlignment = CanvasHorizontalAlignment.Center,
                    VerticalAlignment = CanvasVerticalAlignment.Center,
                    Options = CanvasDrawTextOptions.EnableColorFont,
                    WordWrapping = CanvasWordWrapping.NoWrap,
                };
            }
            if (SmallLayouts.Count > 2000)
            {
                foreach (var l in SmallLayouts.Values) l.Dispose();
                SmallLayouts.Clear();
            }
            SmallLayouts[key] = layout = new CanvasTextLayout(Device, text, f, (float)box.Width, (float)box.Height);
        }
        ds.DrawTextLayout(layout, (float)box.X, (float)box.Y, color);
    }

    /// <summary>Znacznik: priorytet (kółko z cyfrą), postęp (wykres kołowy) albo emoji.</summary>
    public static void DrawMarker(CanvasDrawingSession ds, string id, Rect r, bool detailed)
    {
        var c = new Vector2((float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2));
        float rad = (float)r.Width / 2;
        var box = new Box(r.X, r.Y, r.Width, r.Height);
        if (Markers.PriorityValue(id) is var p and > 0)
        {
            ds.FillCircle(c, rad, C(Markers.PriorityColors[Math.Clamp(p, 1, 5) - 1]));
            if (detailed) DrawCentered(ds, p.ToString(), box with { Y = box.Y - 0.5 }, r.Width * 0.62, true, Microsoft.UI.Colors.White, FontFamily);
            return;
        }
        if (id.StartsWith("progress-", StringComparison.Ordinal))
        {
            int v = Markers.ProgressValue(id);
            var green = C(Rgba.Parse("#30A46C", Rgba.Black));
            if (v >= 100)
            {
                ds.FillCircle(c, rad, green);
                using var pb = new CanvasPathBuilder(ds);
                pb.BeginFigure(c + new Vector2(-rad * 0.45f, 0));
                pb.AddLine(c + new Vector2(-rad * 0.1f, rad * 0.35f));
                pb.AddLine(c + new Vector2(rad * 0.48f, -rad * 0.35f));
                pb.EndFigure(CanvasFigureLoop.Open);
                using var check = CanvasGeometry.CreatePath(pb);
                ds.DrawGeometry(check, Microsoft.UI.Colors.White, Math.Max(1.5f, rad * 0.22f), Round);
                return;
            }
            ds.FillCircle(c, rad, Microsoft.UI.Colors.White);
            ds.DrawCircle(c, rad - 0.75f, green, 1.5f);
            if (v > 0)
            {
                float inner = rad - 2.5f;
                double angle = v / 100.0 * Math.PI * 2;
                var start = c + new Vector2(0, -inner);
                var end = c + new Vector2((float)(Math.Sin(angle) * inner), (float)(-Math.Cos(angle) * inner));
                using var pb = new CanvasPathBuilder(ds);
                pb.BeginFigure(c);
                pb.AddLine(start);
                pb.AddArc(end, inner, inner, 0, CanvasSweepDirection.Clockwise, v > 50 ? CanvasArcSize.Large : CanvasArcSize.Small);
                pb.EndFigure(CanvasFigureLoop.Closed);
                using var pie = CanvasGeometry.CreatePath(pb);
                ds.FillGeometry(pie, green);
            }
            return;
        }
        if (detailed) DrawCentered(ds, Markers.Find(id).Glyph, box.Inflate(r.Width * 0.15) with { Y = box.Y - r.Height * 0.12 }, r.Width * 0.92, false, Microsoft.UI.Colors.Black, EmojiFont);
        else ds.FillCircle(c, rad * 0.8f, Color.FromArgb(120, 128, 128, 128));
    }

    // ================================================================== granice i trafianie

    public Box? ContentBounds()
    {
        IEnumerable<Box> boxes = NodeOrder.Select(v => v.Bounds);
        boxes = boxes.Concat(Links.Values.Select(l => l.PaintBounds));
        boxes = boxes.Concat(NodeOrder.Where(v => v.BadgeBounds is not null).Select(v => v.BadgeBounds!.Value));
        return Box.Union(boxes);
    }

    public NodeView? NodeAt(Vec w, string? except = null)
    {
        for (int i = NodeOrder.Count - 1; i >= 0; i--)
        {
            var v = NodeOrder[i];
            if (v.Id != except && v.HitTest(w)) return v;
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var l in _textCache.Values) l.Dispose();
        _textCache.Clear();
        foreach (var v in Nodes.Values) v.ShapeGeometry?.Dispose();
        foreach (var l in Links.Values)
        {
            l.Geometry?.Dispose();
            l.Head?.Dispose();
            l.Tail?.Dispose();
        }
        foreach (var b in Branches) b.Geometry?.Dispose();
        foreach (var f in _formats.Values) f.Dispose();
        _formats.Clear();
        Nodes.Clear();
        Links.Clear();
        Branches.Clear();
        NodeOrder.Clear();
    }
}
