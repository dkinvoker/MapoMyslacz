using System.Numerics;
using MapoMyslacz.Core.Model;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using static MapoMyslacz.Editor.MapScene;

namespace MapoMyslacz.Editor;

/// <summary>Orientacja na dużych mapach: wyniki wyszukiwania, fokus na gałęzi, minimapa.</summary>
public sealed partial class MapCanvas
{
    // ================================================================== wyniki wyszukiwania

    private readonly HashSet<string> _searchHits = [];
    private string? _searchCurrent;
    private static readonly Rgba SearchColor = Rgba.Parse("#F5A524", Rgba.Black);

    /// <summary>Podświetla znalezione kafelki (bieżący wynik mocniej).</summary>
    public void SetSearchHits(IEnumerable<string> ids, string? current)
    {
        _searchHits.Clear();
        foreach (var id in ids) _searchHits.Add(id);
        _searchCurrent = current;
        _canvas.Invalidate();
    }

    private void DrawSearchHits(CanvasDrawingSession ds, float px)
    {
        if (_searchHits.Count == 0) return;
        var color = C(SearchColor);
        foreach (var id in _searchHits)
        {
            if (!_nodes.TryGetValue(id, out var v)) continue;
            bool current = id == _searchCurrent;
            var b = v.Bounds.Inflate((current ? 9 : 7) * px + v.Style.BorderWidth / 2);
            ds.DrawRoundedRectangle(R(b), 9 * px, 9 * px, color, (current ? 4f : 2.5f) * px);
        }
    }

    // ================================================================== fokus na gałęzi

    private string? _focusId;
    private HashSet<string>? _focusSet;

    /// <summary>Kafelek, którego gałąź jest wyróżniona (reszta mapy przygaszona), albo null.</summary>
    public string? FocusedNodeId => _focusId;

    /// <summary>Włączono/wyłączono fokus na gałęzi.</summary>
    public event Action? FocusChanged;

    /// <summary>
    /// „Pokaż tylko tę gałąź”: zaznaczony kafelek z potomkami i kafelkami połączonymi z nimi strzałkami — wyraźnie,
    /// reszta mapy przygaszona.
    /// </summary>
    public void FocusBranch()
    {
        var node = _session.SingleNode ?? _session.SelectedNodeObjects.FirstOrDefault();
        if (node is null)
        {
            Notify?.Invoke("Zaznacz kafelek, którego gałąź chcesz wyróżnić.");
            return;
        }
        _focusId = node.Id;
        UpdateFocusSet();
        FocusChanged?.Invoke();
        if (_focusSet is { } set) FitBox(Box.Union(set.Where(_nodes.ContainsKey).Select(id => _nodes[id].Bounds)));
        _canvas.Invalidate();
    }

    public void ToggleFocus()
    {
        if (_focusId is not null) ExitFocus();
        else FocusBranch();
    }

    public void ExitFocus()
    {
        if (_focusId is null) return;
        _focusId = null;
        _focusSet = null;
        FocusChanged?.Invoke();
        _canvas.Invalidate();
    }

    /// <summary>Po każdej przebudowie: gałąź mogła urosnąć albo kafelek zniknąć.</summary>
    private void UpdateFocusSet()
    {
        if (_focusId is null) return;
        var idx = _session.Index;
        if (idx.Get(_focusId) is not { } n || !_nodes.ContainsKey(n.Id))
        {
            ExitFocus();
            return;
        }
        var set = new HashSet<string> { n.Id };
        foreach (var d in idx.Descendants(n.Id))
            if (_nodes.ContainsKey(d.Id)) set.Add(d.Id);
        // powiązania: kafelki połączone strzałką z gałęzią (np. wszystko, co łączy się z postacią)
        var related = new List<string>();
        foreach (var l in _session.Map.Links)
        {
            if (set.Contains(l.From) && _nodes.ContainsKey(l.To)) related.Add(l.To);
            else if (set.Contains(l.To) && _nodes.ContainsKey(l.From)) related.Add(l.From);
        }
        set.UnionWith(related);
        _focusSet = set;
    }

    private void DrawFocus(CanvasDrawingSession ds, Box view, Box visible)
    {
        if (_focusSet is not { } set) return;
        ds.FillRectangle(R(view.Inflate(view.Width)), C(_canvasColor.WithAlpha(205)));
        _scene.Draw(ds, visible, _zoom, set);
    }

