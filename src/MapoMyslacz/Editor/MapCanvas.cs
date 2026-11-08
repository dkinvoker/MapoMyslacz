using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace MapoMyslacz.Editor;

/// <summary>
/// Nieskończone płótno z mapą myśli: rysuje kafelki, gałęzie i strzałki, obsługuje przesuwanie widoku,
/// powiększanie, zaznaczanie, przeciąganie i edycję tekstu w miejscu.
/// Układ: świat (kafelki itd.) jest jednym Canvasem przekształcanym przez skalę i przesunięcie, więc
/// wszystkie obliczenia są w układzie mapy, a ekran to tylko widok na nią.
/// </summary>
public sealed partial class MapCanvas : Grid
{
    public const double MinZoom = 0.1, MaxZoom = 4;
    internal static readonly Rgba Accent = Rgba.Parse("#7C3AED", Rgba.Black);

    private MapSession _session = new();
    private readonly XCanvas _exportHost = new();
    private readonly Rectangle _backdrop = new() { IsHitTestVisible = false };
    private readonly XCanvas _world = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly XCanvas _branchLayer = new(), _linkLayer = new(), _nodeLayer = new(), _overlay = new();
    private readonly CompositeTransform _transform = new();
    private readonly Rectangle _marquee = new()
    {
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        StrokeThickness = 1,
    };
    private readonly Dictionary<string, NodeVisual> _nodes = [];
    private readonly Dictionary<string, Microsoft.UI.Xaml.Shapes.Path> _branches = [];
    private readonly Dictionary<string, LinkVisual> _links = [];
    private double _zoom = 1;
    private Vec _pan;
    private Rgba _canvasColor = Styles.CanvasBackground;
    private bool _viewInitialized;

    /// <summary>Zmienił się widok (powiększenie/przesunięcie).</summary>
    public event Action? ViewChanged;

    /// <summary>Krótki komunikat dla użytkownika (np. „Skopiowano 3 kafelki”).</summary>
    public event Action<string>? Notify;

    /// <summary>Podpowiedź na pasku stanu zależna od tego, co się dzieje (np. tryb łączenia).</summary>
    public event Action<string?>? HintChanged;

    public MapCanvas()
    {
        Background = Xaml.Brush(Rgba.Transparent); // przezroczyste tło też łapie mysz
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        AllowDrop = false;

        _marquee.Fill = Xaml.Brush(Accent.WithAlpha(28));
        _marquee.Stroke = Xaml.Brush(Accent.WithAlpha(200));

        _world.RenderTransform = _transform;
        _world.Children.Add(_branchLayer);
        _world.Children.Add(_linkLayer);
        _world.Children.Add(_nodeLayer);
        _world.Children.Add(_overlay);

        // kolejność: płótno eksportu (zasłonięte tłem), tło, świat, prostokąt zaznaczania
        Children.Add(_exportHost);
        Children.Add(_backdrop);
        Children.Add(_world);
        Children.Add(_marquee);

        SizeChanged += (_, e) =>
        {
            Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
            if (!_viewInitialized && e.NewSize.Width > 0) InitializeView();
        };
        ActualThemeChanged += (_, _) => Rebuild();
        Bind(_session);
        InitInput();
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
            ClearVisuals();
            _session = value;
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

    private void ClearVisuals()
    {
        _nodeLayer.Children.Clear();
        _branchLayer.Children.Clear();
        _linkLayer.Children.Clear();
        _overlay.Children.Clear();
        _nodes.Clear();
        _branches.Clear();
        _links.Clear();
    }

    // ================================================================== widok

    public double Zoom => _zoom;

    /// <summary>Kolor płótna: z mapy albo zależny od motywu (jasny/ciemny).</summary>
    public Rgba CanvasColor => _canvasColor;

    internal Vec ToWorld(Point p) => new((p.X - _pan.X) / _zoom, (p.Y - _pan.Y) / _zoom);
    internal Point ToScreen(Vec w) => new(w.X * _zoom + _pan.X, w.Y * _zoom + _pan.Y);

    private Point ViewCenter => new(ActualWidth / 2, ActualHeight / 2);

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
        _transform.ScaleX = _transform.ScaleY = _zoom;
        _transform.TranslateX = _pan.X;
        _transform.TranslateY = _pan.Y;
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

    /// <summary>Granice treści mapy (kafelki, strzałki, podpisy).</summary>
    internal Box? ContentBounds(IEnumerable<string>? onlyNodes = null)
    {
        IEnumerable<Box> boxes = onlyNodes is null
            ? _nodes.Values.Select(v => v.Bounds)
            : onlyNodes.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds);
        if (onlyNodes is null)
        {
            boxes = boxes.Concat(_links.Values.Where(l => l.LabelBounds is not null).Select(l => l.LabelBounds!.Value));
            foreach (var lv in _links.Values)
            {
                if (lv.Route is null) continue;
                // krzywa Béziera leży w otoczce swoich punktów — przybliżamy ją punktami pośrednimi
                boxes = boxes.Concat(lv.Route.Curves.SelectMany(c => c.Flatten(8)).Select(p => new Box(p.X, p.Y, 0, 0)));
            }
        }
        return Box.Union(boxes);
    }

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

