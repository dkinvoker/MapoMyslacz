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
    private static readonly Rgba GuideColor = Rgba.Parse("#E5484D", Rgba.Black);

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
            // utrata urządzenia graficznego (np. zmiana karty/sterownika) — geometrie i bitmapy trzeba utworzyć od nowa
            if (e.Reason == CanvasCreateResourcesReason.FirstTime) return;
            MapScene.ResetBitmaps();
            RebuildAll();
        };
        MapScene.ImageLoaded += OnImageLoaded;
        _editorLayer.RenderTransform = _editorTransform;
        Children.Add(_canvas);
        Children.Add(_editorLayer);

        SizeChanged += (_, e) =>
        {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            if (!_viewInitialized && e.NewSize.Width > 0) InitializeView();
            else if (_viewInitialized && e.PreviousSize.Width > 0)
            {
                // środek widoku zostaje w miejscu (zmiana okna, panel właściwości) — inaczej widok zapisany w pliku
                // otwierał się przesunięty o pół szerokości panelu, który pojawia się już po pierwszym układzie
                _pan = new Vec(_pan.X + (e.NewSize.Width - e.PreviousSize.Width) / 2, _pan.Y + (e.NewSize.Height - e.PreviousSize.Height) / 2);
                ApplyTransform();
            }
            _canvas.Invalidate();
        };
        ActualThemeChanged += (_, _) => Rebuild();
        Bind(_session);
        InitInput();
    }

    /// <summary>
    /// Płótno jako element UI Automation (czytniki ekranu, testy interfejsu) — bez tego panel siatki jest w drzewie
    /// dostępności niewidoczny.
    /// </summary>
    protected override Microsoft.UI.Xaml.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new CanvasPeer(this);

    // „partial” jest konieczne: generator C#/WinRT tworzy wtedy tablice metod dla nadpisań (bez tego UI Automation
    // dostaje błędy i urywa drzewo okna)
    private sealed partial class CanvasPeer(MapCanvas owner) : Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override Microsoft.UI.Xaml.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() =>
            Microsoft.UI.Xaml.Automation.Peers.AutomationControlType.Pane;

        protected override string GetClassNameCore() => nameof(MapCanvas);

        protected override string GetNameCore() => "Płótno mapy";

        // dzieci: tylko pola edycji tekstu — element Win2D w drzewie dostępności urywał dalsze elementy okna
        // (panel, zakładki, pasek stanu przestawały być widoczne dla czytników ekranu i testów)
        protected override IList<Microsoft.UI.Xaml.Automation.Peers.AutomationPeer> GetChildrenCore()
        {
            var peers = new List<Microsoft.UI.Xaml.Automation.Peers.AutomationPeer>();
            foreach (var child in owner._editorLayer.Children)
                if (Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(child) is { } p) peers.Add(p);
            return peers;
        }
    }

    /// <summary>Zwalnia powierzchnię Win2D przy zamykaniu okna.</summary>
    public void Shutdown()
    {
        MapScene.ImageLoaded -= OnImageLoaded;
        StopAnimation();
        DropSceneCache();
        DropMinimap();
        _canvas.RemoveFromVisualTree();
        _scene.Dispose();
    }

    public MapSession Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            StopAnimation();
            CancelInteraction();
            CommitEdit();
            _searchHits.Clear();
            _searchCurrent = null;
            ExitFocus();
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
        else FitToContent(animate: false);
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
        StopAnimation();
        SetViewCore(v.X, v.Y, v.Zoom);
    }

    private void SetViewCore(double x, double y, double zoom)
    {
        _zoom = Math.Clamp(zoom <= 0 ? 1 : zoom, MinZoom, MaxZoom);
        _pan = new Vec(ActualWidth / 2 - x * _zoom, ActualHeight / 2 - y * _zoom);
        ApplyTransform();
    }

    public void ZoomAt(Point screen, double zoom)
    {
        StopAnimation();
        var w = ToWorld(screen);
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _pan = new Vec(screen.X - w.X * _zoom, screen.Y - w.Y * _zoom);
        ApplyTransform();
    }

    /// <summary>Powiększenie wokół środka widoku — płynnie (kolejne naciśnięcia sumują się z trwającą animacją).</summary>
    public void ZoomBy(double factor)
    {
        var target = _animTo ?? ExactView;
        AnimateTo(target.X, target.Y, Math.Clamp(target.Zoom * factor, MinZoom, MaxZoom), 160);
    }

    public void ZoomTo(double zoom)
    {
        var c = _animTo ?? ExactView;
        AnimateTo(c.X, c.Y, zoom, 220);
    }

    public void PanBy(double dx, double dy)
    {
        StopAnimation();
        _pan = new Vec(_pan.X + dx, _pan.Y + dy);
        ApplyTransform();
    }

    // ------------------------------------------------------------------ płynne przejścia widoku

    private readonly record struct ViewPoint(double X, double Y, double Zoom);

    private ViewPoint? _animFrom, _animTo;
    private long _animStart;
    private double _animMs;
    private bool _animating;

    private ViewPoint ExactView
    {
        get
        {
            var c = ToWorld(ViewCenter);
            return new ViewPoint(c.X, c.Y, _zoom);
        }
    }

    /// <summary>Płynne przejście widoku (środek w układzie mapy + powiększenie), np. „Pokaż całą mapę”, wyszukiwanie.</summary>
    private void AnimateTo(double x, double y, double zoom, double ms = 300)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (ActualWidth <= 0 || !_viewInitialized || !IsLoaded)
        {
            SetViewCore(x, y, zoom);
            return;
        }
        _animFrom = ExactView;
        _animTo = new ViewPoint(x, y, zoom);
        _animStart = Stopwatch.GetTimestamp();
        _animMs = ms;
        if (_animating) return;
        _animating = true;
        CompositionTarget.Rendering += OnAnimationFrame;
    }

    private void OnAnimationFrame(object? sender, object e)
    {
        if (_animFrom is not { } a || _animTo is not { } b)
        {
            StopAnimation();
            return;
        }
        double t = Math.Min(1, Stopwatch.GetElapsedTime(_animStart).TotalMilliseconds / _animMs);
        double k = 1 - Math.Pow(1 - t, 3); // szybki start, łagodne hamowanie
        // powiększenie w skali logarytmicznej — przybliżanie i oddalanie wyglądają tak samo płynnie
        double zoom = Math.Exp(Math.Log(a.Zoom) + (Math.Log(b.Zoom) - Math.Log(a.Zoom)) * k);
        SetViewCore(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, zoom);
        if (t >= 1) StopAnimation();
    }

    /// <summary>Przerywa płynne przejście (np. gdy użytkownik sam łapie płótno).</summary>
    public void StopAnimation()
    {
        _animFrom = _animTo = null;
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnAnimationFrame;
    }

    /// <summary>Ustawia kafelek na środku widoku (z przybliżeniem, gdy tekst byłby nieczytelnie mały).</summary>
    public void CenterOn(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var v)) return;
        var target = _animTo ?? ExactView;
        double zoom = v.Style.FontSize * target.Zoom < 9 ? Math.Max(target.Zoom, Math.Min(1, 12 / v.Style.FontSize)) : target.Zoom;
        AnimateTo(v.X, v.Y, zoom);
    }

    /// <summary>Granice treści mapy (kafelki, strzałki, podpisy) albo wskazanych kafelków.</summary>
    internal Box? ContentBounds(IEnumerable<string>? onlyNodes = null) =>
        onlyNodes is null
            ? _scene.ContentBounds()
            : Box.Union(onlyNodes.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds));

    /// <summary>Pokazuje całą mapę (albo zaznaczenie) — domyślnie płynnym przejściem.</summary>
    public void FitToContent(bool selectionOnly = false, bool animate = true)
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
        if (animate) AnimateTo(b.Center.X, b.Center.Y, zoom);
        else SetView(new ViewState { X = b.Center.X, Y = b.Center.Y, Zoom = zoom });
    }

    /// <summary>Przesuwa widok tak, żeby kafelek był widoczny (z marginesem) — krótkim płynnym przejściem.</summary>
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
        if (dx == 0 && dy == 0) return;
        var c = ExactView;
        AnimateTo(c.X - dx / _zoom, c.Y - dy / _zoom, _zoom, 180);
    }

    // ================================================================== budowanie sceny

    /// <summary>Tło płótna mapy: własne, potem z motywu, a w motywie domyślnym — jasne albo ciemne jak Windows.</summary>
    internal Rgba CanvasColorFor(MindMap map) =>
        map.Background is { } bg && Rgba.TryParse(bg, out var c)
            ? c
            : MapThemes.Find(map.Theme).Canvas ?? (ActualTheme == ElementTheme.Dark ? Styles.DarkCanvasBackground : Styles.CanvasBackground);

    /// <summary>Synchronizuje scenę z mapą i odświeża rysunek.</summary>
    public void Rebuild()
    {
        var map = _session.Map;
        _canvasColor = CanvasColorFor(map);
        _scene.EditingId = _editNodeId;
        _scene.Build(map, _session.Index, _canvasColor);
        _sceneVersion++;
        UpdateFocusSet();
        UpdateOverlay();
    }

    private void RebuildAll()
    {
        DropSceneCache();
        DropMinimap();
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
        _widthHandle = null;
        if (_editor is null && _labelEditor is null && _drag is DragMode.None or DragMode.ResizeWidth && _session.SingleNode is { } single
            && _nodes.TryGetValue(single.Id, out var nv))
        {
            double px = 1 / _zoom;
            var b = nv.Bounds;
            // uchwyt szerokości w prawym dolnym rogu obrysu zaznaczenia
            double o = 4 * px + nv.Style.BorderWidth / 2;
            _widthHandle = new Vec(b.Right + o, b.Bottom + o);
            if (_drag == DragMode.None)
            {
                int side = MapScene.ChildrenSide(_session.Index, single);
                var c = new Vec(side > 0 ? b.Right + 16 * px : b.Left - 16 * px, b.Center.Y);
                if (nv.BadgeBounds is { } badge) c = new Vec(side > 0 ? badge.Right + 14 * px : badge.Left - 14 * px, c.Y);
                _connectHandle = c;
            }
        }
        _canvas.Invalidate();
    }

    private Vec? _widthHandle;

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
        DrawGrid(ds, new Rect(0, 0, ActualWidth, ActualHeight), _pan);
        var world = Matrix3x2.CreateScale((float)_zoom) * Matrix3x2.CreateTranslation((float)_pan.X, (float)_pan.Y);
        var view = VisibleWorld;
        var visible = view.Inflate(30 / _zoom);
        // przeciąganie kafelków/strzałek zmienia scenę w każdej klatce — wtedy zwykła bitmapa nic by nie dała
        bool editing = DragEditsMap || _editor is not null;
        bool cached = false, movingOnly = false;
        if (DragEditsMap && _heavyDrag)
        {
            // duża scena w trakcie przeciągania: nieruchoma część z bitmapy, na wierzchu tylko to, co się rusza
            if (_dragCache is null || _dragCacheZoom != _zoom || !_dragCacheWorld.ContainsBox(view))
            {
                _dragCache?.Dispose();
                (_dragCache, _dragCacheWorld) = RenderCache(sender, view, (cds, area) => _scene.Draw(cds, area, _zoom, moving: _dragDynamic, movingPart: false));
                _dragCacheZoom = _zoom;
            }
            var at = ToScreen(new Vec(_dragCacheWorld.X, _dragCacheWorld.Y));
            ds.DrawImage(_dragCache, (float)Math.Round(at.X), (float)Math.Round(at.Y));
            cached = movingOnly = true;
        }
        else if (!editing && _scene.CountVisible(visible, HeavyScene) >= HeavyScene)
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
        if (movingOnly) _scene.Draw(ds, visible, _zoom, moving: _dragDynamic, movingPart: true);
        else if (!cached) _scene.Draw(ds, visible, _zoom);
        DrawFocus(ds, view, visible);
        DrawOverlay(ds);
        ds.Transform = Matrix3x2.Identity;
        DrawMinimap(ds);
        if (_drag == DragMode.Marquee) DrawMarquee(ds);
        if (sw is not null) Trace($"draw {sw.Elapsed.TotalMilliseconds:0.0} ms{(cached ? " (z bitmapy)" : "")} ({_nodes.Count} kafelków, {_links.Count} strzałek)");
    }

    private void RenderSceneCache(CanvasControl sender, Box view)
    {
        DropSceneCache();
        (_sceneCache, _cacheWorld) = RenderCache(sender, view, (cds, area) => _scene.Draw(cds, area, _zoom));
        _cacheZoom = _zoom;
        _cacheVersion = _sceneVersion;
    }

    // bitmapa nieruchomej części sceny na czas przeciągania kafelków (duże mapy)
    private CanvasRenderTarget? _dragCache;
    private Box _dragCacheWorld;
    private double _dragCacheZoom;
    private bool _heavyDrag;
    private HashSet<string> _dragDynamic = [];

    private void DropDragCache()
    {
        _heavyDrag = false;
        _dragDynamic = [];
        _dragCache?.Dispose();
        _dragCache = null;
    }

    /// <summary>
    /// Początek zmiany przeciąganiem: na dużej scenie nieruchoma część trafi do bitmapy, a w każdej klatce rysujemy
    /// tylko <paramref name="dynamic"/> (przeciągane kafelki albo wyginaną strzałkę) z tym, co z nimi połączone.
    /// </summary>
    private void PrepareDragCache(HashSet<string> dynamic)
    {
        DropDragCache();
        _dragDynamic = dynamic;
        _heavyDrag = _scene.CountVisible(VisibleWorld.Inflate(30 / _zoom), HeavyScene) >= HeavyScene;
    }

    /// <summary>Rysuje (część) sceny do bitmapy obejmującej widok z zapasem (po pół ekranu z każdej strony), z tłem i siatką.</summary>
    private (CanvasRenderTarget Bitmap, Box World) RenderCache(CanvasControl sender, Box view, Action<CanvasDrawingSession, Box> draw)
    {
        var area = view.Inflate(Math.Max(view.Width, view.Height) * 0.5);
        double wDip = area.Width * _zoom, hDip = area.Height * _zoom;
        double maxDip = sender.Device.MaximumBitmapSizeInPixels / Math.Max(1, sender.Dpi / 96) * 0.95;
        if (wDip > maxDip || hDip > maxDip)
        {
            area = view.Inflate(Math.Max(0, Math.Min((maxDip / _zoom - view.Width) / 2, (maxDip / _zoom - view.Height) / 2)));
            wDip = area.Width * _zoom;
            hDip = area.Height * _zoom;
        }
        var bitmap = new CanvasRenderTarget(sender, (float)wDip, (float)hDip);
        using (var cds = bitmap.CreateDrawingSession())
        {
            cds.Clear(C(_canvasColor));
            DrawGrid(cds, new Rect(0, 0, wDip, hDip), new Vec(-area.X * _zoom, -area.Y * _zoom));
            cds.Transform = Matrix3x2.CreateTranslation((float)-area.X, (float)-area.Y) * Matrix3x2.CreateScale((float)_zoom);
            draw(cds, area);
        }
        return (bitmap, area);
    }

    private void DrawOverlay(CanvasDrawingSession ds)
    {
        float px = (float)(1 / _zoom);
        var accent = C(Accent);
        DrawSearchHits(ds, px);

        if (_session.SelectedBranches.Count > 0)
            foreach (var b in _scene.Branches)
                if (b.Geometry is not null && _session.SelectedBranches.Contains(b.ChildId))
                    ds.DrawGeometry(b.Geometry, C(Accent.WithAlpha(110)), (float)b.Width + 7 * px, RoundCaps);

        foreach (var fv in _scene.Frames)
            if (_session.SelectedFrames.Contains(fv.Id))
                ds.DrawRoundedRectangle(R(fv.Box.Inflate(3 * px)), 12 + 3 * px, 12 + 3 * px, accent, 2.5f * px);

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

        // uchwyt szerokości (przeciągnij — szerokość zawijania tekstu, dwuklik — automatyczna)
        if (_widthHandle is { } wh)
        {
            var r = new Windows.Foundation.Rect(wh.X - 4 * px, wh.Y - 4 * px, 8 * px, 8 * px);
            ds.FillRectangle(r, Microsoft.UI.Colors.White);
            ds.DrawRectangle(r, accent, 1.5f * px);
            if (_drag == DragMode.ResizeWidth && _resizeImage is null && _session.SingleNode is { Width: { } wrap } rn && _nodes.TryGetValue(rn.Id, out var rv))
            {
                // granica zawijania tekstu
                double x = rv.X - rv.Width / 2 + rv.TextX + wrap;
                ds.DrawLine(V2(new Vec(x, rv.Y - rv.Height / 2 - 10 * px)), V2(new Vec(x, rv.Y + rv.Height / 2 + 10 * px)), accent, px, PreviewDash);
            }
        }

        // linie pomocnicze przyciągania
        if (_guides.Count > 0)
        {
            var guide = C(GuideColor);
            foreach (var (a, b) in _guides) ds.DrawLine(V2(a), V2(b), guide, px);
        }

        // ramka, do której trafią upuszczone kafelki
        if (_frameDropBox is { } fb)
        {
            ds.FillRoundedRectangle(R(fb), 10 * px, 10 * px, C(Accent.WithAlpha(30)));
            Outline(ds, fb.Inflate(2 * px), accent, px, 10);
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

    private enum HitKind { None, Node, Badge, LinkMark, Link, LinkHandle1, LinkHandle2, ConnectHandle, WidthHandle, Frame, Branch }

    private readonly record struct Hit(HitKind Kind, string? Id = null);

    private Hit HitTest(Vec w)
    {
        double px = 1 / _zoom;
        if (_connectHandle is { } ch && Vec.Distance(w, ch) <= 11 * px && _session.SingleNode is { } sn)
            return new Hit(HitKind.ConnectHandle, sn.Id);
        if (_widthHandle is { } wh && Math.Abs(w.X - wh.X) <= 7 * px && Math.Abs(w.Y - wh.Y) <= 7 * px && _session.SingleNode is { } wn)
            return new Hit(HitKind.WidthHandle, wn.Id);

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

        // nagłówek ramki wygrywa ze strzałkami, które przez niego przechodzą (za niego chwyta się ramkę)
        for (int i = _scene.Frames.Count - 1; i >= 0; i--)
            if (_scene.Frames[i].Header.Contains(w)) return new Hit(HitKind.Frame, _scene.Frames[i].Id);

        LinkView? best = null;
        foreach (var lv in _links.Values)
            if (lv.HitTest(w, Math.Max(6 * px, (lv.Style?.Width ?? 2) / 2 + 3 * px))) best = lv;
        if (best is not null) return new Hit(HitKind.Link, best.Id);
        // gałęzie drzewa (rodzic → dziecko) — też da się je kliknąć
        foreach (var b in _scene.Branches)
        {
            double tol = Math.Max(6 * px, b.Width / 2 + 3 * px);
            if (b.Geometry is null || !b.Bounds.Inflate(tol).Contains(w)) continue;
            foreach (var curve in b.Curves)
            {
                Vec? prev = null;
                foreach (var q in curve.Flatten(16))
                {
                    if (prev is { } pq && GeometryMath.DistanceToSegment(w, pq, q) <= tol) return new Hit(HitKind.Branch, b.ChildId);
                    prev = q;
                }
            }
        }
        // ramki łapie się za nagłówek albo krawędź — wnętrze to zwykłe płótno (przesuwanie, zaznaczanie)
        if (_scene.FrameAt(w, 6 * px) is { } fv) return new Hit(HitKind.Frame, fv.Id);
        return new Hit(HitKind.None);
    }

    internal string? NodeAt(Vec w, string? except = null) => _scene.NodeAt(w, except)?.Id;
}