    private void FitBox(Box? box)
    {
        if (box is not { } b || ActualWidth <= 0) return;
        double margin = 80;
        double zoom = Math.Min((ActualWidth - margin) / Math.Max(1, b.Width), (ActualHeight - margin) / Math.Max(1, b.Height));
        AnimateTo(b.Center.X, b.Center.Y, Math.Clamp(Math.Min(zoom, 1.25), MinZoom, MaxZoom));
    }

    // ================================================================== minimapa

    private bool _showMinimap = true;
    private CanvasRenderTarget? _miniBitmap;
    private int _miniVersion = -1;
    private Box _miniWorld;
    private double _miniScale;
    private Rect _miniRect;
    private bool _miniVisible;
    private Box? _contentCache;
    private int _contentVersion = -1;

    /// <summary>Minimapa w prawym dolnym rogu (pokazuje się, gdy mapa nie mieści się w oknie).</summary>
    public bool ShowMinimap
    {
        get => _showMinimap;
        set
        {
            _showMinimap = value;
            _canvas.Invalidate();
        }
    }

    private Box? CachedContent()
    {
        if (_contentVersion != _sceneVersion)
        {
            _contentCache = _scene.ContentBounds();
            _contentVersion = _sceneVersion;
        }
        return _contentCache;
    }

    /// <summary>Położenie i skala minimapy; ukryta, gdy cała mapa jest na ekranie.</summary>
    private void LayoutMinimap()
    {
        _miniVisible = false;
        if (!_showMinimap || ActualWidth < 480 || ActualHeight < 300 || CachedContent() is not { } content) return;
        if (_drag != DragMode.Minimap && VisibleWorld.ContainsBox(content)) return;
        var world = content.Inflate(Math.Max(content.Width, content.Height) * 0.04 + 20);
        const double maxW = 200, maxH = 140;
        double scale = Math.Min(maxW / world.Width, maxH / world.Height);
        double w = Math.Max(60, world.Width * scale), h = Math.Max(40, world.Height * scale);
        _miniScale = scale;
        _miniWorld = Box.FromCenter(world.Center.X, world.Center.Y, w / scale, h / scale);
        _miniRect = new Rect(Math.Round(ActualWidth - w - 16), Math.Round(ActualHeight - h - 16), Math.Round(w), Math.Round(h));
        _miniVisible = true;
    }

    private bool MinimapContains(Point p) => _miniVisible && Inflate(_miniRect, 5).Contains(p);

    private static Rect Inflate(Rect r, double d) => new(r.X - d, r.Y - d, r.Width + 2 * d, r.Height + 2 * d);

    private Rgba MinimapCard => _canvasColor.PrefersLightText ? _canvasColor.Mix(Rgba.White, 0.08) : _canvasColor.Mix(Rgba.Black, 0.035);

    private void DrawMinimap(CanvasDrawingSession ds)
    {
        LayoutMinimap();
        if (!_miniVisible) return;
        bool stale = _miniBitmap is null || _miniVersion != _sceneVersion
                     || Math.Abs(_miniBitmap.Size.Width - _miniRect.Width) > 0.5 || Math.Abs(_miniBitmap.Size.Height - _miniRect.Height) > 0.5;
        // w trakcie przeciągania (kafelków, strzałki) minimapa czeka do puszczenia myszy — na dużej mapie jej
        // narysowanie trwa dłużej niż klatka, a nie musi nadążać za każdym ruchem
        if (stale && (_miniBitmap is null || !DragEditsMap))
        {
            _miniBitmap?.Dispose();
            _miniBitmap = new CanvasRenderTarget(_canvas, (float)_miniRect.Width, (float)_miniRect.Height);
            using (var mds = _miniBitmap.CreateDrawingSession())
            {
                mds.Clear(C(MinimapCard));
                mds.Transform = Matrix3x2.CreateTranslation((float)-_miniWorld.X, (float)-_miniWorld.Y) * Matrix3x2.CreateScale((float)_miniScale);
                _scene.DrawMinimap(mds, _miniScale);
            }
            _miniVersion = _sceneVersion;
        }

        var frame = Inflate(_miniRect, 5);
        var edge = _canvasColor.PrefersLightText ? Rgba.White.WithAlpha(50) : Rgba.Black.WithAlpha(40);
        ds.FillRoundedRectangle(frame, 8, 8, C(MinimapCard));
        ds.DrawRoundedRectangle(frame, 8, 8, C(edge), 1);
        ds.DrawImage(_miniBitmap, (float)_miniRect.X, (float)_miniRect.Y);

        // ramka widoku (przycięta do minimapy)
        var view = VisibleWorld;
        double l = Math.Max(_miniRect.X, _miniRect.X + (view.Left - _miniWorld.X) * _miniScale);
        double t = Math.Max(_miniRect.Y, _miniRect.Y + (view.Top - _miniWorld.Y) * _miniScale);
        double r = Math.Min(_miniRect.Right, _miniRect.X + (view.Right - _miniWorld.X) * _miniScale);
        double b = Math.Min(_miniRect.Bottom, _miniRect.Y + (view.Bottom - _miniWorld.Y) * _miniScale);
        if (r - l >= 1 && b - t >= 1)
        {
            var vr = new Rect(l, t, r - l, b - t);
            ds.FillRectangle(vr, C(Accent.WithAlpha(34)));
            ds.DrawRectangle(vr, C(Accent.WithAlpha(230)), 1.5f);
        }
    }