    // ================================================================== rysowanie

    /// <summary>Synchronizuje elementy XAML z mapą (tworzy, aktualizuje i usuwa).</summary>
    public void Rebuild()
    {
        var map = _session.Map;
        var idx = _session.Index;
        bool dark = ActualTheme == ElementTheme.Dark;
        _canvasColor = map.Background is { } bg && Rgba.TryParse(bg, out var c)
            ? c
            : dark ? Styles.DarkCanvasBackground : Styles.CanvasBackground;
        _backdrop.Fill = Xaml.Brush(_canvasColor);

        var visible = new HashSet<string>(map.Nodes.Where(idx.IsVisible).Select(n => n.Id));
        foreach (var id in _nodes.Keys.Where(k => !visible.Contains(k)).ToList())
        {
            _nodeLayer.Children.Remove(_nodes[id].Root);
            _nodes.Remove(id);
        }
        foreach (var n in map.Nodes)
        {
            if (!visible.Contains(n.Id)) continue;
            if (!_nodes.TryGetValue(n.Id, out var v))
            {
                v = new NodeVisual(n.Id);
                _nodes[n.Id] = v;
                _nodeLayer.Children.Add(v.Root);
            }
            UpdateNode(idx, n, v);
        }
        RebuildBranches(idx);
        RebuildLinks();
        UpdateOverlay();
    }

    private void UpdateNode(MapIndex idx, Node n, NodeVisual v)
    {
        var style = Styles.Resolve(n.Style, Styles.BranchColorFor(idx, n), _canvasColor);
        int hidden = n.Collapsed ? idx.Descendants(n.Id).Count : 0;
        v.Update(n, style, hidden, ChildrenSide(idx, n));
    }

    /// <summary>Strona, po której rosną dzieci kafelka (do znaczka zwiniętej gałęzi i uchwytu łączenia).</summary>
    private static int ChildrenSide(MapIndex idx, Node n)
    {
        var kids = idx.ChildrenOf(n.Id);
        if (kids.Count > 0) return kids.Average(k => k.X) < n.X ? -1 : 1;
        return idx.ParentOf(n) is null ? 1 : MapOps.SideOf(idx, n);
    }

