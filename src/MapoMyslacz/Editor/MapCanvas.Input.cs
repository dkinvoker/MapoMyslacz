using MapoMyslacz.Core.Model;
using MapoMyslacz.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;
using Frame = MapoMyslacz.Core.Model.Frame;

namespace MapoMyslacz.Editor;

/// <summary>Mysz i klawiatura: przesuwanie widoku, zaznaczanie, przeciąganie, łączenie, skróty.</summary>
public sealed partial class MapCanvas
{
    private enum DragMode { None, Pending, Pan, Marquee, MoveNodes, BendLink, MoveHandle, Connect, Minimap, ResizeWidth }

    /// <summary>Przycisk myszy jest wciśnięty nad płótnem (klik albo przeciąganie w toku).</summary>
    public bool IsPointerBusy => _drag != DragMode.None;

    /// <summary>Skończyło się przeciąganie albo klik (puszczony przycisk myszy).</summary>
    public event Action? PointerInteractionEnded;

    /// <summary>Tryby, w których przeciąganie zmienia mapę (jedna partia zmian = jeden krok cofania).</summary>
    private bool DragEditsMap => _drag is DragMode.MoveNodes or DragMode.BendLink or DragMode.MoveHandle or DragMode.ResizeWidth;

    // linie pomocnicze przyciągania (układ mapy) i kafelki, do których można przyciągać
    private readonly List<(Vec A, Vec B)> _guides = [];
    private List<Box> _snapTargets = [];
    private Box _grabBox0;
    private double _resizeLeft, _resizeStartWrap;
    private double? _resizeImage;
    private static readonly InputCursor SizeWeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    private const double DragThreshold = 4;

    private DragMode _drag;
    private Hit _pressHit;
    private Point _pressScreen, _lastScreen;
    private Vec _pressWorld;
    private bool _pressShift, _pressCtrl, _pressAlt;
    private HashSet<string> _moving = [];
    private Dictionary<string, Vec> _moveOrigins = [];
    private Vec _bendC1, _bendC2;
    private bool _bendElbow;
    private List<Vec>? _bendVia;
    private Cubic? _connectPreview;
    private string? _dropTarget;
    private HashSet<string> _marqueeBase = [];
    private string? _hoverHint;
    private bool _spaceDown;
    private string? _connectModeFrom;
    private Vec _lastPointerWorld;
    private bool _pointerInside;
    private long _lastClickTime;
    private Point _lastClickPos;

