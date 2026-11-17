using System.Security.Cryptography;
using System.Text;

namespace MapoMyslacz.Core.Editing;

/// <summary>
/// Historia wersji map: przed zapisem poprzednia zawartość pliku trafia do
/// <c>&lt;folder danych&gt;\history\&lt;nazwa mapy&gt;-&lt;skrót ścieżki&gt;\RRRRMMDD-GGMMSS.mapo</c> (czas to chwila, w której ta
/// wersja została zapisana). Dzięki temu da się wrócić do mapy „z wczoraj” — także po zapisaniu zmian, których
/// nie chcieliśmy. Kopie leżą poza folderem map, więc nie zaśmiecają kampanii.
/// </summary>
public sealed class MapHistory(string root)
{
    /// <summary>Kolejna kopia powstaje najwyżej co tyle (częste Ctrl+S nie zapełniają historii).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);

    /// <summary>Zawsze zostaje tyle najnowszych wersji…</summary>
    public const int KeepNewest = 20;

    /// <summary>…a starsze — po jednej (ostatniej) z każdego dnia, przez tyle dni.</summary>
    public const int KeepDays = 60;

    private const string SourceFile = "mapa.txt";

    public sealed record Version(string Path, DateTime Time, long Size);

    public string Root { get; } = root;

    /// <summary>Folder historii danej mapy: czytelna nazwa + skrót pełnej ścieżki (mapy o tej samej nazwie się nie mieszają).</summary>
    public string FolderFor(string mapPath)
    {
        var full = Path.GetFullPath(mapPath);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant())))[..10].ToLowerInvariant();
        // ta sama mapa zapisana pod ścieżką z inną wielkością liter — ten sam folder
        if (Directory.Exists(Root) && Directory.EnumerateDirectories(Root, "*-" + hash).FirstOrDefault() is { } existing) return existing;
        var name = new string(Path.GetFileNameWithoutExtension(full).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        if (name.Length > 40) name = name[..40];
        return Path.Combine(Root, $"{name}-{hash}");
    }

    /// <summary>Wersje mapy, najnowsza pierwsza.</summary>
    public IReadOnlyList<Version> List(string mapPath)
    {
        var dir = FolderFor(mapPath);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.mapo")
            .Select(p => (Path: p, Time: ParseTime(Path.GetFileNameWithoutExtension(p))))
            .Where(x => x.Time is not null)
            .Select(x => new Version(x.Path, x.Time!.Value, new FileInfo(x.Path).Length))
            .OrderByDescending(v => v.Time)
            .ToList();
    }

    /// <summary>
    /// Wołane tuż przed zapisem mapy: kopiuje obecną zawartość pliku do historii (jako wersję z chwili jej zapisu).
    /// Pomija, gdy pliku jeszcze nie ma, gdy taka sama wersja już jest, albo gdy ostatnia kopia jest młodsza niż
    /// <see cref="MinInterval"/> (<paramref name="force"/> — bez tego ostatniego warunku, np. przed przywróceniem
    /// starej wersji). Potem sprząta stare wersje. Zwraca nową wersję (albo null).
    /// </summary>
    public Version? Backup(string mapPath, DateTime now, bool force = false)
    {
        if (!File.Exists(mapPath)) return null;
        var versions = List(mapPath);
        if (!force && versions.Count > 0 && now - versions[0].Time < MinInterval) return null;
        var bytes = File.ReadAllBytes(mapPath);
        if (versions.Count > 0 && versions[0].Size == bytes.Length && File.ReadAllBytes(versions[0].Path).AsSpan().SequenceEqual(bytes)) return null;

        var dir = FolderFor(mapPath);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SourceFile), Path.GetFullPath(mapPath));
        var saved = File.GetLastWriteTime(mapPath);
        if (saved > now) saved = now;
        var name = saved.ToString("yyyyMMdd-HHmmss");
        var target = Path.Combine(dir, name + ".mapo");
        for (int i = 2; File.Exists(target); i++) target = Path.Combine(dir, $"{name}-{i}.mapo");
        File.WriteAllBytes(target, bytes);
        File.SetLastWriteTime(target, saved);
        Prune(mapPath, now);
        return new Version(target, saved, bytes.Length);
    }

    /// <summary>Usuwa wersje, których nie trzeba trzymać (<see cref="ToRemove"/>).</summary>
    public void Prune(string mapPath, DateTime now)
    {
        foreach (var v in ToRemove(List(mapPath), now))
        {
            try
            {
                File.Delete(v.Path);
            }
            catch (IOException)
            {
                // zablokowana kopia — usunie się następnym razem
            }
        }
    }

    /// <summary>
    /// Które wersje usunąć: zostaje <see cref="KeepNewest"/> najnowszych, a ze starszych — ostatnia z każdego dnia
    /// z ostatnich <see cref="KeepDays"/> dni.
    /// </summary>
    public static List<Version> ToRemove(IReadOnlyList<Version> newestFirst, DateTime now)
    {
        var remove = new List<Version>();
        var days = new HashSet<DateTime>();
        for (int i = 0; i < newestFirst.Count; i++)
        {
            var v = newestFirst[i];
            bool keep = i < KeepNewest || (now - v.Time).TotalDays <= KeepDays && days.Add(v.Time.Date);
            if (i < KeepNewest) days.Add(v.Time.Date);
            if (!keep) remove.Add(v);
        }
        return remove;
    }

    private static DateTime? ParseTime(string name)
    {
        if (name.Length < 15) return null;
        return DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", null, System.Globalization.DateTimeStyles.None, out var t) ? t : null;
    }
}