    private void RebuildBranches(MapIndex idx)
    {
        var want = new HashSet<string>();
        foreach (var n in _session.Map.Nodes)
        {
            if (n.Parent is null || !_nodes.ContainsKey(n.Id) || !_nodes.ContainsKey(n.Parent)) continue;
            want.Add(n.Id);
            if (!_branches.TryGetValue(n.Id, out var path))
            {
                path = new Microsoft.UI.Xaml.Shapes.Path
                {
                    IsHitTestVisible = false,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                _branches[n.Id] = path;
                _branchLayer.Children.Add(path);
            }
            UpdateBranch(idx, n, path);
        }
        foreach (var id in _branches.Keys.Where(k => !want.Contains(k)).ToList())
        {
            _branchLayer.Children.Remove(_branches[id]);
            _branches.Remove(id);
        }
    }

    private void UpdateBranch(MapIndex idx, Node child, Microsoft.UI.Xaml.Shapes.Path path)
    {
        var pv = _nodes[child.Parent!];
        var cv = _nodes[child.Id];
        var curve = LinkRouting.Branch(pv.Outline, cv.Outline);
        path.Data = Xaml.Geometry([curve]);
        path.Stroke = Xaml.Brush(cv.Style!.BranchColor);
        // gałęzie wychodzące z tematu głównego są grubsze
        path.StrokeThickness = idx.Get(child.Parent) is { } parent && parent.Parent is null ? 3 : 2;
    }

    private void RebuildLinks()
    {
        var want = new HashSet<string>();
        foreach (var l in _session.Map.Links)
        {
            if (!_nodes.ContainsKey(l.From) || !_nodes.ContainsKey(l.To)) continue;
            want.Add(l.Id);
            if (!_links.TryGetValue(l.Id, out var lv))
            {
                lv = new LinkVisual(l.Id);
                _links[l.Id] = lv;
                lv.AddTo(_linkLayer);
            }
            UpdateLink(l, lv);
        }
        foreach (var id in _links.Keys.Where(k => !want.Contains(k)).ToList())
        {
            _links[id].RemoveFrom(_linkLayer);
            _links.Remove(id);
        }
    }

    private void UpdateLink(Link l, LinkVisual lv)
    {
        var s = Styles.Resolve(l.Style);
        var path = LinkRouting.Route(l, s.Curve, _nodes[l.From].Outline, _nodes[l.To].Outline);
        lv.Update(l, s, path, _canvasColor);
    }

    /// <summary>Po przesunięciu kafelków: nowe pozycje i geometria dotkniętych linii (bez pełnej przebudowy).</summary>
    private void RefreshGeometry(HashSet<string> moved)
    {
        var idx = _session.Index;
        foreach (var id in moved)
            if (_nodes.TryGetValue(id, out var v) && idx.Get(id) is { } n) v.SetCenter(n.X, n.Y);
        foreach (var (childId, path) in _branches)
        {
            var child = idx.Get(childId);
            if (child?.Parent is null) continue;
            if (moved.Contains(childId) || moved.Contains(child.Parent)) UpdateBranch(idx, child, path);
        }
        foreach (var l in _session.Map.Links)
            if ((moved.Contains(l.From) || moved.Contains(l.To)) && _links.TryGetValue(l.Id, out var lv)) UpdateLink(l, lv);
        UpdateOverlay();
    }

    private void RefreshLink(Link l)
    {
        if (_links.TryGetValue(l.Id, out var lv)) UpdateLink(l, lv);
        UpdateOverlay();
    }

    internal Vec SizeOf(Node n) => _nodes.TryGetValue(n.Id, out var v) ? v.Size : MapOps.EstimatingSizer(n);

    internal NodeSizer Sizer => SizeOf;

    // ================================================================== nakładka: zaznaczenie i uchwyty

    private Vec? _connectHandle;

    private void UpdateOverlay()
    {
        _overlay.Children.Clear();
        _connectHandle = null;
        double px = 1 / _zoom;
        var accent = Xaml.Brush(Accent);

        foreach (var id in _session.SelectedLinks)
        {
            if (!_links.TryGetValue(id, out var lv) || lv.Route is null || lv.Style is null) continue;
            _overlay.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = Xaml.Geometry(lv.Route.Curves),
                Stroke = Xaml.Brush(Accent.WithAlpha(90)),
                StrokeThickness = lv.Style.Width + 7 * px,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            });
            if (lv.LabelBounds is { } lb) AddOutline(lb.Inflate(2 * px), accent, px, 3);
        }

        foreach (var id in _session.SelectedNodes)
        {
            if (!_nodes.TryGetValue(id, out var v)) continue;
            AddOutline(v.Bounds.Inflate(4 * px + (v.Style?.BorderWidth ?? 0) / 2), accent, px, 7);
        }