    private static readonly InputCursor ArrowCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private static readonly InputCursor MoveCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
    private static readonly InputCursor HandCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    private static readonly InputCursor CrossCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);

    private void InitInput()
    {
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => FinishDrag(click: false);
        PointerWheelChanged += OnPointerWheel;
        PointerEntered += (_, _) => _pointerInside = true;
        PointerExited += (_, _) => _pointerInside = false;
        RightTapped += OnRightTapped;
        PreviewKeyDown += OnPreviewKeyDown;
        KeyUp += OnKeyUp;
        CharacterReceived += OnCharacterReceived;
        LostFocus += (_, _) => _spaceDown = false;
    }

    internal static readonly string? TraceFile = Environment.GetEnvironmentVariable("MAPOMYSLACZ_TRACE");

    internal static void Trace(string msg)
    {
        if (TraceFile is not null) File.AppendAllText(TraceFile, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>Punkt mapy do wklejania: pod kursorem (jeśli jest nad płótnem) albo środek widoku.</summary>
    internal Vec PastePoint => _pointerInside ? _lastPointerWorld : ToWorld(ViewCenter);

    private void SetCursor(InputCursor? cursor) => ProtectedCursor = cursor ?? ArrowCursor;

    // ================================================================== mysz

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        var props = pt.Properties;
        var pos = pt.Position;
        var w = ToWorld(pos);
        if (TraceFile is not null) Trace($"pressed {pos} świat={w} zoom={_zoom:0.###} drag={_drag} left={props.IsLeftButtonPressed} trafienie={HitTest(w).Kind}");
        Focus(FocusState.Pointer);
        StopAnimation();
        if (_editor is not null || _labelEditor is not null) CommitEdit();
        if (_drag != DragMode.None) return;

        if (props.IsLeftButtonPressed && MinimapContains(pos))
        {
            _drag = DragMode.Minimap;
            MoveViewToMinimap(pos);
            CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        var mods = e.KeyModifiers;
        _pressShift = mods.HasFlag(VirtualKeyModifiers.Shift);
        _pressCtrl = mods.HasFlag(VirtualKeyModifiers.Control);
        _pressAlt = mods.HasFlag(VirtualKeyModifiers.Menu);
        _pressScreen = _lastScreen = pos;
        _pressWorld = w;

        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && _spaceDown))
        {
            _pressHit = new Hit(HitKind.None);
            _drag = DragMode.Pan;
            SetCursor(HandCursors.Get(grab: true) ?? MoveCursor);
            CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        var hit = HitTest(w);
        if (props.IsRightButtonPressed)
        {
            // prawy przycisk: zaznacz to, co pod kursorem (menu pokaże RightTapped)
            if (hit.Kind == HitKind.Node && !_session.SelectedNodes.Contains(hit.Id!)) _session.SelectNode(hit.Id!);
            else if (hit.Kind == HitKind.Link && !_session.SelectedLinks.Contains(hit.Id!)) _session.SelectLink(hit.Id!);
            else if (hit.Kind == HitKind.Frame && !_session.SelectedFrames.Contains(hit.Id!)) _session.SelectFrame(hit.Id!);
            else if (hit.Kind == HitKind.Branch && !_session.SelectedBranches.Contains(hit.Id!)) _session.SelectBranch(hit.Id!);
            return;
        }
        if (!props.IsLeftButtonPressed) return;

        if (_connectModeFrom is { } from)
        {
            if (NodeAt(w, from) is { } target) CreateLink(from, target);
            ExitConnectMode();
            e.Handled = true;
            return;
        }

        if (_linkPickFrom is { } src)
        {
            if (NodeAt(w, src) is { } target && _session.Index.Get(src) is { } sn)
            {
                _session.Edit(_ => sn.Link = "#" + target);
                Notify?.Invoke("Dodano link do kafelka — Ctrl+klik (albo klik w niebieski znaczek) przenosi do niego.");
            }
            ExitLinkPick();
            e.Handled = true;
            return;
        }

        // dwuklik wykrywamy sami: zdarzenie DoubleTapped nie przychodzi, gdy wciśnięcie jest obsłużone (przechwycenie myszy)
        long now = Environment.TickCount64;
        if (now - _lastClickTime <= Native.DoubleClickTime
            && Math.Abs(pos.X - _lastClickPos.X) <= 5 && Math.Abs(pos.Y - _lastClickPos.Y) <= 5)
        {
            _lastClickTime = 0;
            OnDoubleClick(w, hit);
            e.Handled = true;
            return;
        }

        _pressHit = hit;
        switch (hit.Kind)
        {
            case HitKind.Badge:
                _session.Edit(m => { if (_session.Index.Get(hit.Id) is { } n) n.Collapsed = false; });
                e.Handled = true;
                return;
            case HitKind.LinkMark when _session.Index.Get(hit.Id) is { } linked:
                OpenLinkRequested?.Invoke(linked);
                e.Handled = true;
                return;
            case HitKind.Node when _pressCtrl && !_pressShift && _session.Index.Get(hit.Id) is { Link.Length: > 0 } withLink:
                // Ctrl+klik na kafelku z linkiem otwiera link (zamiast dokładać do zaznaczenia)
                OpenLinkRequested?.Invoke(withLink);
                e.Handled = true;
                return;
            case HitKind.Node:
                if (_pressCtrl) _session.Toggle(hit.Id, null);
                else if (_pressShift) _session.SelectNode(hit.Id!, add: true);
                else if (!_session.SelectedNodes.Contains(hit.Id!)) _session.SelectNode(hit.Id!);
                break;
            case HitKind.Link:
                if (_pressCtrl) _session.Toggle(null, hit.Id);
                else if (_pressShift) _session.SelectLink(hit.Id!, add: true);
                else if (!_session.SelectedLinks.Contains(hit.Id!)) _session.SelectLink(hit.Id!);
                break;
            case HitKind.Frame:
                if (_pressCtrl) _session.Toggle(null, null, hit.Id);
                else if (_pressShift) _session.SelectFrame(hit.Id!, add: true);
                else if (!_session.SelectedFrames.Contains(hit.Id!)) _session.SelectFrame(hit.Id!);
                break;
            case HitKind.Branch:
                if (_pressCtrl) _session.Toggle(null, null, null, hit.Id);
                else if (_pressShift) _session.SelectBranch(hit.Id!, add: true);
                else if (!_session.SelectedBranches.Contains(hit.Id!)) _session.SelectBranch(hit.Id!);
                break;
        }
        _drag = DragMode.Pending;
        CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(this).Position;
        var w = ToWorld(pos);
        _lastPointerWorld = w;
        _pointerInside = true;
        if (_drag != DragMode.None) Trace($"moved {pos} drag={_drag}");

        if (_drag == DragMode.None)
        {
            if (MinimapContains(pos)) SetCursor(HandCursor);
            else UpdateHoverCursor(w, e.KeyModifiers);
            return;
        }
        if (_drag == DragMode.Pending)
        {
            if (Math.Abs(pos.X - _pressScreen.X) < DragThreshold && Math.Abs(pos.Y - _pressScreen.Y) < DragThreshold) return;
            StartDrag();
        }

        var d = w - _pressWorld;
        switch (_drag)
        {
            case DragMode.Pan:
                PanBy(pos.X - _lastScreen.X, pos.Y - _lastScreen.Y);
                break;

            case DragMode.Minimap:
                MoveViewToMinimap(pos);
                break;

            case DragMode.ResizeWidth when _session.Index.Get(_pressHit.Id) is { } rn:
            {
                // szerokość zawijania tekstu (albo obrazka); lewa krawędź kafelka zostaje w miejscu
                double wanted = _resizeStartWrap + (w.X - _pressWorld.X);
                if (_resizeImage is not null && rn.Image is { } ri) MapImages.Resize(ri, wanted);
                else rn.Width = Math.Max(24, Math.Round(wanted));
                if (_scene.Relayout(_session.Index, rn) is { } rv) rn.X = Math.Round(_resizeLeft + rv.Width / 2);
                RefreshGeometry([rn.Id]);
                break;
            }

            case DragMode.MoveNodes:
                // Shift: tylko w poziomie albo w pionie
                bool lockX = false, lockY = false;
                if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
                {
                    lockY = Math.Abs(d.X) >= Math.Abs(d.Y);
                    lockX = !lockY;
                    d = lockY ? new Vec(d.X, 0) : new Vec(0, d.Y);
                }
                // przyciąganie do krawędzi i środków sąsiadów (Ctrl — bez przyciągania)
                if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) _guides.Clear();
                else d = Snap(d, lockX, lockY);
                var idx = _session.Index;
                foreach (var id in _moving)
                {
                    if (idx.Get(id) is not { } n) continue;
                    var o = _moveOrigins[id];
                    n.X = Math.Round(o.X + d.X);
                    n.Y = Math.Round(o.Y + d.Y);
                }
                RefreshGeometry(_moving);
                break;

            case DragMode.BendLink when FindLink(_pressHit.Id) is { } bl:
                if (_bendVia is { } via) bl.Via = via.Select(p => LinkRouting.Round(p + d)).ToList();
                else if (_bendElbow) bl.C1 = LinkRouting.Round(_bendC1 + d); // łamana: przesuwa się środkowy odcinek
                else
                {
                    bl.C1 = LinkRouting.Round(_bendC1 + d * (4.0 / 3));
                    bl.C2 = LinkRouting.Round(_bendC2 + d * (4.0 / 3));
                }
                RefreshLink(bl);
                break;

            case DragMode.MoveHandle when FindLink(_pressHit.Id) is { } hl:
                if (_pressHit.Kind == HitKind.LinkHandle1 && _nodes.TryGetValue(hl.From, out var fv)) hl.C1 = LinkRouting.Round(w - fv.Outline.Center);
                else if (_nodes.TryGetValue(hl.To, out var tv)) hl.C2 = LinkRouting.Round(w - tv.Outline.Center);
                RefreshLink(hl);
                break;

            case DragMode.Connect when _pressHit.Id is { } fromId && _nodes.TryGetValue(fromId, out var src):
                _dropTarget = NodeAt(w, fromId);
                var end = _dropTarget is { } t && _nodes.TryGetValue(t, out var tgt) ? tgt.Outline.BoundaryToward(src.Outline.Center) : w;
                var start = src.Outline.BoundaryToward(end);
                _connectPreview = Cubic.Line(start, end);
                UpdateOverlay();
                break;

            case DragMode.Marquee:
                UpdateMarquee(pos);
                break;
        }
        _lastScreen = pos;
    }

    private Link? FindLink(string? id) => id is null ? null : _session.Map.Links.FirstOrDefault(l => l.Id == id);

    private void StartDrag()
    {
        switch (_pressHit.Kind)
        {
            case HitKind.Node when _session.SelectedNodes.Contains(_pressHit.Id!):
            case HitKind.Frame when _session.SelectedFrames.Contains(_pressHit.Id!):
            {
                _drag = DragMode.MoveNodes;
                _session.BeginBatch();
                var idx = _session.Index;
                // Alt: przesuwa tylko zaznaczone kafelki, bez ich gałęzi; ramka zawsze jedzie z całą zawartością
                _moving = MapOps.MovingSet(idx, _session.SelectedNodes, withDescendants: !_pressAlt);
                _moving.UnionWith(MapOps.MovingSet(idx, _session.SelectedFrameObjects.SelectMany(f => f.Nodes), withDescendants: true));
                _moveOrigins = _moving.ToDictionary(id => id, id => new Vec(idx.Get(id)!.X, idx.Get(id)!.Y));
                // przyciąganie: chwycony kafelek do nieruchomych kafelków w pobliżu widoku
                _grabBox0 = _pressHit.Kind == HitKind.Node && _nodes.TryGetValue(_pressHit.Id!, out var grabbed) ? grabbed.Bounds : default;
                var near = VisibleWorld.Inflate(Math.Max(VisibleWorld.Width, VisibleWorld.Height));
                _snapTargets = _scene.NodeOrder.Where(v => !_moving.Contains(v.Id) && v.Bounds.Intersects(near)).Select(v => v.Bounds).ToList();
                PrepareDragCache(_moving);
                SetCursor(MoveCursor);
                UpdateOverlay();
                break;
            }
            case HitKind.WidthHandle when _nodes.TryGetValue(_pressHit.Id!, out var rv):
                _drag = DragMode.ResizeWidth;
                _session.BeginBatch();
                _resizeLeft = rv.X - rv.Width / 2;
                // kafelek z obrazkiem: uchwyt skaluje obrazek, bez obrazka — szerokość zawijania tekstu
                _resizeImage = _session.Index.Get(rv.Id)?.Image is { } resizing ? resizing.Width : null;
                _resizeStartWrap = _resizeImage ?? Math.Max(rv.TextW, 24);
                PrepareDragCache([rv.Id]);
                SetCursor(SizeWeCursor);
                UpdateOverlay();
                break;
            case HitKind.Link when _session.SelectedLinks.Contains(_pressHit.Id!) && FindLink(_pressHit.Id) is { } l
                                   && _nodes.TryGetValue(l.From, out var fv) && _nodes.TryGetValue(l.To, out var tv):
                _drag = DragMode.BendLink;
                _session.BeginBatch();
                // prostą przeciągnięciem zamieniamy na łuk (prostej nie da się wygiąć)
                if (LinkLook(l).Curve == LinkCurve.Straight) SetCurve(_session.Map, l, LinkCurve.Curved);
                _bendVia = l.Via?.ToList();
                _bendElbow = LinkLook(l).Curve == LinkCurve.Elbow && _bendVia is null;
                (_bendC1, _bendC2) = _bendElbow ? (l.C1 ?? Vec.Zero, Vec.Zero) : LinkRouting.Controls(l, fv.Outline.Center, tv.Outline.Center);
                PrepareDragCache([l.Id]);
                SetCursor(MoveCursor);
                break;
            case HitKind.LinkHandle1 or HitKind.LinkHandle2:
                _drag = DragMode.MoveHandle;
                _session.BeginBatch();
                PrepareDragCache([_pressHit.Id!]);
                break;
            case HitKind.ConnectHandle:
                _drag = DragMode.Connect;
                SetCursor(CrossCursor);
                HintChanged?.Invoke("Upuść na kafelku, żeby dodać strzałkę — albo na pustym miejscu, żeby dodać dziecko");
                break;
            case HitKind.None when _pressShift || _pressCtrl:
                _drag = DragMode.Marquee;
                _marqueeBase = [.. _session.SelectedNodes];
                _marqueeHits.Clear();
                UpdateMarquee(_pressScreen);
                break;
            case HitKind.None:
                _drag = DragMode.Pan;
                SetCursor(HandCursors.Get(grab: true) ?? MoveCursor);
                break;
            default:
                _drag = DragMode.Pan;
                SetCursor(HandCursors.Get(grab: true) ?? MoveCursor);
                break;
        }
    }

    /// <summary>
    /// Zaznaczanie prostokątem: w trakcie przeciągania tylko podgląd na płótnie (bez zmiany zaznaczenia sesji, więc
    /// panel właściwości nie przebudowuje się przy każdym ruchu myszy); zaznaczenie trafia do sesji po puszczeniu.
    /// </summary>
    private void UpdateMarquee(Point pos)
    {
        _marqueeEnd = pos;
        var r = MarqueeRect;
        var box = Box.FromCorners(ToWorld(new Point(r.X, r.Y)), ToWorld(new Point(r.X + r.Width, r.Y + r.Height)));
        _marqueeHits.Clear();
        foreach (var v in _scene.NodeOrder)
            if (box.Intersects(v.Bounds)) _marqueeHits.Add(v.Id);
        _canvas.Invalidate();
    }

    /// <summary>
    /// Przyciąganie przeciąganego kafelka: jego lewa krawędź, środek i prawa (tak samo góra/środek/dół) do tych samych
    /// linii sąsiadów, gdy są bliżej niż 6 pikseli ekranu. Zwraca poprawione przesunięcie i ustawia linie pomocnicze.
    /// </summary>
    private Vec Snap(Vec d, bool lockX, bool lockY)
    {
        _guides.Clear();
        if (_snapTargets.Count == 0 || _grabBox0.Width <= 0) return d;
        double tol = 6 / _zoom;
        var b = _grabBox0.Offset(d);
        double bestX = tol, bestY = tol;
        double? sx = null, sy = null;
        Span<double> mx = [b.Left, b.Center.X, b.Right], my = [b.Top, b.Center.Y, b.Bottom];
        foreach (var t in _snapTargets)
        {
            Span<double> tx = [t.Left, t.Center.X, t.Right], ty = [t.Top, t.Center.Y, t.Bottom];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double ddx = tx[j] - mx[i], ddy = ty[j] - my[i];
                    if (!lockX && Math.Abs(ddx) < bestX) { bestX = Math.Abs(ddx); sx = ddx; }
                    if (!lockY && Math.Abs(ddy) < bestY) { bestY = Math.Abs(ddy); sy = ddy; }
                }
        }
        d = new Vec(d.X + (sx ?? 0), d.Y + (sy ?? 0));

        // linie pomocnicze: każda linia kafelka, która pokrywa się z linią któregoś sąsiada
        b = _grabBox0.Offset(d);
        mx = [b.Left, b.Center.X, b.Right];
        my = [b.Top, b.Center.Y, b.Bottom];
        foreach (var x in mx)
        {
            double top = b.Top, bottom = b.Bottom;
            bool any = false;
            foreach (var t in _snapTargets)
                if (Math.Abs(t.Left - x) < 0.5 || Math.Abs(t.Center.X - x) < 0.5 || Math.Abs(t.Right - x) < 0.5)
                {
                    any = true;
                    top = Math.Min(top, t.Top);
                    bottom = Math.Max(bottom, t.Bottom);
                }
            if (any) _guides.Add((new Vec(x, top - 8), new Vec(x, bottom + 8)));
        }
        foreach (var y in my)
        {
            double left = b.Left, right = b.Right;
            bool any = false;
            foreach (var t in _snapTargets)
                if (Math.Abs(t.Top - y) < 0.5 || Math.Abs(t.Center.Y - y) < 0.5 || Math.Abs(t.Bottom - y) < 0.5)
                {
                    any = true;
                    left = Math.Min(left, t.Left);
                    right = Math.Max(right, t.Right);
                }
            if (any) _guides.Add((new Vec(left - 8, y), new Vec(right + 8, y)));
        }
        return d;
    }

    private Point _marqueeEnd;
    private readonly HashSet<string> _marqueeHits = [];

    private Rect MarqueeRect => new(
        Math.Min(_marqueeEnd.X, _pressScreen.X), Math.Min(_marqueeEnd.Y, _pressScreen.Y),
        Math.Abs(_marqueeEnd.X - _pressScreen.X), Math.Abs(_marqueeEnd.Y - _pressScreen.Y));

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == DragMode.None) return;
        _lastPointerWorld = ToWorld(e.GetCurrentPoint(this).Position);
        FinishDrag(click: true);
        ReleasePointerCaptures();
        UpdateHoverCursor(_lastPointerWorld, e.KeyModifiers);
        e.Handled = true;
    }

    private void FinishDrag(bool click)
    {
        var mode = _drag;
        Trace($"finish click={click} drag={mode}");
        if (mode == DragMode.None) return;
        _drag = DragMode.None;
        switch (mode)
        {
            case DragMode.Pending when click:
                _lastClickTime = _pressHit.Kind is HitKind.ConnectHandle or HitKind.Badge ? 0 : Environment.TickCount64;
                _lastClickPos = _pressScreen;
                OnClick();
                break;
            case DragMode.MoveNodes or DragMode.BendLink or DragMode.MoveHandle or DragMode.ResizeWidth:
                _session.EndBatch();
                break;
            case DragMode.Connect when click:
                FinishConnect(_lastPointerWorld);
                break;
            case DragMode.Marquee:
                _session.Select(_marqueeBase.Concat(_marqueeHits));
                _marqueeHits.Clear();
                break;
        }
        _connectPreview = null;
        _dropTarget = null;
        _bendVia = null;
        _guides.Clear();
        _snapTargets = [];
        DropDragCache();
        HintChanged?.Invoke(null);
        UpdateOverlay();
        PointerInteractionEnded?.Invoke();
    }

    /// <summary>Przerywa przeciąganie (Esc, zmiana mapy) i cofa jego skutki.</summary>
    private void CancelInteraction()
    {
        if (DragEditsMap) _session.CancelBatch();
        _drag = DragMode.None;
        _marqueeHits.Clear();
        _guides.Clear();
        DropDragCache();
        _connectPreview = null;
        _dropTarget = null;
        ExitConnectMode();
        ExitLinkPick();
        ReleasePointerCaptures();
        UpdateOverlay();
        PointerInteractionEnded?.Invoke();
    }

    private void OnClick()
    {
        switch (_pressHit.Kind)
        {
            case HitKind.Node when !_pressCtrl && !_pressShift && _session.SelectionCount > 1:
                _session.SelectNode(_pressHit.Id!);
                break;
            case HitKind.Link when !_pressCtrl && !_pressShift && _session.SelectionCount > 1:
                _session.SelectLink(_pressHit.Id!);
                break;
            case HitKind.Frame when !_pressCtrl && !_pressShift && _session.SelectionCount > 1:
                _session.SelectFrame(_pressHit.Id!);
                break;
            case HitKind.Branch when !_pressCtrl && !_pressShift && _session.SelectionCount > 1:
                _session.SelectBranch(_pressHit.Id!);
                break;
            case HitKind.ConnectHandle:
                AddChild();
                break;
            case HitKind.None when !_pressCtrl && !_pressShift:
                _session.ClearSelection();
                break;
        }
    }

    private void FinishConnect(Vec w)
    {
        if (_pressHit.Id is not { } from || _session.Index.Get(from) is null) return;
        if (NodeAt(w, from) is { } target)
        {
            CreateLink(from, target);
            return;
        }
        // upuszczenie na pustym miejscu: nowe dziecko w tym punkcie (z opcją „dzieci jako strzałki” — kafelek ze strzałką)
        CreateAndEdit(m =>
        {
            var n = new Node { Text = MapOps.DefaultText, Parent = ChildrenAsArrows ? null : from, X = Math.Round(w.X), Y = Math.Round(w.Y) };
            m.Nodes.Add(n);
            if (ChildrenAsArrows) MapOps.AddLink(m, from, n.Id);
            return n;
        });
    }

    private void UpdateHoverCursor(Vec w, VirtualKeyModifiers mods)
    {
        if (_connectModeFrom is not null || _linkPickFrom is not null)
        {
            SetCursor(CrossCursor);
            return;
        }
        if (_spaceDown)
        {
            SetCursor(HandCursors.Get(grab: false) ?? MoveCursor);
            return;
        }
        var hit = HitTest(w);
        bool ctrl = mods.HasFlag(VirtualKeyModifiers.Control);
        var linked = hit.Kind is HitKind.Node or HitKind.LinkMark ? _session.Index.Get(hit.Id)?.Link : null;
        string? hint = linked is { Length: > 0 }
            ? $"Link: {DescribeLink(linked)} — {(hit.Kind == HitKind.LinkMark ? "kliknij" : "Ctrl+klik")}, żeby otworzyć"
            : hit.Kind == HitKind.Branch
                ? "Gałąź drzewa (rodzic → dziecko) — klik zaznacza, Delete odłącza kafelek, prawy przycisk — kolor albo zamiana na strzałkę"
                : null;
        if (hint != _hoverHint)
        {
            _hoverHint = hint;
            HintChanged?.Invoke(hint);
        }
        SetCursor(hit.Kind switch
        {
            HitKind.WidthHandle => SizeWeCursor,
            HitKind.Node when ctrl && linked is { Length: > 0 } => HandCursor,
            HitKind.Node or HitKind.Frame => MoveCursor,
            HitKind.Branch => HandCursor,
            HitKind.Link when _session.SelectedLinks.Contains(hit.Id!) => MoveCursor,
            HitKind.Link or HitKind.Badge or HitKind.LinkMark or HitKind.LinkHandle1 or HitKind.LinkHandle2 or HitKind.ConnectHandle => HandCursor,
            _ when mods.HasFlag(VirtualKeyModifiers.Shift) || mods.HasFlag(VirtualKeyModifiers.Control) => CrossCursor,
            _ => HandCursors.Get(grab: false) ?? ArrowCursor,
        });
    }

    /// <summary>Opis linku; dla kafelka tej samej mapy — jego tekst.</summary>
    private string DescribeLink(string link)
    {
        var t = link.Trim();
        if (t.StartsWith('#') && _session.Index.Get(t[1..]) is { } target)
        {
            var text = target.Text.Replace('\n', ' ').Trim();
            return $"kafelek „{(text.Length > 40 ? text[..39] + "…" : text)}”";
        }
        return LinkTargets.Describe(link);
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        int delta = pt.Properties.MouseWheelDelta;
        if (pt.Properties.IsHorizontalMouseWheel) PanBy(-delta, 0);
        else if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift)) PanBy(delta, 0);
        // nad minimapą powiększamy wokół środka widoku (punkt pod kursorem leży wtedy „pod” minimapą)
        else ZoomAt(MinimapContains(pt.Position) ? ViewCenter : pt.Position, _zoom * Math.Pow(1.0015, delta));
        e.Handled = true;
    }

    private void OnDoubleClick(Vec w, Hit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.WidthHandle when _session.Index.Get(hit.Id) is { Image: not null } wi:
                // dwuklik na uchwycie: obrazek w naturalnym rozmiarze
                ResetImageSize(wi);
                break;
            case HitKind.WidthHandle when _session.Index.Get(hit.Id) is { Width: not null } wn:
                // dwuklik na uchwycie: szerokość znów automatyczna
                _session.Edit(_ => wn.Width = null);
                break;
            case HitKind.Node:
                _session.SelectNode(hit.Id!);
                BeginEdit(hit.Id!);
                break;
            case HitKind.Link:
                _session.SelectLink(hit.Id!);
                BeginLabelEdit(hit.Id!);
                break;
            case HitKind.Frame:
                _session.SelectFrame(hit.Id!);
                BeginFrameTitleEdit(hit.Id!);
                break;
            case HitKind.None:
                CreateAndEdit(m => MapOps.AddFloating(m, w));
                break;
        }
    }

    // ================================================================== tryb łączenia (Ctrl+L przy jednym kafelku)

    public void EnterConnectMode()
    {
        if (_session.SingleNode is not { } n) return;
        _connectModeFrom = n.Id;
        SetCursor(CrossCursor);
        HintChanged?.Invoke("Kliknij kafelek, do którego ma prowadzić strzałka (Esc — anuluj)");
    }

    private void ExitConnectMode()
    {
        if (_connectModeFrom is null) return;
        _connectModeFrom = null;
        HintChanged?.Invoke(null);
    }

    // ================================================================== wskazywanie kafelka do linku

    private string? _linkPickFrom;

    /// <summary>Następny klik w kafelek ustawi link zaznaczonego kafelka na niego („#id”).</summary>
    public void EnterLinkPickMode()
    {
        if (_session.SingleNode is not { } n) return;
        CommitEdit();
        ExitConnectMode();
        _linkPickFrom = n.Id;
        SetCursor(CrossCursor);
        Focus(FocusState.Programmatic);
        HintChanged?.Invoke("Kliknij kafelek, do którego ma prowadzić link (Esc — anuluj)");
    }

    private void ExitLinkPick()
    {
        if (_linkPickFrom is null) return;
        _linkPickFrom = null;
        HintChanged?.Invoke(null);
    }

    // ================================================================== klawiatura

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (TraceFile is not null) Trace($"key {e.Key} źródło={e.OriginalSource?.GetType().Name} edytor={_editor is not null} zaznaczone={_session.SelectionCount}");
        if (_editor is not null)
        {
            // pole edycji dopiero powstaje (nie ma jeszcze fokusu) — Enter/Tab/Esc i tak do niego należą
            if (ReferenceEquals(e.OriginalSource, this)) OnEditorKeyDown(this, e);
            return;
        }
        if (_labelEditor is not null)
        {
            if (ReferenceEquals(e.OriginalSource, this)) OnLabelEditorKeyDown(this, e);
            return;
        }
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        bool ctrl = IsDown(VirtualKey.Control), shift = IsDown(VirtualKey.Shift), alt = IsDown(VirtualKey.Menu);
        // sam Alt (np. Alt+← — powrót do poprzedniej mapy, Alt+F4) należy do okna, nie do płótna
        if (alt && !ctrl) return;
        // AltGr = Ctrl+Alt — to polskie litery (ą, ć, ę…), nie skróty
        bool cmd = ctrl && !alt;
        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Tab when shift:
                if (_session.SingleNode is { Parent: { } p }) { _session.SelectNode(p); EnsureVisible(p); }
                break;
            case VirtualKey.Tab or VirtualKey.Insert:
                AddChild();
                break;
            case VirtualKey.Enter:
                if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0) BeginLabelEdit(_session.SelectedLinks.First());
                else AddSibling();
                break;
            case VirtualKey.F2:
                EditSelected();
                break;
            case VirtualKey.Delete or VirtualKey.Back:
                DeleteSelection();
                break;
            case VirtualKey.Escape:
                if (_connectModeFrom is not null || _linkPickFrom is not null || _drag != DragMode.None) CancelInteraction();
                else if (_session.SelectionCount == 0 && _focusId is not null) ExitFocus();
                else _session.ClearSelection();
                break;
            case VirtualKey.Space:
                if (!_spaceDown)
                {
                    _spaceDown = true;
                    SetCursor(HandCursors.Get(grab: false) ?? MoveCursor);
                }
                break;
            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down:
                var dir = e.Key switch
                {
                    VirtualKey.Left => Direction.Left,
                    VirtualKey.Right => Direction.Right,
                    VirtualKey.Up => Direction.Up,
                    _ => Direction.Down,
                };
                if (cmd) Nudge(dir, shift ? 1 : 10);
                else Navigate(dir);
                break;
            case VirtualKey.A when cmd:
                _session.SelectAll();
                break;
            case VirtualKey.C when cmd:
                _ = CopyAsync(cut: false);
                break;
            case VirtualKey.X when cmd:
                _ = CopyAsync(cut: true);
                break;
            case VirtualKey.V when cmd:
                _ = PasteAsync();
                break;
            case VirtualKey.D when cmd:
                Duplicate();
                break;
            case VirtualKey.Z when cmd && shift:
            case VirtualKey.Y when cmd:
                Redo();
                break;
            case VirtualKey.Z when cmd:
                Undo();
                break;
            case VirtualKey.B when cmd:
                ToggleBold();
                break;
            case VirtualKey.I when cmd:
                ToggleItalic();
                break;
            case VirtualKey.L when cmd:
                ConnectSelected();
                break;
            case VirtualKey.G when cmd:
                FrameSelection();
                break;
            case VirtualKey.R when cmd:
                ArrangeSelection();
                break;
            case (VirtualKey)191 when cmd: // „/”
                ToggleCollapse();
                break;
            case VirtualKey.Number0 or VirtualKey.NumberPad0 when cmd:
                FitToContent();
                break;
            case VirtualKey.Number1 or VirtualKey.NumberPad1 when cmd:
                ZoomTo(1);
                break;
            case VirtualKey.Add or (VirtualKey)187 when cmd: // „+” / „=”
                ZoomBy(1.2);
                break;
            case VirtualKey.Subtract or (VirtualKey)189 when cmd: // „-”
                ZoomBy(1 / 1.2);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Space && _spaceDown)
        {
            _spaceDown = false;
            UpdateHoverCursor(_lastPointerWorld, VirtualKeyModifiers.None);
        }
    }

    /// <summary>Pisanie przy zaznaczonym kafelku od razu zaczyna edycję (pierwsza litera zastępuje tekst).</summary>
    private void OnCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        char ch = e.Character;
        if ((_editor ?? _labelEditor) is { } ed)
        {
            // szybkie pisanie zaraz po Tab/Enter: pole edycji jeszcze nie ma fokusu — znaki trafiają do niego
            if (!char.IsControl(ch))
            {
                if (_editorFresh) ed.Text = "";
                _editorFresh = false;
                ed.Text += ch;
                ed.Select(ed.Text.Length, 0);
                e.Handled = true;
            }
            return;
        }
        if (char.IsControl(ch) || ch == ' ') return;
        bool ctrl = IsDown(VirtualKey.Control), alt = IsDown(VirtualKey.Menu);
        if ((ctrl || alt) && !(ctrl && alt)) return;
        if (_session.SingleNode is { } n)
        {
            BeginEdit(n.Id, ch.ToString());
            e.Handled = true;
        }
        else if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0)
        {
            BeginLabelEdit(_session.SelectedLinks.First(), ch.ToString());
            e.Handled = true;
        }
        else if (_session.SingleFrame is { } f)
        {
            BeginFrameTitleEdit(f.Id, ch.ToString());
            e.Handled = true;
        }
    }

    private void Navigate(Direction dir)
    {
        var idx = _session.Index;
        var visible = _nodes.Keys.Select(id => idx.Get(id)).OfType<Node>().ToList();
        if (visible.Count == 0) return;
        var current = _session.SingleNode;
        if (current is null)
        {
            var c = ToWorld(ViewCenter);
            var first = visible.MinBy(n => Vec.Distance(new Vec(n.X, n.Y), c))!;
            _session.SelectNode(first.Id);
            return;
        }
        if (MapOps.Nearest(visible, current, dir) is { } next)
        {
            _session.SelectNode(next.Id);
            EnsureVisible(next.Id);
        }
    }

    // ================================================================== menu kontekstowe

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_drag != DragMode.None) return;
        var pos = e.GetPosition(this);
        var w = ToWorld(pos);
        var hit = HitTest(w);
        var menu = hit.Kind switch
        {
            HitKind.Node or HitKind.Badge => NodeMenu(),
            HitKind.Link => LinkMenu(),
            HitKind.Frame => FrameMenu(),
            HitKind.Branch => BranchMenu(),
            _ => CanvasMenu(w),
        };
        menu.ShowAt(this, new FlyoutShowOptions { Position = pos });
        e.Handled = true;
    }

    private static MenuFlyoutItem Item(string text, string glyph, Action action, string? shortcut = null, bool enabled = true)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph },
            IsEnabled = enabled,
        };
        if (shortcut is not null) item.KeyboardAcceleratorTextOverride = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private MenuFlyout NodeMenu()
    {
        var idx = _session.Index;
        var sel = _session.SelectedNodeObjects.ToList();
        bool single = sel.Count == 1;
        bool anyChildren = sel.Any(n => idx.ChildrenOf(n.Id).Count > 0);
        bool anyParent = sel.Any(n => n.Parent is not null);
        bool allCollapsed = anyChildren && sel.Where(n => idx.ChildrenOf(n.Id).Count > 0).All(n => n.Collapsed);

        var menu = new MenuFlyout();
        if (single && sel[0].Link is { Length: > 0 } link)
        {
            var target = sel[0];
            menu.Items.Add(Item($"Otwórz link: {LinkTargets.Describe(link)}", "", () => OpenLinkRequested?.Invoke(target), "Ctrl+klik"));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        menu.Items.Add(Item("Edytuj tekst", "", EditSelected, "F2", single));
        menu.Items.Add(Item("Dodaj dziecko", "", AddChild, "Tab", single));
        menu.Items.Add(Item("Dodaj rodzeństwo", "", AddSibling, "Enter", single));
        menu.Items.Add(Item(sel.Count == 2 ? "Połącz strzałką" : "Połącz strzałką z…", "", ConnectSelected, "Ctrl+L"));
        menu.Items.Add(new MenuFlyoutSeparator());

        var styles = new MenuFlyoutSubItem { Text = "Styl", Icon = new FontIcon { Glyph = "" } };
        foreach (var p in CustomPresets.Concat(Styles.Presets))
        {
            var preset = p;
            var it = new MenuFlyoutItem { Text = p.Name };
            ToolTipService.SetToolTip(it, p.Description);
            it.Click += (_, _) => ApplyPreset(preset);
            styles.Items.Add(it);
        }
        menu.Items.Add(styles);

        var shapes = new MenuFlyoutSubItem { Text = "Kształt", Icon = new FontIcon { Glyph = "" } };
        foreach (var (shape, name) in ShapeNames)
        {
            var s = shape;
            var it = new MenuFlyoutItem { Text = name };
            it.Click += (_, _) => SetShape(s);
            shapes.Items.Add(it);
        }
        menu.Items.Add(shapes);

        var markers = new MenuFlyoutSubItem { Text = "Znaczniki", Icon = new FontIcon { Glyph = "" } };
        foreach (var group in new[] { Markers.Priorities, Markers.Progress, Markers.Symbols.Take(12).ToList() })
        {
            foreach (var m in group)
            {
                var marker = m;
                var it = new ToggleMenuFlyoutItem
                {
                    Text = marker.Group == MarkerGroup.Symbol ? $"{marker.Glyph}  {marker.Name}" : marker.Name,
                    IsChecked = sel.Count > 0 && sel.All(n => Markers.Has(n, marker.Id)),
                };
                it.Click += (_, _) => ToggleMarker(marker.Id);
                markers.Items.Add(it);
            }
            markers.Items.Add(new MenuFlyoutSeparator());
        }
        markers.Items.Add(Item("Usuń znaczniki", "", () => _session.Edit(_ => sel.ForEach(n => n.Icons = null))));
        menu.Items.Add(markers);
        menu.Items.Add(new MenuFlyoutSeparator());

        if (anyChildren)
        {
            menu.Items.Add(Item(allCollapsed ? "Rozwiń gałąź" : "Zwiń gałąź", allCollapsed ? "" : "", ToggleCollapse, "Ctrl+/"));
            menu.Items.Add(Item("Uporządkuj gałąź", "", ArrangeSelection, "Ctrl+R"));
        }
        if (anyParent) menu.Items.Add(Item("Odłącz od rodzica", "", DetachSelection));
        if (sel.Count >= 2)
        {
            var align = new MenuFlyoutSubItem { Text = "Wyrównaj", Icon = new FontIcon { Glyph = "" } };
            foreach (var (mode, text, glyph) in AlignCommands)
            {
                var m = mode;
                align.Items.Add(Item(text, glyph, () => AlignSelection(m)));
                if (m == AlignMode.Right) align.Items.Add(new MenuFlyoutSeparator());
            }
            align.Items.Add(new MenuFlyoutSeparator());
            align.Items.Add(Item("Rozłóż równo w poziomie", "", () => DistributeSelection(true), enabled: sel.Count >= 3));
            align.Items.Add(Item("Rozłóż równo w pionie", "", () => DistributeSelection(false), enabled: sel.Count >= 3));
            menu.Items.Add(align);
        }
        menu.Items.Add(Item(_focusId is null ? "Pokaż tylko tę gałąź" : "Pokaż całą mapę", "", ToggleFocus, "Ctrl+Shift+F"));
        menu.Items.Add(new MenuFlyoutSeparator());

        if (single)
        {
            var node = sel[0];
            menu.Items.Add(Item("Kopiuj link do kafelka", "", () => CopyNodeLink(node)));
            menu.Items.Add(Item(node.Image is null ? "Wstaw obrazek…" : "Zmień obrazek…", "", RequestPickImage, "Ctrl+V"));
            if (node.Image is not null) menu.Items.Add(Item("Otwórz obrazek", "", () => OpenImage(node)));
        }
        if (sel.Any(n => n.Image is not null)) menu.Items.Add(Item("Usuń obrazek", "", RemoveImages));

        // ramki
        menu.Items.Add(Item("Otocz ramką", "", FrameSelection, "Ctrl+G"));
        var ids = sel.Select(n => n.Id).ToList();
        var frames = _session.Map.Frames ?? [];
        var addable = frames.Where(f => !ids.All(f.Nodes.Contains)).ToList();
        if (addable.Count > 0)
        {
            var add = new MenuFlyoutSubItem { Text = "Dodaj do ramki", Icon = new FontIcon { Glyph = "" } };
            foreach (var f in addable)
            {
                var frame = f;
                add.Items.Add(Item(FrameName(frame), "", () => _session.Edit(_ => MapOps.AddToFrame(frame, ids))));
            }
            menu.Items.Add(add);
        }
        if (frames.Any(f => f.Nodes.Any(ids.Contains)))
            menu.Items.Add(Item("Wyjmij z ramki", "", () => _session.Edit(m => MapOps.RemoveFromFrames(m, ids))));
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item("Kopiuj", "", () => _ = CopyAsync(false), "Ctrl+C"));
        menu.Items.Add(Item("Wytnij", "", () => _ = CopyAsync(true), "Ctrl+X"));
        menu.Items.Add(Item("Duplikuj", "", Duplicate, "Ctrl+D"));
        menu.Items.Add(Item("Usuń", "", DeleteSelection, "Del"));
        return menu;
    }

    /// <summary>Polecenia wyrównania (menu kontekstowe i panel właściwości).</summary>
    internal static readonly (AlignMode Mode, string Text, string Glyph)[] AlignCommands =
    [
        (AlignMode.Left, "Do lewej krawędzi", ""), (AlignMode.CenterX, "Do środka (w poziomie)", ""),
        (AlignMode.Right, "Do prawej krawędzi", ""), (AlignMode.Top, "Do górnej krawędzi", ""),
        (AlignMode.CenterY, "Do środka (w pionie)", ""), (AlignMode.Bottom, "Do dolnej krawędzi", ""),
    ];

    internal static readonly (NodeShape Shape, string Name)[] ShapeNames =
    [
        (NodeShape.Rounded, "Zaokrąglony"), (NodeShape.Rectangle, "Prostokąt"), (NodeShape.Pill, "Owal"),
        (NodeShape.Ellipse, "Elipsa"), (NodeShape.Diamond, "Romb"), (NodeShape.Hexagon, "Sześciokąt"),
        (NodeShape.None, "Bez ramki (sam tekst)"),
    ];

    private MenuFlyout LinkMenu()
    {
        var links = _session.SelectedLinkObjects.ToList();
        bool single = links.Count == 1;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Edytuj podpis", "", () => { if (single) BeginLabelEdit(links[0].Id); }, "F2", single));
        menu.Items.Add(Item("Odwróć kierunek", "", () => _session.Edit(_ => links.ForEach(MapOps.Reverse))));
        var shape = new MenuFlyoutSubItem { Text = "Kształt linii", Icon = new FontIcon { Glyph = "" } };
        foreach (var (curve, name) in CurveNames)
        {
            var c = curve;
            var it = new ToggleMenuFlyoutItem { Text = name, IsChecked = links.All(l => LinkLook(l).Curve == c) };
            it.Click += (_, _) => _session.Edit(m => links.ForEach(l => SetCurve(m, l, c)));
            shape.Items.Add(it);
        }
        menu.Items.Add(shape);
        menu.Items.Add(Item("Przywróć domyślną krzywiznę", "", () => _session.Edit(_ => links.ForEach(l => { l.C1 = l.C2 = null; l.Via = null; }))));
        menu.Items.Add(new MenuFlyoutSeparator());
        bool start = links.All(l => LinkLook(l).ArrowStart);
        bool end = links.All(l => LinkLook(l).ArrowEnd);
        menu.Items.Add(new ToggleMenuFlyoutItem { Text = "Grot na początku", IsChecked = start }.With(i =>
            i.Click += (_, _) => _session.Edit(_ => links.ForEach(l => MapOps.EditStyle(l, s => s.ArrowStart = !start)))));
        menu.Items.Add(new ToggleMenuFlyoutItem { Text = "Grot na końcu", IsChecked = end }.With(i =>
            i.Click += (_, _) => _session.Edit(_ => links.ForEach(l => MapOps.EditStyle(l, s => s.ArrowEnd = !end)))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Usuń", "", DeleteSelection, "Del"));
        return menu;
    }

    /// <summary>Własne style użytkownika (z ustawień) — pokazywane przed gotowymi w menu „Styl”.</summary>
    public IReadOnlyList<StylePreset> CustomPresets { get; set; } = [];

    internal static readonly (LinkCurve Curve, string Name)[] CurveNames =
        [(LinkCurve.Curved, "Zakrzywiona"), (LinkCurve.Straight, "Prosta"), (LinkCurve.Elbow, "Łamana (pod kątem prostym)")];

    /// <summary>Wygląd strzałki z uwzględnieniem motywu mapy i stylu strzałek.</summary>
    internal ResolvedLinkStyle LinkLook(Link l) => MapThemes.ResolveLink(_session.Map, _session.Index, l);

    /// <summary>
    /// Zmiana kształtu linii; przy przejściu na łamaną albo z niej kształt wraca do domyślnego (inne znaczenie punktów).
    /// Kształt równy domyślnemu ze stylu strzałek mapy nie jest zapisywany (zmiana motywu go zmieni).
    /// </summary>
    internal static void SetCurve(MindMap map, Link l, LinkCurve curve)
    {
        var defaults = MapThemes.LinksFor(map);
        var before = l.Style?.Curve ?? defaults.Curve;
        if (before == curve) return;
        if (before == LinkCurve.Elbow || curve == LinkCurve.Elbow) l.C1 = l.C2 = null;
        MapOps.EditStyle(l, s => s.Curve = curve == defaults.Curve ? null : curve);
    }

    internal static string FrameName(Frame f) => string.IsNullOrWhiteSpace(f.Title) ? "Ramka bez nazwy" : f.Title.Trim();

    private MenuFlyout FrameMenu()
    {
        var frames = _session.SelectedFrameObjects.ToList();
        bool single = frames.Count == 1;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Zmień nazwę", "", () => { if (single) BeginFrameTitleEdit(frames[0].Id); }, "F2", single));
        var colors = new MenuFlyoutSubItem { Text = "Kolor", Icon = new FontIcon { Glyph = "" } };
        foreach (var (color, name) in Styles.FrameColors)
        {
            var c = color;
            var it = new ToggleMenuFlyoutItem
            {
                Text = name,
                IsChecked = frames.Count > 0 && frames.All(f => Styles.FrameColor(f) == Rgba.Parse(c, Rgba.Black)),
            };
            it.Click += (_, _) => _session.Edit(_ => frames.ForEach(f => f.Color = c == Styles.FrameColors[0].Color ? null : c));
            colors.Items.Add(it);
        }
        menu.Items.Add(colors);
        menu.Items.Add(Item("Zaznacz kafelki w ramce", "", () => _session.Select(frames.SelectMany(f => f.Nodes))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Usuń ramkę (kafelki zostają)", "", DeleteSelection, "Del"));
        menu.Items.Add(Item("Usuń ramkę razem z kafelkami", "", () =>
        {
            var nodes = frames.SelectMany(f => f.Nodes).ToList();
            var ids = frames.Select(f => f.Id).ToList();
            _session.Edit(m => MapOps.Delete(m, nodes, null, ids));
            _session.ClearSelection();
        }));
        return menu;
    }

    private MenuFlyout BranchMenu()
    {
        var children = _session.SelectedBranchChildren.ToList();
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Odłącz kafelek od rodzica", "", DeleteSelection, "Del"));
        menu.Items.Add(Item("Zamień na strzałkę", "", BranchesToLinks));
        var colors = new MenuFlyoutSubItem { Text = "Kolor gałęzi", Icon = new FontIcon { Glyph = "" } };
        colors.Items.Add(Item("Automatyczny (z palety)", "", () => SetBranchColor(children, null)));
        colors.Items.Add(new MenuFlyoutSeparator());
        foreach (var c in Styles.BranchPalette.Append(Rgba.Parse("#8A8794", Rgba.Black)).Append(Rgba.Parse("#1E1B26", Rgba.Black)))
        {
            var color = c.ToString();
            var it = new MenuFlyoutItem
            {
                Text = color,
                Icon = new FontIcon { Glyph = "", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(MapScene.C(c)) },
            };
            it.Click += (_, _) => SetBranchColor(children, color);
            colors.Items.Add(it);
        }
        menu.Items.Add(colors);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Zaznacz kafelek na końcu gałęzi", "", () => _session.Select(children.Select(n => n.Id))));
        return menu;
    }

    private void SetBranchColor(List<Node> children, string? color) =>
        _session.Edit(_ => children.ForEach(n => MapOps.EditStyle(n, s => s.BranchColor = color)));

    private MenuFlyout CanvasMenu(Vec w)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Nowy kafelek tutaj", "", () => CreateAndEdit(m => MapOps.AddFloating(m, w))));
        menu.Items.Add(Item("Wklej", "", () => _ = PasteAsync(w), "Ctrl+V"));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Zaznacz wszystko", "", _session.SelectAll, "Ctrl+A"));
        menu.Items.Add(Item("Uporządkuj wszystkie gałęzie", "", ArrangeAll));
        menu.Items.Add(Item("Pokaż całą mapę", "", () => FitToContent(), "Ctrl+0"));
        return menu;
    }
}

internal static class FluentExtensions
{
    public static T With<T>(this T obj, Action<T> init)
    {
        init(obj);
        return obj;
    }
}
