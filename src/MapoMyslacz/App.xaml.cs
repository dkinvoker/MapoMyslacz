using MapoMyslacz.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace MapoMyslacz;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => LogError("Nieobsłużony wyjątek", e.Exception);
    }

    /// <summary>Dziennik błędów w %LOCALAPPDATA%\MapoMyslacz\crash.log (do zgłaszania problemów).</summary>
    public static void LogError(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            var path = Path.Combine(AppInfo.DataFolder, "crash.log");
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {AppInfo.FullName} — {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // dziennik nie może sam powodować awarii
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var cli = Environment.GetCommandLineArgs().Skip(1).ToArray();
        int exportIndex = Array.FindIndex(cli, a => string.Equals(a, "--export-png", StringComparison.OrdinalIgnoreCase));
        if (exportIndex >= 0)
        {
            _ = RunCliExportAsync(cli, exportIndex);
            return;
        }

        // pierwszy argument, który jest istniejącym plikiem = mapa do otwarcia (dwuklik w Eksploratorze)
        var file = cli.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
        _window = new MainWindow(file);
        _window.Activate();
        // dwuklik na .mapo w Eksploratorze — rejestracja w tle, żeby nie opóźniać pojawienia się okna
        _ = Task.Run(FileAssociation.EnsureRegistered);
    }

    /// <summary>
    /// Eksport bez obsługi okna:  MapoMyslacz.exe mapa.mapo --export-png obraz.png [--scale 2]
    /// (działa też z plikiem .xmind). Okno powstaje poza ekranem, bo XAML potrzebuje go do renderowania.
    /// </summary>
    private async Task RunCliExportAsync(string[] cli, int exportIndex)
    {
        int code = 2;
        try
        {
            string? Arg(string name)
            {
                int i = Array.FindIndex(cli, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < cli.Length ? cli[i + 1] : null;
            }
            var output = exportIndex + 1 < cli.Length ? cli[exportIndex + 1] : null;
            string? input = null;
            for (int i = 0; i < cli.Length; i++)
            {
                if (cli[i].StartsWith("--", StringComparison.Ordinal)) { i++; continue; }
                input = cli[i];
                break;
            }
            double scale = double.TryParse(Arg("--scale"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var sc) ? sc : 2;
            if (input is not null && output is not null && File.Exists(input))
            {
                _window = new MainWindow();
                _window.AppWindow.IsShownInSwitchers = false;
                _window.AppWindow.Move(new PointInt32(-32000, -32000));
                _window.Activate();
                code = await _window.RunCliExportAsync(input, output, scale);
            }
        }
        catch (Exception ex)
        {
            LogError("Eksport z linii poleceń", ex);
            code = 1;
        }
        Environment.Exit(code);
    }
}
