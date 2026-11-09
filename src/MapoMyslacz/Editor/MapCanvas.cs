using System.Diagnostics;
using System.Numerics;
using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Model;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using static MapoMyslacz.Editor.MapScene;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace MapoMyslacz.Editor;

/// <summary>
/// Nieskończone płótno z mapą myśli. Całą mapę rysuje Win2D na jednej powierzchni (<see cref="MapScene"/>),
/// rysowane jest tylko to, co widać — dzięki temu przesuwanie, powiększanie i zaznaczanie są płynne także
/// przy tysiącach kafelków. Elementy XAML są tylko dwa: powierzchnia rysowania i warstwa z polem edycji tekstu.
/// Wszystkie obliczenia są w układzie mapy, a ekran to widok na nią (skala + przesunięcie).
/// </summary>
public sealed partial class MapCanvas : Grid
{
    public const double MinZoom = 0.05, MaxZoom = 4;
    internal static readonly Rgba Accent = Rgba.Parse("#7C3AED", Rgba.Black);

    private MapSession _session = new();
    private readonly CanvasControl _canvas = new() { UseSharedDevice = true };
    private readonly XCanvas _editorLayer = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly CompositeTransform _editorTransform = new();
    private readonly MapScene _scene = new();
    private double _zoom = 1;
    private Vec _pan;
    private Rgba _canvasColor = Styles.CanvasBackground;
    private bool _viewInitialized;

    private Dictionary<string, NodeView> _nodes => _scene.Nodes;
    private Dictionary<string, LinkView> _links => _scene.Links;

    /// <summary>Zmienił się widok (powiększenie/przesunięcie).</summary>
    public event Action? ViewChanged;

    /// <summary>Krótki komunikat dla użytkownika (np. „Skopiowano 3 kafelki”).</summary>
    public event Action<string>? Notify;

    /// <summary>Podpowiedź na pasku stanu zależna od tego, co się dzieje (np. tryb łączenia).</summary>
    public event Action<string?>? HintChanged;

    /// <summary>Użytkownik chce otworzyć link kafelka (Ctrl+klik albo znaczek linku).</summary>
    public event Action<Node>? OpenLinkRequested;

    public MapCanvas()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // przezroczyste tło też łapie mysz
        IsTabStop = true;
        UseSystemFocusVisuals = false;

        _canvas.Draw += OnDraw;
        if (TraceDraw is not null) MapScene.Profiler = Trace;
        _canvas.CreateResources += (_, e) =>
        {
            // utrata urządzenia graficznego (np. zmiana karty/sterownika) — geometrie trzeba utworzyć od nowa
            if (e.Reason != CanvasCreateResourcesReason.FirstTime) RebuildAll();
        };
        _editorLayer.RenderTransform = _editorTransform;
        Children.Add(_canvas);
        Children.Add(_editorLayer);