        // uchwyty krzywizny pojedynczej zaznaczonej strzałki
        if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0
            && _links.TryGetValue(_session.SelectedLinks.First(), out var sl) && sl.Route is { Handle1: { } h1, Handle2: { } h2 } p)
        {
            AddLine(p.Start, h1, accent, px);
            AddLine(p.End, h2, accent, px);
            AddDot(h1, 5.5 * px, accent, px);
            AddDot(h2, 5.5 * px, accent, px);
        }

        // uchwyt „+” do przeciągania nowej strzałki / dodawania dziecka
        if (_editor is null && _drag == DragMode.None && _session.SingleNode is { } single && _nodes.TryGetValue(single.Id, out var nv))
        {
            int side = ChildrenSide(_session.Index, single);
            var b = nv.Bounds;
            var c = new Vec(side > 0 ? b.Right + 16 * px : b.Left - 16 * px, b.Center.Y);
            if (nv.BadgeBounds is { } badge) c = new Vec(side > 0 ? badge.Right + 14 * px : badge.Left - 14 * px, c.Y);
            _connectHandle = c;
            double r = 9 * px;
            var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = accent, IsHitTestVisible = false };
            XCanvas.SetLeft(dot, c.X - r);
            XCanvas.SetTop(dot, c.Y - r);
            _overlay.Children.Add(dot);
            var white = Xaml.Brush(Rgba.White);
            AddLine(c - new Vec(4.5 * px, 0), c + new Vec(4.5 * px, 0), white, px * 1.8);
            AddLine(c - new Vec(0, 4.5 * px), c + new Vec(0, 4.5 * px), white, px * 1.8);
        }

        if (_drag == DragMode.Connect && _connectPreview is { } prev)
        {
            _overlay.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = Xaml.Geometry([prev]),
                Stroke = accent,
                StrokeThickness = 2 * px,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false,
            });
            var arrow = LinkVisual.Arrow(prev.P3, prev.TangentAt(1).Normalized(), 10 * px, 5 * px);
            _overlay.Children.Add(new Polygon { Points = Xaml.Points(arrow), Fill = accent, IsHitTestVisible = false });
            if (_dropTarget is { } dt && _nodes.TryGetValue(dt, out var tv))
                AddOutline(tv.Bounds.Inflate(5 * px), accent, px, 7, dashed: true);
        }
    }

    private void AddOutline(Box b, Brush stroke, double px, double radius, bool dashed = false)
    {
        var r = new Rectangle
        {
            Width = b.Width,
            Height = b.Height,
            Stroke = stroke,
            StrokeThickness = 2 * px,
            RadiusX = radius * px,
            RadiusY = radius * px,
            IsHitTestVisible = false,
        };
        if (dashed) r.StrokeDashArray = new DoubleCollection { 3, 2 };
        XCanvas.SetLeft(r, b.X);
        XCanvas.SetTop(r, b.Y);
        _overlay.Children.Add(r);
    }

    private void AddLine(Vec a, Vec b, Brush stroke, double thickness) =>
        _overlay.Children.Add(new Line
        {
            X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        });

    private void AddDot(Vec c, double r, Brush stroke, double px)
    {
        var e = new Ellipse
        {
            Width = 2 * r,
            Height = 2 * r,
            Fill = Xaml.Brush(Rgba.White),
            Stroke = stroke,
            StrokeThickness = 2 * px,
            IsHitTestVisible = false,
        };
        XCanvas.SetLeft(e, c.X - r);
        XCanvas.SetTop(e, c.Y - r);
        _overlay.Children.Add(e);
    }

    // ================================================================== trafianie myszką

    private enum HitKind { None, Node, Badge, Link, LinkHandle1, LinkHandle2, ConnectHandle }

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
        for (int i = _session.Map.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _session.Map.Nodes[i];
            if (!_nodes.TryGetValue(n.Id, out var v)) continue;
            if (v.BadgeBounds is { } bb && bb.Inflate(2 * px).Contains(w)) return new Hit(HitKind.Badge, n.Id);
            if (v.HitTest(w)) return new Hit(HitKind.Node, n.Id);
        }

        LinkVisual? best = null;
        foreach (var lv in _links.Values)
            if (lv.HitTest(w, Math.Max(6 * px, (lv.Style?.Width ?? 2) / 2 + 3 * px))) best = lv;
        if (best is not null) return new Hit(HitKind.Link, best.Id);
        return new Hit(HitKind.None);
    }

    internal string? NodeAt(Vec w, string? except = null)
    {
        for (int i = _session.Map.Nodes.Count - 1; i >= 0; i--)
        {
            var n = _session.Map.Nodes[i];
            if (n.Id != except && _nodes.TryGetValue(n.Id, out var v) && v.HitTest(w)) return n.Id;
        }
        return null;
    }
}
