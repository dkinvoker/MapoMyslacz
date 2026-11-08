namespace MapoMyslacz.Services;

/// <summary>Informacje o wersji (numer pochodzi z Directory.Build.props).</summary>
public static class AppInfo
{
    private static readonly Version AssemblyVersion = typeof(AppInfo).Assembly.GetName().Version ?? new Version(1, 0);

    /// <summary>Krótki numer, np. "1.0" (z trzecią cyfrą tylko gdy nie jest zerem: "1.0.1").</summary>
    public static string Version => AssemblyVersion.Build > 0
        ? $"{AssemblyVersion.Major}.{AssemblyVersion.Minor}.{AssemblyVersion.Build}"
        : $"{AssemblyVersion.Major}.{AssemblyVersion.Minor}";

    public static string Name => "MapoMyślacz";

    public static string FullName => $"{Name} v{Version}";

    /// <summary>Folder danych programu: %LOCALAPPDATA%\MapoMyslacz (usuwa go skrypt odinstalowania).</summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapoMyslacz");
}