        SizeChanged += (_, e) =>
        {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            if (!_viewInitialized && e.NewSize.Width > 0) InitializeView();
            _canvas.Invalidate();
        };
        ActualThemeChanged += (_, _) => Rebuild();
        Bind(_session);
        InitInput();
    }

    /// <summary>Zwalnia powierzchnię Win2D przy zamykaniu okna.</summary>
    public void Shutdown()
    {
        DropSceneCache();
        _canvas.RemoveFromVisualTree();
        _scene.Dispose();
    }

    public MapSession Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            CancelInteraction();
            CommitEdit();
            _session.Changed -= OnSessionChanged;
            _session.SelectionChanged -= OnSelectionChanged;
            _session = value;
            DropSceneCache();
            _scene.Dispose();
            if (_hoverHint is not null)
            {
                _hoverHint = null;
                HintChanged?.Invoke(null);
            }
            Bind(value);
            _viewInitialized = false;
            if (ActualWidth > 0) InitializeView();
        }
    }

    private void Bind(MapSession s)
    {
        s.Changed += OnSessionChanged;
        s.SelectionChanged += OnSelectionChanged;
        Rebuild();
    }

    private void InitializeView()
    {
        _viewInitialized = true;
        if (_session.InitialView is { } v) SetView(v);
        else FitToContent();
    }

    private void OnSessionChanged()
    {
        if (_editor is not null && _session.IsBatching) return;
        Rebuild();
    }

    private void OnSelectionChanged() => UpdateOverlay();

    // ================================================================== widok

    public double Zoom => _zoom;

    /// <summary>Kolor płótna: z mapy albo zależny od motywu (jasny/ciemny).</summary>
    public Rgba CanvasColor => _canvasColor;

    internal Vec ToWorld(Point p) => new((p.X - _pan.X) / _zoom, (p.Y - _pan.Y) / _zoom);
    internal Point ToScreen(Vec w) => new(w.X * _zoom + _pan.X, w.Y * _zoom + _pan.Y);

    private Point ViewCenter => new(ActualWidth / 2, ActualHeight / 2);

    private Box VisibleWorld => Box.FromCorners(ToWorld(new Point(0, 0)), ToWorld(new Point(ActualWidth, ActualHeight)));

    /// <summary>Bieżący widok do zapisania w pliku.</summary>
    public ViewState CurrentView
    {
        get
        {
            var c = ToWorld(ViewCenter);
            return new ViewState { X = Math.Round(c.X, 1), Y = Math.Round(c.Y, 1), Zoom = Math.Round(_zoom, 3) };
        }
    }

    private void ApplyTransform()
    {
        _editorTransform.ScaleX = _editorTransform.ScaleY = _zoom;
        _editorTransform.TranslateX = _pan.X;
        _editorTransform.TranslateY = _pan.Y;
        UpdateOverlay();
        ViewChanged?.Invoke();
    }

    public void SetView(ViewState v)
    {
        _zoom = Math.Clamp(v.Zoom <= 0 ? 1 : v.Zoom, MinZoom, MaxZoom);
        _pan = new Vec(ActualWidth / 2 - v.X * _zoom, ActualHeight / 2 - v.Y * _zoom);
        ApplyTransform();
    }

    public void ZoomAt(Point screen, double zoom)
    {
        var w = ToWorld(screen);
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _pan = new Vec(screen.X - w.X * _zoom, screen.Y - w.Y * _zoom);
        ApplyTransform();
    }

    public void ZoomBy(double factor) => ZoomAt(ViewCenter, _zoom * factor);

    public void ZoomTo(double zoom) => ZoomAt(ViewCenter, zoom);

    public void PanBy(double dx, double dy)
    {
        _pan = new Vec(_pan.X + dx, _pan.Y + dy);
        ApplyTransform();
    }

    /// <summary>Granice treści mapy (kafelki, strzałki, podpisy) albo wskazanych kafelków.</summary>
    internal Box? ContentBounds(IEnumerable<string>? onlyNodes = null) =>
        onlyNodes is null
            ? _scene.ContentBounds()
            : Box.Union(onlyNodes.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds));

    /// <summary>Pokazuje całą mapę (albo zaznaczenie).</summary>
    public void FitToContent(bool selectionOnly = false)
    {
        if (ActualWidth <= 0) return;
        var bounds = selectionOnly && _session.SelectedNodes.Count > 0 ? ContentBounds(_session.SelectedNodes) : ContentBounds();
        if (bounds is not { } b)
        {
            SetView(new ViewState { X = 0, Y = 0, Zoom = 1 });
            return;
        }
        double margin = 60;
        double zoom = Math.Min((ActualWidth - margin) / Math.Max(1, b.Width), (ActualHeight - margin) / Math.Max(1, b.Height));
        zoom = Math.Clamp(Math.Min(zoom, 1.25), MinZoom, MaxZoom);
        SetView(new ViewState { X = b.Center.X, Y = b.Center.Y, Zoom = zoom });
    }

    /// <summary>Przesuwa widok tak, żeby kafelek był widoczny (z marginesem).</summary>
    public void EnsureVisible(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var v) || ActualWidth <= 0) return;
        var b = v.Bounds.Inflate(40);
        var tl = ToScreen(new Vec(b.Left, b.Top));
        var br = ToScreen(new Vec(b.Right, b.Bottom));
        double dx = 0, dy = 0;
        if (tl.X < 0) dx = -tl.X;
        else if (br.X > ActualWidth) dx = ActualWidth - br.X;
        if (tl.Y < 0) dy = -tl.Y;
        else if (br.Y > ActualHeight) dy = ActualHeight - br.Y;
        if (dx != 0 || dy != 0) PanBy(dx, dy);
    }

    // ================================================================== budowanie sceny

    /// <summary>Synchronizuje scenę z mapą i odświeża rysunek.</summary>
    public void Rebuild()
    {
        var map = _session.Map;
        bool dark = ActualTheme == ElementTheme.Dark;
        _canvasColor = map.Background is { } bg && Rgba.TryParse(bg, out var c)
            ? c
            : dark ? Styles.DarkCanvasBackground : Styles.CanvasBackground;
        _scene.EditingId = _editNodeId;
        _scene.Build(map, _session.Index, _canvasColor);
        _sceneVersion++;
        UpdateOverlay();
    }

    private void RebuildAll()
    {
        DropSceneCache();
        _scene.Dispose();
        Rebuild();
    }

    /// <summary>Po przesunięciu kafelków: nowe pozycje i geometria dotkniętych linii (bez pełnej przebudowy).</summary>
    private void RefreshGeometry(HashSet<string> moved)
    {
        _scene.Move(_session.Index, moved, _session.Map);
        _sceneVersion++;
        UpdateOverlay();
    }

    private void RefreshLink(Link l)
    {
        if (_links.TryGetValue(l.Id, out var lv)) _scene.UpdateLink(l, lv);
        _sceneVersion++;
        UpdateOverlay();
    }

    internal Vec SizeOf(Node n) => _nodes.TryGetValue(n.Id, out var v) ? v.Size : MapOps.EstimatingSizer(n);

    internal NodeSizer Sizer => SizeOf;

    // ================================================================== rysowanie

    private static readonly string? TraceDraw = Environment.GetEnvironmentVariable("MAPOMYSLACZ_TRACE");
    private Vec? _connectHandle;

    /// <summary>Nakładka (zaznaczenie, uchwyty) jest rysowana razem ze sceną — wystarczy odświeżyć.</summary>
    private void UpdateOverlay()
    {
        _connectHandle = null;
        if (_editor is null && _labelEditor is null && _drag == DragMode.None && _session.SingleNode is { } single
            && _nodes.TryGetValue(single.Id, out var nv))
        {
            double px = 1 / _zoom;
            int side = MapScene.ChildrenSide(_session.Index, single);
            var b = nv.Bounds;
            var c = new Vec(side > 0 ? b.Right + 16 * px : b.Left - 16 * px, b.Center.Y);
            if (nv.BadgeBounds is { } badge) c = new Vec(side > 0 ? badge.Right + 14 * px : badge.Left - 14 * px, c.Y);
            _connectHandle = c;
        }
        _canvas.Invalidate();
    }

    // ------------------------------------------------------------------ bitmapa sceny dla bardzo dużych widoków

    /// <summary>Od ilu widocznych elementów scena trafia do bitmapy (przesuwanie i zaznaczanie tylko ją przesuwają).</summary>
    private const int HeavyScene = 700;

    private CanvasRenderTarget? _sceneCache;
    private Box _cacheWorld;
    private double _cacheZoom;
    private int _cacheVersion = -1;
    private int _sceneVersion;

    private void DropSceneCache()
    {
        _sceneCache?.Dispose();
        _sceneCache = null;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var sw = TraceDraw is null ? null : Stopwatch.StartNew();
        var ds = args.DrawingSession;
        ds.Clear(C(_canvasColor));
        var world = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)_pan.X, (float)_pan.Y);
        var view = VisibleWorld;
        var visible = view.Inflate(30 / _zoom);
        // przeciąganie kafelków/strzałek zmienia scenę w każdej klatce — wtedy bitmapa nic by nie dała
        bool editing = _drag is DragMode.MoveNodes or DragMode.BendLink or DragMode.MoveHandle || _editor is not null;
        bool cached = false;
        if (!editing && _scene.CountVisible(visible, HeavyScene) >= HeavyScene)
        {
            if (_sceneCache is null || _cacheZoom != _zoom || _cacheVersion != _sceneVersion || !_cacheWorld.ContainsBox(view))
                RenderSceneCache(sender, view);
            if (_sceneCache is not null)
            {
                var at = ToScreen(new Vec(_cacheWorld.X, _cacheWorld.Y));
                ds.DrawImage(_sceneCache, (float)Math.Round(at.X), (float)Math.Round(at.Y));
                cached = true;
            }
        }
        else if (_sceneCache is not null)
        {
            DropSceneCache();
        }
        ds.Transform = world;
        if (!cached) _scene.Draw(ds, visible, _zoom);
        DrawOverlay(ds);
        ds.Transform = Matrix3x2.Identity;
        if (_drag == DragMode.Marquee) DrawMarquee(ds);
        if (sw is not null) Trace($"draw {sw.Elapsed.TotalMilliseconds:0.0} ms{(cached ? " (z bitmapy)" : "")} ({_nodes.Count} kafelków, {_links.Count} strzałek)");
    }

    /// <summary>Rysuje scenę do bitmapy obejmującej widok z zapasem (po pół ekranu z każdej strony).</summary>
    private void RenderSceneCache(CanvasControl sender, Box view)
    {
        DropSceneCache();
        var area = view.Inflate(Math.Max(view.Width, view.Height) * 0.5);
        double wDip = area.Width * _zoom, hDip = area.Height * _zoom;
        double maxDip = sender.Device.MaximumBitmapSizeInPixels / Math.Max(1, sender.Dpi / 96) * 0.95;
        if (wDip > maxDip || hDip > maxDip)
        {
            area = view.Inflate(Math.Max(0, Math.Min((maxDip / _zoom - view.Width) / 2, (maxDip / _zoom - view.Height) / 2)));
            wDip = area.Width * _zoom;
            hDip = area.Height * _zoom;
        }
        _sceneCache = new CanvasRenderTarget(sender, (float)wDip, (float)hDip);
        using (var cds = _sceneCache.CreateDrawingSession())
        {
            cds.Clear(C(_canvasColor));
            cds.Transform = Matrix3x2.CreateTranslation((float)-area.X, (float)-area.Y) * Matrix3x2.CreateScale((float)_zoom);
            _scene.Draw(cds, area, _zoom);
        }
        _cacheWorld = area;
        _cacheZoom = _zoom;
        _cacheVersion = _sceneVersion;
    }

    private void DrawOverlay(CanvasDrawingSession ds)
    {
        float px = (float)(1 / _zoom);
        var accent = C(Accent);

        foreach (var id in _session.SelectedLinks)
        {
            if (!_links.TryGetValue(id, out var lv) || lv.Geometry is null || lv.Style is null) continue;
            ds.DrawGeometry(lv.Geometry, C(Accent.WithAlpha(90)), (float)lv.Style.Width + 7 * px, RoundCaps);
            if (lv.LabelBounds is { } lb) Outline(ds, lb.Inflate(2 * px), accent, px, 3);
        }

        IEnumerable<string> selected = _session.SelectedNodes;
        if (_drag == DragMode.Marquee) selected = _marqueeBase.Concat(_marqueeHits);
        foreach (var id in selected)
        {
            if (!_nodes.TryGetValue(id, out var v)) continue;
            Outline(ds, v.Bounds.Inflate(4 * px + v.Style.BorderWidth / 2), accent, px, 7);
        }

        // uchwyty krzywizny pojedynczej zaznaczonej strzałki
        if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0
            && _links.TryGetValue(_session.SelectedLinks.First(), out var sl) && sl.Route is { Handle1: { } h1, Handle2: { } h2 } p)
        {
            ds.DrawLine(V2(p.Start), V2(h1), accent, px);
            ds.DrawLine(V2(p.End), V2(h2), accent, px);
            foreach (var h in new[] { h1, h2 })
            {
                ds.FillCircle(V2(h), 5.5f * px, Microsoft.UI.Colors.White);
                ds.DrawCircle(V2(h), 5.5f * px, accent, 2 * px);
            }
        }

        // kółko „+”: klik — dziecko, przeciągnięcie — strzałka
        if (_connectHandle is { } ch)
        {
            ds.FillCircle(V2(ch), 9 * px, accent);
            ds.DrawLine(V2(ch - new Vec(4.5 * px, 0)), V2(ch + new Vec(4.5 * px, 0)), Microsoft.UI.Colors.White, 1.8f * px, RoundCaps);
            ds.DrawLine(V2(ch - new Vec(0, 4.5 * px)), V2(ch + new Vec(0, 4.5 * px)), Microsoft.UI.Colors.White, 1.8f * px, RoundCaps);
        }

        if (_drag == DragMode.Connect && _connectPreview is { } prev)
        {
            using var g = PathGeometry([prev]);
            ds.DrawGeometry(g, accent, 2 * px, PreviewDash);
            FillTriangle(ds, Arrow(prev.P3, prev.TangentAt(1).Normalized(), 10 * px, 5 * px), accent);
            if (_dropTarget is { } dt && _nodes.TryGetValue(dt, out var tv))
                Outline(ds, tv.Bounds.Inflate(5 * px), accent, px, 7, dashed: true);
        }
    }

    private static readonly CanvasStrokeStyle RoundCaps = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };
    private static readonly CanvasStrokeStyle PreviewDash = new() { CustomDashStyle = [4f, 3f] };
    private static readonly CanvasStrokeStyle OutlineDash = new() { CustomDashStyle = [3f, 2f] };

    private static void Outline(CanvasDrawingSession ds, Box b, Windows.UI.Color color, float px, float radius, bool dashed = false)
    {
        if (dashed) ds.DrawRoundedRectangle(R(b), radius * px, radius * px, color, 2 * px, OutlineDash);
        else ds.DrawRoundedRectangle(R(b), radius * px, radius * px, color, 2 * px);
    }

    private void DrawMarquee(CanvasDrawingSession ds)
    {
        var r = MarqueeRect;
        ds.FillRectangle(r, C(Accent.WithAlpha(28)));
        ds.DrawRectangle(r, C(Accent.WithAlpha(200)), 1);
    }

    // ================================================================== trafianie myszką

    private enum HitKind { None, Node, Badge, LinkMark, Link, LinkHandle1, LinkHandle2, ConnectHandle }

    private readonly record struct Hit(HitKind Kind, string? Id = null);

    private Hit HitTest(Vec w)
    {
        double px = 1 / _zoom;
        if (_connectHandle is { } ch && Vec.Distance(w, ch) <= 11 * px && _session.SingleNode is { } sn)
            return new Hit(HitKind.ConnectHandle, sn.Id);

        if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0
            && _links.TryGetValue(_session.SelectedLinks.First(), out var sl) && sl.Route is { } sp)
        {
            if (sp.Handle1 is { } h1 && Vec.Distance(w, h1) <= 8 * px) return new Hit(HitKind.LinkHandle1, sl.Id);
            if (sp.Handle2 is { } h2 && Vec.Distance(w, h2) <= 8 * px) return new Hit(HitKind.LinkHandle2, sl.Id);
        }

        // kafelki od wierzchu (ostatnio dodane są na górze)
        var order = _scene.NodeOrder;
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var v = order[i];
            if (v.BadgeBounds is { } bb && bb.Inflate(2 * px).Contains(w)) return new Hit(HitKind.Badge, v.Id);
            if (v.LinkMarkBounds is { } lm && lm.Inflate(1 * px).Contains(w) && _zoom * v.Style.FontSize >= 2.5) return new Hit(HitKind.LinkMark, v.Id);
            if (v.HitTest(w)) return new Hit(HitKind.Node, v.Id);
        }

        LinkView? best = null;
        foreach (var lv in _links.Values)
            if (lv.HitTest(w, Math.Max(6 * px, (lv.Style?.Width ?? 2) / 2 + 3 * px))) best = lv;
        if (best is not null) return new Hit(HitKind.Link, best.Id);
        return new Hit(HitKind.None);
    }

    internal string? NodeAt(Vec w, string? except = null) => _scene.NodeAt(w, except)?.Id;
}
