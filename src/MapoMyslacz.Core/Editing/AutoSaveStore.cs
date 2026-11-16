using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapoMyslacz.Core.Model;

namespace MapoMyslacz.Core.Editing;

/// <summary>Opis autozapisu: skąd pochodzi mapa i który proces go zapisał.</summary>
public sealed class AutoSaveInfo
{
    /// <summary>Plik mapy, którą edytowano (null — mapa jeszcze niezapisana).</summary>
    public string? OriginalPath { get; set; }
    public string? Title { get; set; }
    public DateTime SavedAt { get; set; }
    public int ProcessId { get; set; }

    /// <summary>Czas startu procesu (UTC, ticks) — numer procesu po restarcie Windows może trafić się innemu programowi.</summary>
    public long ProcessStartTicks { get; set; }

    [JsonIgnore] public string MetaPath { get; set; } = "";
    [JsonIgnore] public string MapPath => Path.ChangeExtension(MetaPath, MindMap.FileExtension);

    /// <summary>Nazwa do pokazania użytkownikowi.</summary>
    [JsonIgnore]
    public string DisplayName => OriginalPath is { Length: > 0 } p ? Path.GetFileNameWithoutExtension(p) : Title ?? "Bez tytułu";
}

/// <summary>
/// Autozapis niezapisanych zmian: każde okno ma swój „slot” (para plików: mapa .mapo i opis .json) w folderze
/// autozapisu. Po poprawnym zamknięciu (albo zapisaniu mapy) slot znika; po awarii zostaje i przy następnym
/// uruchomieniu program proponuje przywrócenie.
/// </summary>
public sealed class AutoSaveStore
{
    public const string MetaExtension = ".json";

    public string Folder { get; }

    /// <summary>Slot tego okna (nazwa plików bez rozszerzenia).</summary>
    public string Id { get; }

    public AutoSaveStore(string folder, string? id = null)
    {
        Folder = folder;
        Id = id ?? $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Ids.New()}";
    }

    private string MetaPath => Path.Combine(Folder, Id + MetaExtension);
    private string MapPath => Path.Combine(Folder, Id + MindMap.FileExtension);

    public bool Exists => File.Exists(MetaPath);

    /// <summary>Zapisuje bieżący stan mapy. Najpierw mapa, potem opis — opis bez mapy nigdy nie powstanie.</summary>
    public void Write(MapSession session, ViewState? view, int processId, long processStartTicks)
    {
        Directory.CreateDirectory(Folder);
        session.WriteCopy(MapPath, view);
        var info = new AutoSaveInfo
        {
            OriginalPath = session.FilePath,
            Title = session.Map.Title,
            SavedAt = DateTime.Now,
            ProcessId = processId,
            ProcessStartTicks = processStartTicks,
        };
        File.WriteAllText(MetaPath, JsonSerializer.Serialize(info, AutoSaveJson.Default.AutoSaveInfo), new UTF8Encoding(false));
    }

    /// <summary>Usuwa slot tego okna (mapa zapisana, odrzucona albo okno zamknięte poprawnie).</summary>
    public void Delete() => DeleteFiles(MetaPath, MapPath);

    /// <summary>Wszystkie autozapisy w folderze (najnowsze pierwsze).</summary>
    public static List<AutoSaveInfo> List(string folder)
    {
        var result = new List<AutoSaveInfo>();
        if (!Directory.Exists(folder)) return result;
        foreach (var meta in Directory.EnumerateFiles(folder, "*" + MetaExtension))
        {
            try
            {
                var info = JsonSerializer.Deserialize(File.ReadAllText(meta, Encoding.UTF8), AutoSaveJson.Default.AutoSaveInfo);
                if (info is null) continue;
                info.MetaPath = meta;
                if (File.Exists(info.MapPath)) result.Add(info);
                else DeleteFiles(meta); // opis bez mapy — śmieć
            }
            catch
            {
                // uszkodzony opis — pomijamy (nie blokuje startu)
            }
        }
        return result.OrderByDescending(i => i.SavedAt).ToList();
    }

    /// <summary>
    /// Autozapisy osierocone — ich proces już nie działa (awaria, zabity proces, wyłączony prąd).
    /// Autozapisy innych otwartych okien są pomijane.
    /// </summary>
    public static List<AutoSaveInfo> Orphans(string folder, Func<int, long, bool> isProcessAlive) =>
        List(folder).Where(i => !isProcessAlive(i.ProcessId, i.ProcessStartTicks)).ToList();

    /// <summary>Wczytuje mapę z autozapisu.</summary>
    public static MindMap Load(AutoSaveInfo info) => MapSerializer.Load(info.MapPath);

    public static void Delete(AutoSaveInfo info) => DeleteFiles(info.MetaPath, info.MapPath);

    /// <summary>Usuwa osierocone autozapisy starsze niż <paramref name="maxAge"/> (zapomniane „na później”).</summary>
    public static int Prune(string folder, TimeSpan maxAge, Func<int, long, bool> isProcessAlive)
    {
        int n = 0;
        foreach (var i in Orphans(folder, isProcessAlive))
        {
            if (DateTime.Now - i.SavedAt <= maxAge) continue;
            Delete(i);
            n++;
        }
        return n;
    }

    private static void DeleteFiles(params string[] paths)
    {
        foreach (var p in paths)
        {
            try { File.Delete(p); } catch { }
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AutoSaveInfo))]
internal partial class AutoSaveJson : JsonSerializerContext
{
}
