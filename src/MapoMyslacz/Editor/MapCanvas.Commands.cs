using System.Text;
using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using Windows.System;
using XCanvas = Microsoft.UI.Xaml.Controls.Canvas;

namespace MapoMyslacz.Editor;

/// <summary>Polecenia edycji (dodawanie, usuwanie, schowek, układ) i edycja tekstu w miejscu.</summary>
public sealed partial class MapCanvas
{
    private const string ClipboardFormat = "MapoMyslacz.Fragment";

    private TextBox? _editor;
    private string? _editNodeId;
    private string _editOriginal = "";
    private bool _editIsNew;
    private bool _editorFresh;
    private bool _finishingEdit;
    private TextBox? _labelEditor;
    private string? _labelLinkId;

    public bool IsEditingText => _editor is not null || _labelEditor is not null;

    // ================================================================== dodawanie

    /// <summary>Tworzy kafelek i od razu otwiera edycję jego tekstu (całość = jeden krok cofania).</summary>
    private void CreateAndEdit(Func<MindMap, Node> create)
    {
        CommitEdit();
        _session.BeginBatch();
        var node = create(_session.Map);
        _session.Touch();
        Rebuild();
        _session.SelectNode(node.Id);
        EnsureVisible(node.Id);
        StartEditor(node.Id, null, isNew: true);
    }

    public void AddChild()
    {
        var parent = _session.SingleNode ?? _session.SelectedNodeObjects.FirstOrDefault();
        if (parent is null)
        {
            AddFloating();
            return;
        }
        CreateAndEdit(m => MapOps.AddChild(m, parent.Id, Sizer));
    }

    public void AddSibling()
    {
        if (_session.SingleNode is not { } node)
        {
            AddFloating();
            return;
        }
        CreateAndEdit(m => MapOps.AddSibling(m, node.Id, Sizer));
    }

    /// <summary>Nowy swobodny kafelek na środku widoku.</summary>
    public void AddFloating()
    {
        var c = ToWorld(ViewCenter);
        // nie nakładaj kilku nowych kafelków dokładnie na siebie
        while (_nodes.Values.Any(v => Vec.Distance(new Vec(v.X, v.Y), c) < 8)) c += new Vec(24, 24);
        CreateAndEdit(m => MapOps.AddFloating(m, c));
    }

    public void ConnectSelected()
    {
        var sel = _session.SelectedNodes.ToList();
        if (sel.Count == 1)
        {
            EnterConnectMode();
            return;
        }
        if (sel.Count < 2)
        {
            Notify?.Invoke("Zaznacz kafelek (albo dwa), żeby połączyć je strzałką.");
            return;
        }
        // kilka kafelków: strzałki od pierwszego zaznaczonego do pozostałych
        var created = new List<string>();
        _session.Edit(m =>
        {
            for (int i = 1; i < sel.Count; i++)
                if (MapOps.AddLink(m, sel[0], sel[i]) is { } l) created.Add(l.Id);
        });
        if (created.Count > 0) _session.Select([], created);
    }

    private void CreateLink(string from, string to)
    {
        Link? link = null;
        _session.Edit(m => link = MapOps.AddLink(m, from, to));
        if (link is not null) _session.SelectLink(link.Id);
        else Notify?.Invoke("Taka strzałka już istnieje.");
    }

    // ================================================================== usuwanie, porządkowanie

    public void DeleteSelection()
    {
        if (_session.SelectedNodes.Count == 0 && _session.SelectedLinks.Count == 0) return;
        var nodes = _session.SelectedNodes.ToList();
        var links = _session.SelectedLinks.ToList();
        _session.Edit(m => MapOps.Delete(m, nodes, links));
        _session.ClearSelection();
    }

    public void ArrangeSelection()
    {
        var sel = _session.SelectedNodes.ToList();
        if (sel.Count == 0)
        {
            ArrangeAll();
            return;
        }
        var idx = _session.Index;
        // gałąź bez dzieci? porządkujemy rodzeństwo (czyli gałąź rodzica)
        var targets = sel.Select(id => idx.ChildrenOf(id).Count > 0 ? id : idx.Get(id)?.Parent).OfType<string>().Distinct().ToList();
        if (targets.Count == 0)
        {
            Notify?.Invoke("Porządkowanie układa gałęzie — zaznaczony kafelek nie ma dzieci.");
            return;
        }
        _session.Edit(m => { foreach (var id in targets) TreeLayout.Arrange(m, id, Sizer); });
    }

