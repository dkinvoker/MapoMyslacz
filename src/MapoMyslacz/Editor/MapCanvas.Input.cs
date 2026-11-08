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

namespace MapoMyslacz.Editor;

/// <summary>Mysz i klawiatura: przesuwanie widoku, zaznaczanie, przeciąganie, łączenie, skróty.</summary>
public sealed partial class MapCanvas
{
    private enum DragMode { None, Pending, Pan, Marquee, MoveNodes, BendLink, MoveHandle, Connect }

    private const double DragThreshold = 4;

    private DragMode _drag;
    private Hit _pressHit;
    private Point _pressScreen, _lastScreen;
    private Vec _pressWorld;
    private bool _pressShift, _pressCtrl, _pressAlt;
    private HashSet<string> _moving = [];
    private Dictionary<string, Vec> _moveOrigins = [];
    private Vec _bendC1, _bendC2;
    private List<Vec>? _bendVia;
    private Cubic? _connectPreview;
    private string? _dropTarget;
    private HashSet<string> _marqueeBase = [];
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

    private static readonly string? TraceFile = Environment.GetEnvironmentVariable("MAPOMYSLACZ_TRACE");

    private static void Trace(string msg)
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
        Trace($"pressed {pos} drag={_drag} left={props.IsLeftButtonPressed}");
        Focus(FocusState.Pointer);
        if (_editor is not null || _labelEditor is not null) CommitEdit();
        if (_drag != DragMode.None) return;

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
            UpdateHoverCursor(w, e.KeyModifiers);
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

            case DragMode.MoveNodes:
                // Shift: tylko w poziomie albo w pionie
                if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
                    d = Math.Abs(d.X) >= Math.Abs(d.Y) ? new Vec(d.X, 0) : new Vec(0, d.Y);
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
            {
                _drag = DragMode.MoveNodes;
                _session.BeginBatch();
                var idx = _session.Index;
                // Alt: przesuwa tylko zaznaczone kafelki, bez ich gałęzi
                _moving = MapOps.MovingSet(idx, _session.SelectedNodes, withDescendants: !_pressAlt);
                _moveOrigins = _moving.ToDictionary(id => id, id => new Vec(idx.Get(id)!.X, idx.Get(id)!.Y));
                SetCursor(MoveCursor);
                UpdateOverlay();
                break;
            }
            case HitKind.Link when _session.SelectedLinks.Contains(_pressHit.Id!) && FindLink(_pressHit.Id) is { } l
                                   && _nodes.TryGetValue(l.From, out var fv) && _nodes.TryGetValue(l.To, out var tv):
                _drag = DragMode.BendLink;
                _session.BeginBatch();
                if (l.Style?.Curve == LinkCurve.Straight) MapOps.EditStyle(l, s => s.Curve = null);
                _bendVia = l.Via?.ToList();
                (_bendC1, _bendC2) = LinkRouting.Controls(l, fv.Outline.Center, tv.Outline.Center);
                SetCursor(MoveCursor);
                break;
            case HitKind.LinkHandle1 or HitKind.LinkHandle2:
                _drag = DragMode.MoveHandle;
                _session.BeginBatch();
                break;
            case HitKind.ConnectHandle:
                _drag = DragMode.Connect;
                SetCursor(CrossCursor);
                HintChanged?.Invoke("Upuść na kafelku, żeby dodać strzałkę — albo na pustym miejscu, żeby dodać dziecko");
                break;
            case HitKind.None when _pressShift || _pressCtrl:
                _drag = DragMode.Marquee;
                _marqueeBase = _pressShift || _pressCtrl ? [.. _session.SelectedNodes] : [];
                _marquee.Visibility = Visibility.Visible;
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

