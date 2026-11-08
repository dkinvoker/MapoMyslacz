using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapoMyslacz.Services;

/// <summary>Ustawienia użytkownika w %LOCALAPPDATA%\MapoMyslacz\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Sprawdzaj przy starcie (najwyżej raz dziennie), czy na GitHubie jest nowa wersja.</summary>
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>Wersja, o której użytkownik powiedział „pomiń” — nie przypominamy o niej.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Ostatnio otwierane mapy (najnowsza pierwsza).</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Panel właściwości po prawej stronie.</summary>
    public bool ShowProperties { get; set; } = true;

    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public void AddRecent(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 12) RecentFiles.RemoveRange(12, RecentFiles.Count - 12);
        Save();
    }

    // ------------------------------------------------------------------ zapis

    private static string FilePath => Path.Combine(AppInfo.DataFolder, "settings.json");

    private static AppSettings? _current;

    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), SettingsJson.Default.AppSettings) ?? new AppSettings();
        }
        catch
        {
            // uszkodzony plik — zaczynamy od domyślnych
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
        }
        catch
        {
            // brak zapisu ustawień nie może zablokować pracy
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext
{
}