    public void ArrangeAll()
    {
        if (_session.Edit(m => TreeLayout.ArrangeAll(m, Sizer))) Notify?.Invoke("Uporządkowano gałęzie.");
        else Notify?.Invoke("Wszystkie gałęzie są już uporządkowane.");
    }

    public void ToggleCollapse()
    {
        var idx = _session.Index;
        var withKids = _session.SelectedNodeObjects.Where(n => idx.ChildrenOf(n.Id).Count > 0).ToList();
        if (withKids.Count == 0) return;
        bool collapse = withKids.Any(n => !n.Collapsed);
        _session.Edit(_ => withKids.ForEach(n => n.Collapsed = collapse));
    }

    public void DetachSelection()
    {
        var ids = _session.SelectedNodes.ToList();
        _session.Edit(m => { foreach (var id in ids) MapOps.Reparent(m, id, null); });
    }

    private void Nudge(Direction dir, double step)
    {
        if (_session.SelectedNodes.Count == 0) return;
        var d = dir switch
        {
            Direction.Left => new Vec(-step, 0),
            Direction.Right => new Vec(step, 0),
            Direction.Up => new Vec(0, -step),
            _ => new Vec(0, step),
        };
        var ids = _session.SelectedNodes.ToList();
        _session.Edit(m => MapOps.Move(m, ids, d));
    }

    public void Undo()
    {
        CommitEdit();
        if (!_session.Undo()) Notify?.Invoke("Nie ma czego cofnąć.");
    }

    public void Redo()
    {
        CommitEdit();
        if (!_session.Redo()) Notify?.Invoke("Nie ma czego ponowić.");
    }

    // ================================================================== styl

    public void ApplyPreset(StylePreset preset)
    {
        var nodes = _session.SelectedNodeObjects.ToList();
        _session.Edit(_ => nodes.ForEach(n => MapOps.ApplyPreset(n, preset)));
    }

    public void SetShape(NodeShape shape)
    {
        var nodes = _session.SelectedNodeObjects.ToList();
        _session.Edit(_ => nodes.ForEach(n => MapOps.EditStyle(n, s => s.Shape = shape == NodeShape.Rounded ? null : shape)));
    }

    /// <summary>Włącza znacznik na wszystkich zaznaczonych kafelkach (albo wyłącza, jeśli wszystkie już go mają).</summary>
    public void ToggleMarker(string id)
    {
        var nodes = _session.SelectedNodeObjects.ToList();
        if (nodes.Count == 0) return;
        bool on = !nodes.All(n => Markers.Has(n, id));
        _session.Edit(_ => nodes.ForEach(n => Markers.Set(n, id, on)));
    }

    public void ToggleBold()
    {
        var nodes = _session.SelectedNodeObjects.ToList();
        if (nodes.Count == 0) return;
        bool on = !nodes.All(n => n.Style?.Bold == true);
        _session.Edit(_ => nodes.ForEach(n => MapOps.EditStyle(n, s => s.Bold = on ? true : null)));
    }

    public void ToggleItalic()
    {
        var nodes = _session.SelectedNodeObjects.ToList();
        if (nodes.Count == 0) return;
        bool on = !nodes.All(n => n.Style?.Italic == true);
        _session.Edit(_ => nodes.ForEach(n => MapOps.EditStyle(n, s => s.Italic = on ? true : null)));
    }

    // ================================================================== schowek

    public async Task CopyAsync(bool cut)
    {
        var ids = _session.SelectedNodes.ToList();
        if (ids.Count == 0) return;
        var fragment = MapOps.Copy(_session.Map, ids);
        try
        {
            var dp = new DataPackage { RequestedOperation = cut ? DataPackageOperation.Move : DataPackageOperation.Copy };
            // własny format jako bajty UTF-8 (po Clipboard.Flush() Windows i tak oddaje go jako strumień)
            dp.SetData(ClipboardFormat, await ToStreamAsync(Encoding.UTF8.GetBytes(MapSerializer.SerializeFragment(fragment))));
            // dla innych programów: konspekt z wcięciami (wklejony z powrotem odtworzy gałęzie)
            dp.SetText(Outline(fragment));
            Clipboard.SetContent(dp);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Nie udało się skopiować: {ex.Message}");
            return;
        }
        if (cut)
        {
            _session.Edit(m => MapOps.Delete(m, ids));
            _session.ClearSelection();
        }
        Notify?.Invoke(cut ? $"Wycięto: {fragment.Nodes.Count} kaf." : $"Skopiowano: {fragment.Nodes.Count} kaf.");
    }

