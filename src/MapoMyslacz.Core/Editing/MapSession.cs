using MapoMyslacz.Core.Model;

namespace MapoMyslacz.Core.Editing;

/// <summary>
/// Otwarta mapa: dokument, plik, zaznaczenie, historia cofania i znacznik niezapisanych zmian.
/// Każda zmiana przechodzi przez <see cref="Edit"/> (albo parę <see cref="BeginBatch"/>/<see cref="EndBatch"/>
/// dla przeciągania i edycji tekstu) — dzięki temu cofanie obejmuje całe czynności, nie pojedyncze piksele.
/// </summary>
public sealed class MapSession
{
    private readonly History _history = new();
    private string? _batchBefore;
    private int _batchDepth;
    private long _version, _savedVersion;
    private MapIndex? _index;

    public MindMap Map { get; private set; }
    public string? FilePath { get; set; }

    /// <summary>Widok zapisany w pliku (środek i powiększenie) — do odtworzenia po otwarciu.</summary>
    public ViewState? InitialView { get; private set; }

    public HashSet<string> SelectedNodes { get; } = [];
    public HashSet<string> SelectedLinks { get; } = [];

    /// <summary>Mapa się zmieniła (treść, układ albo cofnięcie).</summary>
    public event Action? Changed;

    /// <summary>Zmieniło się zaznaczenie.</summary>
    public event Action? SelectionChanged;

    public MapSession(MindMap? map = null, string? filePath = null)
    {
        Map = map ?? CreateDefault();
        InitialView = Map.View;
        Map.View = null; // widok nie jest częścią historii zmian
        FilePath = filePath;
    }

    public static MapSession Open(string path) => new(MapSerializer.Load(path), path);

    /// <summary>Nowa mapa: jeden temat główny na środku.</summary>
    public static MindMap CreateDefault()
    {
        var map = new MindMap();
        var preset = Styles.Presets.First(p => p.Name == "Temat główny");
        map.Nodes.Add(new Node { Text = "Temat główny", Style = preset.Style.Clone() });
        return map;
    }

    public MapIndex Index => _index ??= new MapIndex(Map);

    public bool IsDirty => _version != _savedVersion;
    public bool IsBatching => _batchDepth > 0;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public string DisplayName => FilePath is null ? (Map.Title ?? "Bez tytułu") : Path.GetFileNameWithoutExtension(FilePath);

    // ------------------------------------------------------------------ zmiany

    /// <summary>Wykonuje zmianę jako jeden krok cofania. Zwraca false, jeśli mapa się nie zmieniła.</summary>
    public bool Edit(Action<MindMap> change)
    {
        BeginBatch();
        change(Map);
        return EndBatch();
    }

    /// <summary>Początek złożonej zmiany (przeciąganie, edycja tekstu) — zapamiętuje stan „przed”.</summary>
    public void BeginBatch()
    {
        if (_batchDepth++ == 0) _batchBefore = Snapshot();
    }

    /// <summary>Koniec złożonej zmiany: jeśli coś się zmieniło, zapisuje krok w historii i powiadamia widok.</summary>
    public bool EndBatch()
    {
        if (_batchDepth == 0) return false;
        if (--_batchDepth > 0) return false;
        var before = _batchBefore!;
        _batchBefore = null;
        _index = null;
        if (Snapshot() == before)
        {
            Changed?.Invoke(); // np. przywrócony tekst po Esc — widok i tak trzeba odświeżyć
            return false;
        }
        _history.Push(before);
        _version++;
        PruneSelection();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Przerywa złożoną zmianę i przywraca stan sprzed niej.</summary>
    public void CancelBatch()
    {
        if (_batchDepth == 0) return;
        _batchDepth = 0;
        if (_batchBefore is { } before) Restore(before);
        _batchBefore = null;
        Changed?.Invoke();
    }

    /// <summary>Do zmian „na żywo” w trakcie przeciągania (bez kroku w historii) — unieważnia indeks.</summary>
    public void Touch() => _index = null;

    public bool Undo()
    {
        if (IsBatching) return false;
        var state = _history.Undo(Snapshot());
        if (state is null) return false;
        Restore(state);
        _version++;
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (IsBatching) return false;
        var state = _history.Redo(Snapshot());
        if (state is null) return false;
        Restore(state);
        _version++;
        Changed?.Invoke();
        return true;
    }

    private string Snapshot() => MapSerializer.SerializeCompact(Map);

    private void Restore(string json)
    {
        Map = MapSerializer.Deserialize(json);
        _index = null;
        PruneSelection();
    }

    // ------------------------------------------------------------------ zapis

    public void SaveAs(string path, ViewState? view)
    {
        Map.View = view;
        try
        {
            MapSerializer.Save(Map, path);
        }
        finally
        {
            Map.View = null;
        }
        FilePath = path;
        _savedVersion = _version;
    }

    /// <summary>Oznacza mapę jako zmienioną (np. po imporcie — jeszcze nie ma swojego pliku .mapo).</summary>
    public void MarkDirty() => _version++;

    // ------------------------------------------------------------------ zaznaczenie

    public void Select(IEnumerable<string> nodes, IEnumerable<string>? links = null, bool add = false)
    {
        if (!add)
        {
            SelectedNodes.Clear();
            SelectedLinks.Clear();
        }
        foreach (var n in nodes) if (Index.Contains(n)) SelectedNodes.Add(n);
        foreach (var l in links ?? []) if (Map.Links.Any(x => x.Id == l)) SelectedLinks.Add(l);
        SelectionChanged?.Invoke();
    }

    public void SelectNode(string id, bool add = false) => Select([id], null, add);

    public void SelectLink(string id, bool add = false) => Select([], [id], add);

    public void Toggle(string? nodeId, string? linkId)
    {
        if (nodeId is not null && !SelectedNodes.Remove(nodeId)) SelectedNodes.Add(nodeId);
        if (linkId is not null && !SelectedLinks.Remove(linkId)) SelectedLinks.Add(linkId);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (SelectedNodes.Count == 0 && SelectedLinks.Count == 0) return;
        SelectedNodes.Clear();
        SelectedLinks.Clear();
        SelectionChanged?.Invoke();
    }

    public void SelectAll() => Select(Map.Nodes.Where(n => Index.IsVisible(n)).Select(n => n.Id), Map.Links.Select(l => l.Id));

    public IEnumerable<Node> SelectedNodeObjects => SelectedNodes.Select(id => Index.Get(id)).OfType<Node>();
    public IEnumerable<Link> SelectedLinkObjects => Map.Links.Where(l => SelectedLinks.Contains(l.Id));

    /// <summary>Jedyny zaznaczony kafelek (albo null).</summary>
    public Node? SingleNode => SelectedNodes.Count == 1 && SelectedLinks.Count == 0 ? Index.Get(SelectedNodes.First()) : null;

    private void PruneSelection()
    {
        int before = SelectedNodes.Count + SelectedLinks.Count;
        SelectedNodes.RemoveWhere(id => !Index.Contains(id) || !Index.IsVisible(Index.Get(id)!));
        var links = Map.Links.Select(l => l.Id).ToHashSet();
        SelectedLinks.RemoveWhere(id => !links.Contains(id));
        if (SelectedNodes.Count + SelectedLinks.Count != before) SelectionChanged?.Invoke();
    }
}