    /// <summary>Klik/przeciąganie w minimapie: środek widoku w tym miejscu mapy.</summary>
    private void MoveViewToMinimap(Point p)
    {
        double x = _miniWorld.X + (Math.Clamp(p.X, _miniRect.X, _miniRect.Right) - _miniRect.X) / _miniScale;
        double y = _miniWorld.Y + (Math.Clamp(p.Y, _miniRect.Y, _miniRect.Bottom) - _miniRect.Y) / _miniScale;
        StopAnimation();
        SetViewCore(x, y, _zoom);
    }

    private void DropMinimap()
    {
        _miniBitmap?.Dispose();
        _miniBitmap = null;
        _miniVersion = -1;
        _gridBrush?.Dispose();
        _gridBrush = null;
        _gridTile?.Dispose();
        _gridTile = null;
    }

    // ================================================================== siatka kropek

    private bool _showGrid;
    private CanvasRenderTarget? _gridTile;
    private Microsoft.Graphics.Canvas.Brushes.CanvasImageBrush? _gridBrush;
    private int _gridTilePx;
    private Rgba _gridColor;

    /// <summary>Odstęp kropek w układzie mapy (przy oddaleniu co piąta, potem co dwudziesta piąta).</summary>
    public const double GridSpacing = 24;

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            _sceneVersion++; // bitmapa sceny zawiera tło — trzeba ją narysować od nowa
            _canvas.Invalidate();
        }
    }

    /// <summary>
    /// Kropki siatki: jeden kafelek z kropką powielany pędzlem — cały ekran to jedno wypełnienie, niezależnie od
    /// liczby kropek. Kropka ma zawsze ten sam rozmiar na ekranie. <paramref name="origin"/> — gdzie na celu
    /// rysowania leży punkt (0, 0) mapy.
    /// </summary>
    private void DrawGrid(CanvasDrawingSession ds, Rect area, Vec origin)
    {
        if (!_showGrid) return;
        double spacing = GridSpacing;
        while (spacing * _zoom < 14) spacing *= 5;
        double step = spacing * _zoom;
        int tile = Math.Max(8, (int)Math.Round(step));
        var color = _canvasColor.PrefersLightText ? Rgba.White.WithAlpha(52) : Rgba.Black.WithAlpha(46);
        if (_gridBrush is null || _gridTile is null || _gridTilePx != tile || _gridColor != color)
        {
            _gridBrush?.Dispose();
            _gridTile?.Dispose();
            _gridTile = new CanvasRenderTarget(_canvas, tile, tile);
            using (var t = _gridTile.CreateDrawingSession())
            {
                t.Clear(Microsoft.UI.Colors.Transparent);
                t.FillCircle(tile / 2f, tile / 2f, 1.15f, C(color));
            }
            _gridBrush = new Microsoft.Graphics.Canvas.Brushes.CanvasImageBrush(_canvas, _gridTile)
            {
                ExtendX = CanvasEdgeBehavior.Wrap,
                ExtendY = CanvasEdgeBehavior.Wrap,
                SourceRectangle = new Rect(0, 0, tile, tile),
            };
            _gridTilePx = tile;
            _gridColor = color;
        }
        // kafelek ma całe piksele — lekkie skalowanie wyrównuje go do prawdziwego odstępu kropek
        float k = (float)(step / tile);
        _gridBrush.Transform = Matrix3x2.CreateTranslation(-tile / 2f, -tile / 2f) * Matrix3x2.CreateScale(k)
                               * Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        ds.FillRectangle(area, _gridBrush);
    }
}