    public async Task PasteAsync(Vec? at = null)
    {
        MapFragment? fragment = null;
        try
        {
            var view = Clipboard.GetContent();
            if (view.Contains(ClipboardFormat))
                fragment = MapSerializer.TryDeserializeFragment(await ReadClipboardTextAsync(await view.GetDataAsync(ClipboardFormat)));
            if (fragment is null && view.Contains(StandardDataFormats.Text))
            {
                var text = await view.GetTextAsync();
                fragment = MapSerializer.TryDeserializeFragment(text) ?? (string.IsNullOrWhiteSpace(text) ? null : OutlineParser.Parse(text));
            }
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Nie udało się wkleić: {ex.Message}");
            return;
        }
        if (fragment is null || fragment.Nodes.Count == 0)
        {
            Notify?.Invoke("W schowku nie ma kafelków ani tekstu.");
            return;
        }
        List<Node> added = [];
        var point = at ?? PastePoint;
        _session.Edit(m => added = MapOps.Paste(m, fragment, point));
        _session.Select(added.Select(n => n.Id));
    }

    public void Duplicate()
    {
        var ids = _session.SelectedNodes.ToList();
        if (ids.Count == 0) return;
        var fragment = MapOps.Copy(_session.Map, ids);
        List<Node> added = [];
        _session.Edit(m => added = MapOps.Paste(m, fragment, offset: new Vec(36, 36)));
        _session.Select(added.Select(n => n.Id));
    }

    /// <summary>Tekst z wcięciami (tabulator na poziom) — tak kafelki trafiają do Notatnika czy Worda.</summary>
    private static string Outline(MapFragment f)
    {
        var map = new MindMap { Nodes = f.Nodes };
        var idx = new MapIndex(map);
        var sb = new StringBuilder();
        void Write(Node n, int depth)
        {
            sb.Append('\t', depth).AppendLine(n.Text.Replace("\n", " "));
            foreach (var c in idx.ChildrenOf(n.Id).OrderBy(c => c.Y)) Write(c, depth + 1);
        }
        foreach (var root in f.Nodes.Where(n => n.Parent is null).OrderBy(n => n.Y)) Write(root, 0);
        return sb.ToString().TrimEnd();
    }

