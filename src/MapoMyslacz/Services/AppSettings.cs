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

    /// <summary>Minimapa w rogu płótna (gdy mapa nie mieści się w oknie).</summary>
    public bool ShowMinimap { get; set; } = true;

    /// <summary>Delikatna siatka kropek na płótnie.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>
    /// Nowe kafelki z Tab, Enter i kółka „+” łączone strzałką zamiast gałęzi drzewa (swobodne kafelki ze strzałką,
    /// którą można wyginać i podpisywać).
    /// </summary>
    public bool ChildrenAsArrows { get; set; }

    /// <summary>Przyciąganie przeciąganego kafelka do krawędzi i środków sąsiadów (linie pomocnicze).</summary>
    public bool SnapToNeighbors { get; set; } = true;

    /// <summary>Przyciąganie środka kafelka do kropek siatki, gdy siatka jest widoczna.</summary>
    public bool SnapToGrid { get; set; } = true;

    /// <summary>Co ile minut niezapisane zmiany trafiają do autozapisu.</summary>
    public int AutoSaveMinutes { get; set; } = 1;

    /// <summary>Motyw nowych map (id z MapThemes; brak — domyślny).</summary>
    public string? NewMapTheme { get; set; }

    /// <summary>Własne gotowe style („Zapisz styl zaznaczonego kafelka”) — dostępne we wszystkich mapach.</summary>
    public List<CustomStyle> CustomStyles { get; set; } = [];

    /// <summary>Ostatnie ustawienia druku/PDF (zapamiętywane między uruchomieniami).</summary>
    public Core.Export.PrintOptions Print { get; set; } = new();

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

/// <summary>Własny gotowy styl kafelka.</summary>
public sealed class CustomStyle
{
    public string Name { get; set; } = "";
    public Core.Model.NodeStyle Style { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
internal partial class SettingsJson : JsonSerializerContext
{
}
