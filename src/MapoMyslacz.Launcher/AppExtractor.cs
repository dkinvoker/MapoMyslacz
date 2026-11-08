using System.IO.Compression;

namespace MapoMyslacz.Launcher;

/// <summary>
/// Rozpakowanie aplikacji: raz na wersję (id = początek SHA-256 paczki), do folderu tymczasowego, a potem
/// jedno przeniesienie — przerwane rozpakowanie nigdy nie zostawia „półaplikacji”. Starsze wersje są sprzątane.
/// </summary>
internal static class AppExtractor
{
    public const string AppExe = "MapoMyslacz.exe";
    public const string CompleteMarker = ".complete";

    /// <summary>Zwraca folder z rozpakowaną aplikacją; rozpakowuje tylko, gdy tej wersji jeszcze nie ma.</summary>
    public static string EnsureExtracted(Func<Stream> openZip, string id, string root)
    {
        id = id.Trim().ToLowerInvariant();
        if (id.Length < 12) throw new InvalidDataException("Uszkodzony identyfikator wersji.");
        id = id[..12];

        var target = Path.Combine(root, id);
        if (IsComplete(target, id)) return target;

        Directory.CreateDirectory(root);
        var temp = $"{target}.tmp{Environment.ProcessId}";
        TryDeleteDirectory(temp);
        using (var zip = openZip())
            ZipFile.ExtractToDirectory(zip, temp, overwriteFiles: true);
        File.WriteAllText(Path.Combine(temp, CompleteMarker), id);

        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true); // niedokończona wcześniejsza próba
            Directory.Move(temp, target);
        }
        catch (IOException) when (IsComplete(target, id))
        {
            // równolegle uruchomiona druga kopia zdążyła pierwsza — użyj jej wyniku
            TryDeleteDirectory(temp);
        }

        CleanupOldVersions(root, id);
        return target;
    }

    public static bool IsComplete(string dir, string id) =>
        File.Exists(Path.Combine(dir, AppExe)) &&
        File.Exists(Path.Combine(dir, CompleteMarker)) &&
        File.ReadAllText(Path.Combine(dir, CompleteMarker)).Trim() == id;

    /// <summary>Usuwa foldery starszych wersji (jeśli któraś jeszcze działa, zostanie usunięta następnym razem).</summary>
    public static void CleanupOldVersions(string root, string currentId)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root).ToList())
            {
                var name = Path.GetFileName(dir);
                if (name.Equals(currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".del", StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteDirectory(dir);
                    continue;
                }
                // świeży folder tymczasowy może właśnie rozpakowywać inny, równolegle uruchomiony starter
                if (name.Contains(".tmp", StringComparison.OrdinalIgnoreCase)
                    && DateTime.UtcNow - Directory.GetCreationTimeUtc(dir) < TimeSpan.FromMinutes(10)) continue;
                // folder w użyciu (działa starsza wersja): przeniesienie się nie uda — nie ruszamy go wcale,
                // zamiast usuwać częściowo i psuć działający program
                try
                {
                    Directory.Move(dir, dir + ".del");
                }
                catch
                {
                    continue;
                }
                TryDeleteDirectory(dir + ".del");
            }
        }
        catch
        {
            // sprzątanie jest opcjonalne
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // jeszcze zajęty — spróbujemy następnym razem
        }
    }

    public static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // plik w użyciu — spróbujemy przy kolejnym starcie
        }
    }
}
