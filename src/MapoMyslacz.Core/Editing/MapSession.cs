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
    private string? _mergeKey; // klucz ostatniego kroku, do którego można jeszcze doklejać zmiany
    private long _version, _savedVersion;
    private MapIndex? _index;

    public MindMap Map { get; private set; }
    public string? FilePath { get; set; }

    /// <summary>Widok zapisany w pliku (środek i powiększenie) — do odtworzenia po otwarciu albo powrocie do arkusza.</summary>
    public ViewState? InitialView { get; private set; }

    /// <summary>Zapamiętuje widok arkusza (przy przełączaniu zakładek wraca się w to samo miejsce).</summary>
    public void RememberView(ViewState? view) => InitialView = view;

    public HashSet<string> SelectedNodes { get; } = [];
    public HashSet<string> SelectedLinks { get; } = [];
    public HashSet<string> SelectedFrames { get; } = [];

    /// <summary>Zaznaczone gałęzie drzewa — po id kafelka-dziecka (gałąź prowadzi od jego rodzica do niego).</summary>
    public HashSet<string> SelectedBranches { get; } = [];

    /// <summary>Ile obiektów jest zaznaczonych (kafelki, strzałki, ramki, gałęzie).</summary>
    public int SelectionCount => SelectedNodes.Count + SelectedLinks.Count + SelectedFrames.Count + SelectedBranches.Count;

    /// <summary>Mapa się zmieniła (treść, układ albo cofnięcie).</summary>
    public event Action? Changed;

    /// <summary>Zmieniło się zaznaczenie.</summary>
    public event Action? SelectionChanged;

    /// <summary>
    /// Magazyn obrazków (id → dane). Poza historią cofania — migawki byłyby wtedy po kilka MB; obrazki tylko
    /// przybywają, więc cofnięcie i ponowienie zawsze znajdą swoje dane. Do pliku trafiają tylko używane.
    /// </summary>
    public Dictionary<string, string> Images { get; }

    /// <param name="images">Wspólny magazyn obrazków (np. kilku arkuszy jednego pliku); brak — własny z mapy.</param>
    public MapSession(MindMap? map = null, string? filePath = null, Dictionary<string, string>? images = null)
    {
        Map = map ?? CreateDefault();
        InitialView = Map.View;
        Map.View = null; // widok nie jest częścią historii zmian
        FilePath = filePath;
        Images = images ?? Map.Images ?? [];
        if (images is not null && Map.Images is { } own)
            foreach (var (id, data) in own) Images.TryAdd(id, data);
        Map.Images = Images;
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

    /// <summary>Licznik zmian (rośnie przy każdej zmianie, cofnięciu i ponowieniu) — np. do autozapisu.</summary>
    public long Version => _version;
    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public string DisplayName => FilePath is null ? (Map.Title ?? "Bez tytułu") : Path.GetFileNameWithoutExtension(FilePath);

    // ------------------------------------------------------------------ zmiany

    /// <summary>
    /// Wykonuje zmianę jako jeden krok cofania. Zwraca false, jeśli mapa się nie zmieniła. Kolejne zmiany z tym samym
    /// <paramref name="mergeKey"/> (bez innych zmian, cofnięć czy ponowień pomiędzy) doklejają się do poprzedniego
    /// kroku — np. pisanie notatki w panelu na bieżąco zmienia kafelek, a Ctrl+Z cofa całe pisanie naraz.
    /// </summary>
    public bool Edit(Action<MindMap> change, string? mergeKey = null)
    {
        BeginBatch();
        change(Map);
        return EndBatch(mergeKey);
    }

    /// <summary>Początek złożonej zmiany (przeciąganie, edycja tekstu) — zapamiętuje stan „przed”.</summary>
    public void BeginBatch()
    {
        if (_batchDepth++ == 0) _batchBefore = Snapshot();
    }

    /// <summary>Koniec złożonej zmiany: jeśli coś się zmieniło, zapisuje krok w historii i powiadamia widok.</summary>
    public bool EndBatch(string? mergeKey = null)
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
        // ciąg zmian z tym samym kluczem zostaje jednym krokiem: „przed” pierwszej z nich jest już w historii
        if (mergeKey is null || mergeKey != _mergeKey || !_history.CanUndo) _history.Push(before);
        _mergeKey = mergeKey;
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
        _mergeKey = null;
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
        _mergeKey = null;
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
        _mergeKey = null;
        Restore(state);
        _version++;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Migawka do historii cofania — bez obrazków (te są w magazynie sesji).</summary>
    private string Snapshot()
    {
        Map.Images = null;
        try
        {
            return MapSerializer.SerializeCompact(Map);
        }
        finally
        {
            Map.Images = Images;
        }
    }

    private void Restore(string json)
    {
        Map = MapSerializer.Deserialize(json);
        Map.Images = Images;
        _index = null;
        PruneSelection();
    }

    // ------------------------------------------------------------------ zapis

    public void SaveAs(string path, ViewState? view)
    {
        Write(path, view);
        FilePath = path;
        _savedVersion = _version;
    }

    /// <summary>Zapisuje kopię mapy (np. autozapis) — bez zmiany pliku mapy i znacznika niezapisanych zmian.</summary>
    public void WriteCopy(string path, ViewState? view) => Write(path, view);

    private void Write(string path, ViewState? view)
    {
        Map.View = view;
        Map.Images = MapImages.Referenced(Map.Nodes, Images);
        try
        {
            MapSerializer.Save(Map, path);
        }
        finally
        {
            Map.View = null;
            Map.Images = Images;
        }
    }

    /// <summary>
    /// Mapa gotowa do zapisu w pliku z wieloma arkuszami: kopia z widokiem i bez magazynu obrazków
    /// (obrazki wszystkich arkuszy trafiają do pliku raz, wspólnie).
    /// </summary>
    public MindMap ExportSheet(ViewState? view)
    {
        var copy = MapSerializer.Deserialize(Snapshot());
        copy.View = view;
        copy.Images = null;
        return copy;
    }

    /// <summary>Oznacza stan jako zapisany (zapis całego pliku z arkuszami robi dokument).</summary>
    public void MarkSaved(string path)
    {
        FilePath = path;
        _savedVersion = _version;
    }

    /// <summary>Oznacza mapę jako zmienioną (np. po imporcie — jeszcze nie ma swojego pliku .mapo).</summary>
    public void MarkDirty() => _version++;

    // ------------------------------------------------------------------ zaznaczenie

    public void Select(IEnumerable<string> nodes, IEnumerable<string>? links = null, bool add = false, IEnumerable<string>? frames = null,
        IEnumerable<string>? branches = null)
    {
        if (!add)
        {
            SelectedNodes.Clear();
            SelectedLinks.Clear();
            SelectedFrames.Clear();
            SelectedBranches.Clear();
        }
        if (branches is not null)
            foreach (var b in branches) if (Index.Get(b) is { Parent: not null } child && Index.Get(child.Parent) is not null) SelectedBranches.Add(b);
        foreach (var n in nodes) if (Index.Contains(n)) SelectedNodes.Add(n);
        if (links is not null)
        {
            var existing = Map.Links.Select(x => x.Id).ToHashSet();
            foreach (var l in links) if (existing.Contains(l)) SelectedLinks.Add(l);
        }
        if (frames is not null)
            foreach (var f in frames) if (FindFrame(f) is not null) SelectedFrames.Add(f);
        SelectionChanged?.Invoke();
    }

    public void SelectNode(string id, bool add = false) => Select([id], null, add);

    public void SelectLink(string id, bool add = false) => Select([], [id], add);

    public void SelectFrame(string id, bool add = false) => Select([], null, add, [id]);

    /// <summary>Zaznacza gałąź prowadzącą do kafelka <paramref name="childId"/>.</summary>
    public void SelectBranch(string childId, bool add = false) => Select([], null, add, null, [childId]);

    public void Toggle(string? nodeId, string? linkId, string? frameId = null, string? branchId = null)
    {
        if (nodeId is not null && !SelectedNodes.Remove(nodeId)) SelectedNodes.Add(nodeId);
        if (linkId is not null && !SelectedLinks.Remove(linkId)) SelectedLinks.Add(linkId);
        if (frameId is not null && !SelectedFrames.Remove(frameId)) SelectedFrames.Add(frameId);
        if (branchId is not null && !SelectedBranches.Remove(branchId)) SelectedBranches.Add(branchId);
        SelectionChanged?.Invoke();
    }

    public void ClearSelection()
    {
        if (SelectionCount == 0) return;
        SelectedNodes.Clear();
        SelectedLinks.Clear();
        SelectedFrames.Clear();
        SelectedBranches.Clear();
        SelectionChanged?.Invoke();
    }

    public Frame? FindFrame(string? id) => id is null ? null : Map.Frames?.FirstOrDefault(f => f.Id == id);

    public IEnumerable<Frame> SelectedFrameObjects => Map.Frames?.Where(f => SelectedFrames.Contains(f.Id)) ?? [];

    public void SelectAll() => Select(Map.Nodes.Where(n => Index.IsVisible(n)).Select(n => n.Id), Map.Links.Select(l => l.Id));

    public IEnumerable<Node> SelectedNodeObjects => SelectedNodes.Select(id => Index.Get(id)).OfType<Node>();
    public IEnumerable<Link> SelectedLinkObjects => Map.Links.Where(l => SelectedLinks.Contains(l.Id));

    /// <summary>Jedyny zaznaczony kafelek (albo null).</summary>
    public Node? SingleNode => SelectedNodes.Count == 1 && SelectionCount == 1 ? Index.Get(SelectedNodes.First()) : null;

    /// <summary>Jedyna zaznaczona ramka (bez kafelków i strzałek) albo null.</summary>
    public Frame? SingleFrame => SelectedFrames.Count == 1 && SelectionCount == 1 ? FindFrame(SelectedFrames.First()) : null;

    /// <summary>Kafelki, do których prowadzą zaznaczone gałęzie.</summary>
    public IEnumerable<Node> SelectedBranchChildren => SelectedBranches.Select(id => Index.Get(id)).OfType<Node>();

    private void PruneSelection()
    {
        int before = SelectionCount;
        SelectedNodes.RemoveWhere(id => !Index.Contains(id) || !Index.IsVisible(Index.Get(id)!));
        var links = Map.Links.Select(l => l.Id).ToHashSet();
        SelectedLinks.RemoveWhere(id => !links.Contains(id));
        SelectedFrames.RemoveWhere(id => FindFrame(id) is null);
        SelectedBranches.RemoveWhere(id => Index.Get(id) is not { Parent: not null } c || Index.Get(c.Parent) is null || !Index.IsVisible(c));
        if (SelectionCount != before) SelectionChanged?.Invoke();
    }
}
