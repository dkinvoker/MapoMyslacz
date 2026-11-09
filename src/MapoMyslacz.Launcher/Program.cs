using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MapoMyslacz.Launcher;

/// <summary>
/// Jednoplikowy starter: rozpakowuje osadzoną aplikację (raz na wersję) do %LOCALAPPDATA%\MapoMyslacz\app
/// i ją uruchamia, przekazując argumenty (np. plik .mapo z dwukliku albo opcje eksportu z linii poleceń).
/// Obok siebie kładzie skrypt odinstalowania.
/// </summary>
internal static partial class Program
{
    /// <summary>Zmienna, przez którą aplikacja poznaje ścieżkę startera (skojarzenie plików, aktualizacje).</summary>
    private const string LauncherVariable = "MAPOMYSLACZ_LAUNCHER";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var self = Environment.ProcessPath;
            // po aktualizacji z aplikacji zostaje poprzedni plik startera — już niepotrzebny
            if (self is not null) AppExtractor.TryDeleteFile(self + ".old");

            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapoMyslacz", "app");
            string id;
            using (var reader = new StreamReader(OpenResource("payload.id")))
                id = reader.ReadToEnd();
            var appDir = AppExtractor.EnsureExtracted(() => OpenResource("payload.zip"), id, root);

            var psi = new ProcessStartInfo(Path.Combine(appDir, AppExtractor.AppExe))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            // aplikacja kojarzy pliki .mapo z TYM exe (rozpakowana kopia zmienia folder przy każdej wersji)
            if (self is not null) psi.Environment[LauncherVariable] = self;

            AllowSetForegroundWindow(-1); // okno aplikacji może wyjść na pierwszy plan
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Nie udało się uruchomić procesu aplikacji.");
            UninstallScript.TryWrite(self);

            // tryb linii poleceń (--export-png / --export-pdf): czekamy na wynik i zwracamy kod wyjścia; okno — od razu kończymy
            bool cli = args.Any(a => a.StartsWith("--export-", StringComparison.OrdinalIgnoreCase));
            if (!cli) return 0;
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBoxW(0, $"Nie udało się uruchomić MapoMyślacza.\n\n{ex.Message}", "MapoMyślacz", 0x10 /* MB_ICONERROR */);
            return 1;
        }
    }

    private static Stream OpenResource(string name) =>
        typeof(Program).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidDataException("Ten plik nie zawiera aplikacji (zbuduj go przez publish.ps1).");

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