    private void UpdateMarquee(Point pos)
    {
        double x = Math.Min(pos.X, _pressScreen.X), y = Math.Min(pos.Y, _pressScreen.Y);
        double w = Math.Abs(pos.X - _pressScreen.X), h = Math.Abs(pos.Y - _pressScreen.Y);
        _marquee.Margin = new Thickness(x, y, 0, 0);
        _marquee.Width = w;
        _marquee.Height = h;
        var box = Box.FromCorners(ToWorld(new Point(x, y)), ToWorld(new Point(x + w, y + h)));
        var hits = _nodes.Values.Where(v => box.Intersects(v.Bounds)).Select(v => v.Id);
        _session.Select(_marqueeBase.Concat(hits));
    }

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
            case DragMode.MoveNodes or DragMode.BendLink or DragMode.MoveHandle:
                _session.EndBatch();
                break;
            case DragMode.Connect when click:
                FinishConnect(_lastPointerWorld);
                break;
            case DragMode.Marquee:
                _marquee.Visibility = Visibility.Collapsed;
                break;
        }
        _connectPreview = null;
        _dropTarget = null;
        _bendVia = null;
        HintChanged?.Invoke(null);
        UpdateOverlay();
    }

    /// <summary>Przerywa przeciąganie (Esc, zmiana mapy) i cofa jego skutki.</summary>
    private void CancelInteraction()
    {
        if (_drag is DragMode.MoveNodes or DragMode.BendLink or DragMode.MoveHandle) _session.CancelBatch();
        _drag = DragMode.None;
        _marquee.Visibility = Visibility.Collapsed;
        _connectPreview = null;
        _dropTarget = null;
        ExitConnectMode();
        ReleasePointerCaptures();
        UpdateOverlay();
    }

    private void OnClick()
    {
        switch (_pressHit.Kind)
        {
            case HitKind.Node when !_pressCtrl && !_pressShift && _session.SelectedNodes.Count + _session.SelectedLinks.Count > 1:
                _session.SelectNode(_pressHit.Id!);
                break;
            case HitKind.Link when !_pressCtrl && !_pressShift && _session.SelectedNodes.Count + _session.SelectedLinks.Count > 1:
                _session.SelectLink(_pressHit.Id!);
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
        // upuszczenie na pustym miejscu: nowe dziecko w tym punkcie
        CreateAndEdit(m =>
        {
            var n = new Node { Text = MapOps.DefaultText, Parent = from, X = Math.Round(w.X), Y = Math.Round(w.Y) };
            m.Nodes.Add(n);
            return n;
        });
    }

    private void UpdateHoverCursor(Vec w, VirtualKeyModifiers mods)
    {
        if (_connectModeFrom is not null)
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
        SetCursor(hit.Kind switch
        {
            HitKind.Node => MoveCursor,
            HitKind.Link when _session.SelectedLinks.Contains(hit.Id!) => MoveCursor,
            HitKind.Link or HitKind.Badge or HitKind.LinkHandle1 or HitKind.LinkHandle2 or HitKind.ConnectHandle => HandCursor,
            _ when mods.HasFlag(VirtualKeyModifiers.Shift) || mods.HasFlag(VirtualKeyModifiers.Control) => CrossCursor,
            _ => HandCursors.Get(grab: false) ?? ArrowCursor,
        });
    }

    private void OnPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this);
        int delta = pt.Properties.MouseWheelDelta;
        if (pt.Properties.IsHorizontalMouseWheel) PanBy(-delta, 0);
        else if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift)) PanBy(delta, 0);
        else ZoomAt(pt.Position, _zoom * Math.Pow(1.0015, delta));
        e.Handled = true;
    }

    private void OnDoubleClick(Vec w, Hit hit)
    {
        switch (hit.Kind)
        {
            case HitKind.Node:
                _session.SelectNode(hit.Id!);
                BeginEdit(hit.Id!);
                break;
            case HitKind.Link:
                _session.SelectLink(hit.Id!);
                BeginLabelEdit(hit.Id!);
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

    // ================================================================== klawiatura

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
                if (_connectModeFrom is not null || _drag != DragMode.None) CancelInteraction();
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
        menu.Items.Add(Item("Edytuj tekst", "", EditSelected, "F2", single));
        menu.Items.Add(Item("Dodaj dziecko", "", AddChild, "Tab", single));
        menu.Items.Add(Item("Dodaj rodzeństwo", "", AddSibling, "Enter", single));
        menu.Items.Add(Item(sel.Count == 2 ? "Połącz strzałką" : "Połącz strzałką z…", "", ConnectSelected, "Ctrl+L"));
        menu.Items.Add(new MenuFlyoutSeparator());

        var styles = new MenuFlyoutSubItem { Text = "Styl", Icon = new FontIcon { Glyph = "" } };
        foreach (var p in Styles.Presets)
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
        menu.Items.Add(new MenuFlyoutSeparator());

        if (anyChildren)
        {
            menu.Items.Add(Item(allCollapsed ? "Rozwiń gałąź" : "Zwiń gałąź", allCollapsed ? "" : "", ToggleCollapse, "Ctrl+/"));
            menu.Items.Add(Item("Uporządkuj gałąź", "", ArrangeSelection, "Ctrl+R"));
        }
        if (anyParent) menu.Items.Add(Item("Odłącz od rodzica", "", DetachSelection));
        if (anyChildren || anyParent) menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item("Kopiuj", "", () => _ = CopyAsync(false), "Ctrl+C"));
        menu.Items.Add(Item("Wytnij", "", () => _ = CopyAsync(true), "Ctrl+X"));
        menu.Items.Add(Item("Duplikuj", "", Duplicate, "Ctrl+D"));
        menu.Items.Add(Item("Usuń", "", DeleteSelection, "Del"));
        return menu;
    }

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
        bool straight = links.All(l => Styles.Resolve(l.Style).Curve == LinkCurve.Straight);
        menu.Items.Add(Item(straight ? "Zakrzywiona" : "Prosta", straight ? "" : "",
            () => _session.Edit(_ => links.ForEach(l => MapOps.EditStyle(l, s => s.Curve = straight ? null : LinkCurve.Straight)))));
        menu.Items.Add(Item("Przywróć domyślną krzywiznę", "", () => _session.Edit(_ => links.ForEach(l => { l.C1 = l.C2 = null; l.Via = null; }))));
        menu.Items.Add(new MenuFlyoutSeparator());
        bool start = links.All(l => Styles.Resolve(l.Style).ArrowStart);
        bool end = links.All(l => Styles.Resolve(l.Style).ArrowEnd);
        menu.Items.Add(new ToggleMenuFlyoutItem { Text = "Grot na początku", IsChecked = start }.With(i =>
            i.Click += (_, _) => _session.Edit(_ => links.ForEach(l => MapOps.EditStyle(l, s => s.ArrowStart = start ? null : true)))));
        menu.Items.Add(new ToggleMenuFlyoutItem { Text = "Grot na końcu", IsChecked = end }.With(i =>
            i.Click += (_, _) => _session.Edit(_ => links.ForEach(l => MapOps.EditStyle(l, s => s.ArrowEnd = end ? false : null)))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Usuń", "", DeleteSelection, "Del"));
        return menu;
    }

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