    private static async Task<InMemoryRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var ms = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        ms.Seek(0);
        return ms;
    }

    /// <summary>Dane własnego formatu ze schowka jako tekst: string, strumień albo bufor.</summary>
    private static async Task<string?> ReadClipboardTextAsync(object? data)
    {
        byte[] bytes;
        switch (data)
        {
            case string s:
                return s;
            case IRandomAccessStream stream:
            {
                stream.Seek(0);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                uint size = (uint)stream.Size;
                await reader.LoadAsync(size);
                bytes = new byte[size];
                reader.ReadBytes(bytes);
                break;
            }
            case IBuffer buffer:
                bytes = new byte[buffer.Length];
                DataReader.FromBuffer(buffer).ReadBytes(bytes);
                break;
            default:
                return null;
        }
        // UTF-16 (zera co drugi bajt) albo UTF-8
        var text = bytes.Length > 1 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
        return text.TrimEnd('\0');
    }

    // ================================================================== edycja tekstu kafelka

    public void EditSelected()
    {
        if (_session.SingleNode is { } n) BeginEdit(n.Id);
        else if (_session.SelectedLinks.Count == 1 && _session.SelectedNodes.Count == 0) BeginLabelEdit(_session.SelectedLinks.First());
    }

    /// <param name="initialText">Pierwszy wpisany znak (zastępuje tekst) albo null (cały tekst do edycji, zaznaczony).</param>
    public void BeginEdit(string nodeId, string? initialText = null)
    {
        CommitEdit();
        if (_session.Index.Get(nodeId) is null) return;
        _session.BeginBatch();
        StartEditor(nodeId, initialText, isNew: false);
    }

    private void StartEditor(string nodeId, string? initialText, bool isNew)
    {
        var node = _session.Index.Get(nodeId);
        if (node is null || !_nodes.TryGetValue(nodeId, out var v))
        {
            _session.EndBatch();
            return;
        }
        _editNodeId = nodeId;
        _editOriginal = node.Text;
        _editIsNew = isNew;
        _scene.EditingId = nodeId;
        var tb = CreateNodeEditor(v.Style, initialText ?? node.Text);
        _editor = tb;
        _editorLayer.Children.Add(tb);
        if (initialText is not null) node.Text = initialText;
        Relayout(node);

        tb.TextChanged += (_, _) =>
        {
            if (_editor != tb || _session.Index.Get(nodeId) is not { } n) return;
            n.Text = tb.Text.Replace("\r\n", "\n").Replace('\r', '\n');
            Relayout(n);
        };
        tb.PreviewKeyDown += OnEditorKeyDown;
        tb.LostFocus += (_, _) => { if (_editor == tb) CommitEdit(); };
        _editorFresh = initialText is null;
        tb.Loaded += (_, _) =>
        {
            tb.Focus(FocusState.Programmatic);
            if (_editorFresh) tb.SelectAll();
            else tb.Select(tb.Text.Length, 0);
            _editorFresh = false;
        };
        UpdateOverlay();
        HintChanged?.Invoke("Enter — zatwierdź · Shift+Enter — nowy wiersz · Tab — zatwierdź i dodaj dziecko · Esc — anuluj");
    }

    /// <summary>Pole edycji o wyglądzie tekstu kafelka (przezroczyste, ta sama czcionka) — leży na warstwie nad płótnem.</summary>
    private static TextBox CreateNodeEditor(ResolvedNodeStyle s, string text)
    {
        var tb = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(MapScene.FontFamily),
            FontSize = s.FontSize,
            FontWeight = s.Bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = s.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            TextAlignment = s.Align switch
            {
                TextAlign.Left => TextAlignment.Left,
                TextAlign.Right => TextAlignment.Right,
                _ => TextAlignment.Center,
            },
            IsTextScaleFactorEnabled = false,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            IsSpellCheckEnabled = true,
        };
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var ink = new Microsoft.UI.Xaml.Media.SolidColorBrush(MapScene.C(s.TextColor));
        tb.Background = clear;
        tb.Foreground = ink;
        foreach (var k in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
                 })
            tb.Resources[k] = clear;
        foreach (var k in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            tb.Resources[k] = ink;
        return tb;
    }

    /// <summary>
    /// Kafelek rośnie razem z tekstem (rozmiar liczy Win2D, tak samo jak przy rysowaniu); dziecko trzyma krawędź od
    /// strony rodzica (nie „ucieka” na gałąź). Pole edycji przesuwa się nad nowe miejsce tekstu.
    /// </summary>
    private void Relayout(Node node)
    {
        var idx = _session.Index;
        double oldW = _nodes.TryGetValue(node.Id, out var before) ? before.Width : 0;
        if (_scene.Relayout(idx, node) is not { } v) return;
        if (node.Parent is not null && oldW > 0 && Math.Abs(v.Width - oldW) > 0.1)
        {
            int side = MapOps.SideOf(idx, node);
            node.X += side * (v.Width - oldW) / 2;
        }
        RefreshGeometry([node.Id]);
        if (_editor is { } ed)
        {
            var o = v.TextOrigin;
            // szerokość z zapasem na następny znak (TextBox zawija sam — bez zapasu łamałby wiersz przed przeliczeniem)
            double wrap = v.Style.WrapWidth(node);
            ed.Width = Math.Min(wrap + 2, Math.Max(v.TextW + v.Style.FontSize, 16));
            ed.MaxWidth = wrap + 2;
            double shift = (ed.Width - v.TextW) / 2;
            XCanvas.SetLeft(ed, o.X - (v.Style.Align == TextAlign.Left ? 0 : v.Style.Align == TextAlign.Right ? 2 * shift : shift));
            XCanvas.SetTop(ed, o.Y);
        }
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = IsDown(VirtualKey.Shift);
        switch (e.Key)
        {
            case VirtualKey.Enter when !shift:
                e.Handled = true;
                CommitEdit();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                CancelEdit();
                break;
            case VirtualKey.Tab when !shift:
                e.Handled = true;
                CommitEdit();
                AddChild();
                break;
        }
    }

    /// <summary>Kończy edycję (tekstu kafelka albo podpisu strzałki) i zapisuje ją jako krok cofania.</summary>
    public void CommitEdit()
    {
        if (_labelEditor is not null)
        {
            CommitLabelEdit();
            return;
        }
        if (_editor is null || _finishingEdit) return;
        _finishingEdit = true;
        try
        {
            if (_session.Index.Get(_editNodeId) is { } n) n.Text = n.Text.Trim();
            CloseEditor();
            _session.EndBatch();
        }
        finally
        {
            _finishingEdit = false;
        }
    }

    private void CancelEdit()
    {
        if (_editor is null || _finishingEdit) return;
        _finishingEdit = true;
        try
        {
            var id = _editNodeId;
            bool wasNew = _editIsNew;
            if (_session.Index.Get(id) is { } n) n.Text = _editOriginal;
            CloseEditor();
            // nowy kafelek anulowany Esc: usuwamy go (cała partia zmian zniknie bez śladu w historii)
            if (wasNew && id is not null) _session.CancelBatch();
            else _session.EndBatch();
        }
        finally
        {
            _finishingEdit = false;
        }
    }

    private void CloseEditor()
    {
        // najpierw fokus na płótno — inaczej po usunięciu pola przeskoczyłby do panelu właściwości
        Focus(FocusState.Programmatic);
        if (_editor is { } ed) _editorLayer.Children.Remove(ed);
        _editor = null;
        _editNodeId = null;
        _scene.EditingId = null;
        HintChanged?.Invoke(null);
    }

    // ================================================================== podpis strzałki

    public void BeginLabelEdit(string linkId, string? initialText = null)
    {
        CommitEdit();
        var link = FindLink(linkId);
        if (link is null || !_links.TryGetValue(linkId, out var lv) || lv.Route is null) return;
        _session.BeginBatch();
        _labelLinkId = linkId;
        var s = lv.Style!;
        var tb = new TextBox
        {
            Text = initialText ?? link.Label ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = s.FontSize,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(MapScene.FontFamily),
            MinWidth = 120,
            MaxWidth = 280,
            TextAlignment = TextAlignment.Center,
            PlaceholderText = "Podpis strzałki",
            IsTextScaleFactorEnabled = false,
        };
        tb.Measure(new Windows.Foundation.Size(280, double.PositiveInfinity));
        XCanvas.SetLeft(tb, lv.Route.LabelAt.X - Math.Max(120, tb.DesiredSize.Width) / 2);
        XCanvas.SetTop(tb, lv.Route.LabelAt.Y - tb.DesiredSize.Height / 2);
        _labelEditor = tb;
        _editorFresh = initialText is null;
        _editorLayer.Children.Add(tb);
        Trace($"label begin '{tb.Text}'");
        tb.PreviewKeyDown += OnLabelEditorKeyDown;
        tb.LostFocus += (_, _) => { if (_labelEditor == tb) CommitLabelEdit(); };
        tb.Loaded += (_, _) =>
        {
            tb.Focus(FocusState.Programmatic);
            if (_editorFresh) tb.SelectAll();
            else tb.Select(tb.Text.Length, 0);
            _editorFresh = false;
        };
        HintChanged?.Invoke("Enter — zatwierdź podpis · Shift+Enter — nowy wiersz · Esc — anuluj");
    }

    private void OnLabelEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !IsDown(VirtualKey.Shift))
        {
            e.Handled = true;
            CommitLabelEdit();
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CommitLabelEdit(cancel: true);
        }
    }

    private void CommitLabelEdit(bool cancel = false)
    {
        if (_labelEditor is not { } tb || _finishingEdit) return;
        Trace($"label commit cancel={cancel} text='{tb.Text}' focus={tb.FocusState}");
        _finishingEdit = true;
        try
        {
            _labelEditor = null;
            // najpierw fokus na płótno — inaczej po usunięciu pola przeskoczyłby do panelu właściwości
            Focus(FocusState.Programmatic);
            _editorLayer.Children.Remove(tb);
            if (!cancel && FindLink(_labelLinkId) is { } link)
            {
                var text = tb.Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                link.Label = text.Length == 0 ? null : text;
            }
            _labelLinkId = null;
            _session.EndBatch();
            HintChanged?.Invoke(null);
            Focus(FocusState.Programmatic);
        }
        finally
        {
            _finishingEdit = false;
        }
    }
}
