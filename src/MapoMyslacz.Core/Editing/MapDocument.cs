using MapoMyslacz.Core.Model;

namespace MapoMyslacz.Core.Editing;

/// <summary>
/// Plik mapy: jeden albo kilka arkuszy (zakładki), każdy z własną sesją — zaznaczeniem i historią cofania.
/// Obrazki są wspólne dla całego pliku. Na dysku pierwszy arkusz jest korzeniem pliku, a pozostałe leżą w polu
/// <see cref="MindMap.Sheets"/> — starsze wersje programu otwierają więc pierwszy arkusz.
/// </summary>
public sealed class MapDocument
{
    private long _structure, _savedStructure;

    public List<MapSession> Sheets { get; } = [];

    /// <summary>Wspólny magazyn obrazków wszystkich arkuszy.</summary>
    public Dictionary<string, string> Images { get; }

    public string? FilePath { get; private set; }

    /// <summary>Dodano, usunięto, przesunięto arkusz (zakładki trzeba narysować od nowa).</summary>
    public event Action? SheetsChanged;

    private MapDocument(Dictionary<string, string> images, string? filePath)
    {
        Images = images;
        FilePath = filePath;
    }

    /// <summary>Nowy plik z jednym arkuszem (domyślna mapa).</summary>
    public MapDocument() : this([], null) => Sheets.Add(new MapSession(null, null, Images));

    /// <summary>Plik z mapy wczytanej z dysku (pierwszy arkusz + <see cref="MindMap.Sheets"/>).</summary>
    public static MapDocument FromMap(MindMap root, string? filePath)
    {
        var doc = new MapDocument(root.Images ?? [], filePath);
        var rest = root.Sheets ?? [];
        root.Sheets = null;
        root.Images = null;
        doc.Sheets.Add(new MapSession(root, filePath, doc.Images));
        foreach (var sheet in rest)
        {
            sheet.Images = null;
            sheet.Sheets = null;
            doc.Sheets.Add(new MapSession(sheet, filePath, doc.Images));
        }
        return doc;
    }

    /// <summary>Kilka map jako arkusze jednego pliku (np. import z XMind).</summary>
    public static MapDocument FromMaps(IReadOnlyList<MindMap> maps, string? filePath = null)
    {
        var doc = new MapDocument([], filePath);
        foreach (var m in maps)
        {
            if (m.Images is { } imgs)
                foreach (var (id, data) in imgs) doc.Images.TryAdd(id, data);
            m.Images = null;
            m.Sheets = null;
            doc.Sheets.Add(new MapSession(m, filePath, doc.Images));
        }
        if (doc.Sheets.Count == 0) doc.Sheets.Add(new MapSession(null, filePath, doc.Images));
        return doc;
    }

    /// <summary>Jedna sesja jako plik (np. mapa przywrócona albo utworzona w kodzie).</summary>
    public static MapDocument FromSession(MapSession session)
    {
        var doc = new MapDocument(session.Images, session.FilePath);
        doc.Sheets.Add(session);
        return doc;
    }

    public static MapDocument Open(string path) => FromMap(MapSerializer.Load(path), path);

    public bool IsDirty => _structure != _savedStructure || Sheets.Any(s => s.IsDirty);

    /// <summary>Licznik zmian całego pliku (do autozapisu).</summary>
    public long Version => _structure + Sheets.Sum(s => s.Version);

    /// <summary>Nazwa pliku albo tytuł pierwszego arkusza.</summary>
    public string DisplayName => FilePath is null ? Sheets[0].Map.Title ?? "Bez tytułu" : Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Nazwa zakładki: tytuł arkusza albo „Arkusz N”.</summary>
    public string SheetName(int index) => Sheets[index].Map.Title is { Length: > 0 } t ? t : $"Arkusz {index + 1}";

    public void MarkDirty() => _structure++;

    // ------------------------------------------------------------------ arkusze

    /// <summary>Nowy arkusz (pusta mapa z tematem głównym) za wskazanym — zwraca jego numer.</summary>
    public int AddSheet(int after, string? title = null)
    {
        var map = MapSession.CreateDefault();
        map.Title = title ?? $"Arkusz {Sheets.Count + 1}";
        int at = Math.Clamp(after + 1, 0, Sheets.Count);
        Sheets.Insert(at, new MapSession(map, FilePath, Images));
        Changed();
        return at;
    }

    /// <summary>Kopia arkusza (z nowymi id kafelków nie trzeba — każdy arkusz ma własne).</summary>
    public int DuplicateSheet(int index)
    {
        var copy = Sheets[index].ExportSheet(Sheets[index].InitialView);
        copy.Title = $"{SheetName(index)} (kopia)";
        Sheets.Insert(index + 1, new MapSession(copy, FilePath, Images));
        Changed();
        return index + 1;
    }

    /// <summary>Usuwa arkusz (ostatniego nie da się usunąć). Nie da się tego cofnąć — interfejs pyta wcześniej.</summary>
    public bool RemoveSheet(int index)
    {
        if (Sheets.Count <= 1 || index < 0 || index >= Sheets.Count) return false;
        Sheets.RemoveAt(index);
        Changed();
        return true;
    }

    public bool MoveSheet(int from, int to)
    {
        if (from < 0 || from >= Sheets.Count || to < 0 || to >= Sheets.Count || from == to) return false;
        var s = Sheets[from];
        Sheets.RemoveAt(from);
        Sheets.Insert(to, s);
        Changed();
        return true;
    }

    private void Changed()
    {
        _structure++;
        SheetsChanged?.Invoke();
    }

    /// <summary>Arkusz, w którym jest kafelek o danym id (−1 — nigdzie).</summary>
    public int SheetOfNode(string nodeId) => Sheets.FindIndex(s => s.Index.Contains(nodeId));

    // ------------------------------------------------------------------ zapis

    /// <summary>Cały plik jako jedna mapa do zapisu: pierwszy arkusz + pozostałe + używane obrazki.</summary>
    public MindMap ToMap(Func<MapSession, ViewState?> viewOf)
    {
        var root = Sheets[0].ExportSheet(viewOf(Sheets[0]));
        if (Sheets.Count > 1)
        {
            root.Sheets = Sheets.Skip(1).Select(s =>
            {
                var m = s.ExportSheet(viewOf(s));
                m.Format = null!; // w arkuszach zagnieżdżonych znacznik formatu jest zbędny
                m.FormatVersion = 0;
                return m;
            }).ToList();
        }
        root.Images = MapImages.Referenced(Sheets.SelectMany(s => s.Map.Nodes), Images);
        return root;
    }

    public void Save(string path, Func<MapSession, ViewState?> viewOf)
    {
        MapSerializer.Save(ToMap(viewOf), path);
        FilePath = path;
        foreach (var s in Sheets) s.MarkSaved(path);
        _savedStructure = _structure;
    }

    /// <summary>Kopia (autozapis) — bez zmiany ścieżki i znacznika niezapisanych zmian.</summary>
    public void WriteCopy(string path, Func<MapSession, ViewState?> viewOf) => MapSerializer.Save(ToMap(viewOf), path);
}
