using MapoMyslacz.Core.Model;
using Microsoft.Win32;

namespace MapoMyslacz.Services;

/// <summary>
/// Skojarzenie plików .mapo z aplikacją — dwuklik w Eksploratorze otwiera mapę.
/// Wpisy idą do HKCU\Software\Classes (tylko dla bieżącego użytkownika, bez uprawnień administratora);
/// skrypt odinstalowania je usuwa. Przy każdym starcie ścieżka jest odświeżana, więc działa też po przeniesieniu exe.
/// </summary>
internal static class FileAssociation
{
    public const string Extension = MindMap.FileExtension;
    public const string ProgId = "MapoMyslacz.Mapa";

    /// <summary>Jednoplikowy starter podaje tu swoją ścieżkę — to on ma otwierać pliki, nie rozpakowana kopia.</summary>
    public const string LauncherVariable = "MAPOMYSLACZ_LAUNCHER";

    /// <summary>Exe, który ma otwierać pliki: starter (jeśli uruchomiono przez niego) albo ten program.</summary>
    public static string? TargetExe
    {
        get
        {
            var exe = Environment.GetEnvironmentVariable(LauncherVariable);
            return !string.IsNullOrEmpty(exe) && File.Exists(exe) ? exe : Environment.ProcessPath;
        }
    }

    /// <summary>Rejestruje skojarzenie (w tle, przy starcie okna). Wersja Debug niczego nie zmienia w systemie.</summary>
    public static void EnsureRegistered()
    {
#if !DEBUG
        try
        {
            var exe = TargetExe;
            if (string.IsNullOrEmpty(exe)) return;

            using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
            bool changed = false;
            changed |= Set(classes, ProgId, null, "Mapa myśli (MapoMyślacz)");
            changed |= Set(classes, $@"{ProgId}\DefaultIcon", null, $"\"{exe}\",0");
            changed |= Set(classes, $@"{ProgId}\shell\open\command", null, $"\"{exe}\" \"%1\"");
            changed |= Set(classes, Extension, null, ProgId);
            changed |= Set(classes, Extension, "PerceivedType", "document");
            changed |= Set(classes, Extension, "Content Type", "application/x-mapomyslacz+json");
            changed |= Set(classes, $@"{Extension}\OpenWithProgids", ProgId, "");
            // Eksplorator odświeża ikony i „Otwórz za pomocą” dopiero po tym powiadomieniu
            if (changed) Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, 0, 0, 0);
        }
        catch
        {
            // brak dostępu do rejestru itp. — aplikacja działa normalnie, tylko bez dwukliku
        }
#endif
    }

    /// <summary>Zapisuje wartość, jeśli jest inna niż obecna. Zwraca true, gdy coś zmieniono.</summary>
    private static bool Set(RegistryKey root, string path, string? name, string value)
    {
        using var key = root.CreateSubKey(path);
        if (key.GetValue(name) is string current && current == value) return false;
        key.SetValue(name, value, RegistryValueKind.String);
        return true;
    }
}
