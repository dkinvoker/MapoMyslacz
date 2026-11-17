using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace MapoMyslacz.UiTests;

/// <summary>Test interfejsu: działa tylko z MAPOMYSLACZ_UI_TESTS=1 (przejmuje mysz i klawiaturę).</summary>
public sealed class UiFactAttribute : FactAttribute
{
    public UiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MAPOMYSLACZ_UI_TESTS")?.Trim() != "1") // cmd: „set X=1 && …” zostawia spację
            Skip = "Testy interfejsu przejmują mysz — uruchom z MAPOMYSLACZ_UI_TESTS=1.";
        else if (AppSession.FindExe() is null)
            Skip = "Nie znaleziono zbudowanej aplikacji (dotnet build src/MapoMyslacz albo MAPOMYSLACZ_APP=ścieżka do exe).";
    }
}

/// <summary>
/// Uruchomiona aplikacja z mapą testową: własny folder tymczasowy (mapa, ustawienia, autozapis), okno na wierzchu,
/// przeliczanie punktów mapy na ekran. Mapa testowa ma widok zapisany w (0, 0) przy 100% — punkt mapy (x, y) leży więc
/// (x, y) DIP od środka płótna.
/// </summary>
internal sealed class AppSession : IDisposable
{
    private readonly string _dir;
    public string MapPath { get; }
    public Application App { get; }
    public UIA3Automation Automation { get; } = new();
    public Window Window { get; }
    public AutomationElement Canvas { get; }
    private readonly double _scale;

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    // proces testów musi znać skalę ekranu — inaczej Windows podaje mu przeskalowane współrzędne (położenia elementów
    // i myszy), a przeliczenie DIP → piksele mnożyłoby skalę drugi raz
    static AppSession() => SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2

    /// <summary>Ścieżka do exe: MAPOMYSLACZ_APP albo najnowszy zbudowany w src/MapoMyslacz/bin.</summary>
    public static string? FindExe()
    {
        if (Environment.GetEnvironmentVariable("MAPOMYSLACZ_APP") is { Length: > 0 } exe && File.Exists(exe)) return exe;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "MapoMyslacz", "bin"))) dir = dir.Parent;
        if (dir is null) return null;
        return Directory.EnumerateFiles(Path.Combine(dir.FullName, "src", "MapoMyslacz", "bin"), "MapoMyslacz.exe", SearchOption.AllDirectories)
            .Where(p => p.Contains("win-x64", StringComparison.OrdinalIgnoreCase) && !p.Contains("publish", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <param name="settings">Dodatkowe ustawienia (fragment JSON, np. <c>"ChildrenAsArrows": true</c>).</param>
    public AppSession(string mapJson, string fileName = "ui-test.mapo", string? settings = null)
    {
        _dir = Path.Combine(Path.GetTempPath(), "mapo-ui-" + Guid.NewGuid().ToString("N")[..8]);
        var data = Path.Combine(_dir, "dane");
        Directory.CreateDirectory(data);
        // bez sprawdzania aktualizacji (sieć) i ze stałym rozmiarem okna
        var extra = settings is null ? "" : ", " + settings;
        File.WriteAllText(Path.Combine(data, "settings.json"),
            $$"""{ "CheckForUpdates": false, "ShowProperties": true, "ShowMinimap": false, "WindowWidth": 1500, "WindowHeight": 1000{{extra}} }""");
        MapPath = Path.Combine(_dir, fileName);
        File.WriteAllText(MapPath, mapJson, new UTF8Encoding(false));

        var psi = new ProcessStartInfo(FindExe()!, $"\"{MapPath}\"") { UseShellExecute = false };
        psi.Environment["MAPOMYSLACZ_DATA_DIR"] = data;
        // diagnostyka: MAPOMYSLACZ_UI_TRACE=plik — aplikacja zapisuje tam zdarzenia myszy i edycji
        if (Environment.GetEnvironmentVariable("MAPOMYSLACZ_UI_TRACE") is { Length: > 0 } trace) psi.Environment["MAPOMYSLACZ_TRACE"] = trace;
        App = Application.Launch(psi);
        try
        {
            Window = App.GetMainWindow(Automation, TimeSpan.FromSeconds(30))
                     ?? throw new InvalidOperationException("Okno aplikacji się nie pojawiło.");
            Canvas = Retry(() => Window.FindFirstDescendant(cf => cf.ByAutomationId("Map")))
                     ?? throw new InvalidOperationException("Nie znaleziono płótna (AutomationId „Map”).");
            _scale = GetDpiForWindow(Window.Properties.NativeWindowHandle.Value) / 96.0;
            Focus();
            Thread.Sleep(1200); // pierwsze rysowanie i układ panelu
        }
        catch
        {
            Dispose(); // nieudany start — nie zostawiamy otwartej aplikacji
            throw;
        }
    }

    private static T? Retry<T>(Func<T?> find, int timeoutMs = 10_000) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (find() is { } r) return r;
            Thread.Sleep(200);
        }
        return null;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, IntPtr extra);

    /// <summary>
    /// Okno na wierzch — wejście z klawiatury i myszy ma trafić do aplikacji, nie do innych okien. Tylko gdy nie jest
    /// już na wierzchu i bez fokusu UI Automation (ten zabierałby fokus płótnu albo polu edycji tekstu).
    /// </summary>
    public void Focus()
    {
        var hwnd = Window.Properties.NativeWindowHandle.Value;
        if (GetForegroundWindow() == hwnd) return;
        keybd_event(0x12, 0, 0, IntPtr.Zero); // Alt — Windows pozwala wtedy przenieść okno na wierzch
        keybd_event(0x12, 0, 2, IntPtr.Zero);
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        if (GetForegroundWindow() != hwnd) throw new InvalidOperationException("Okno aplikacji nie jest na wierzchu — przerywam, żeby nie pisać w innych oknach.");
    }

    /// <summary>Punkt mapy na ekranie (piksele).</summary>
    public Point Screen(double x, double y)
    {
        var r = Canvas.BoundingRectangle;
        return new Point((int)Math.Round(r.X + r.Width / 2.0 + x * _scale), (int)Math.Round(r.Y + r.Height / 2.0 + y * _scale));
    }

    public void Click(double x, double y)
    {
        Focus();
        Mouse.Click(Screen(x, y));
        Thread.Sleep(500);
    }

    /// <summary>Klik z przytrzymanym klawiszem (Ctrl/Shift) — puszczony dopiero, gdy aplikacja obsłuży klik.</summary>
    public void ClickWith(VirtualKeyShort modifier, double x, double y)
    {
        Focus();
        using (Keyboard.Pressing(modifier))
        {
            Mouse.Click(Screen(x, y));
            Thread.Sleep(500);
        }
        Thread.Sleep(200);
    }

    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>
    /// Ruch myszy prawdziwym zdarzeniem wejścia (współrzędne bezwzględne na całym pulpicie). SetCursorPos, którego
    /// używa FlaUI, przesuwa kursor bez zdarzeń wskaźnika — WinUI nie widzi wtedy przeciągania.
    /// </summary>
    private static void MoveTo(Point p)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        const uint Move = 0x0001, Absolute = 0x8000, VirtualDesk = 0x4000;
        mouse_event(Move | Absolute | VirtualDesk, (int)((p.X - vx) * 65535L / Math.Max(1, vw - 1)), (int)((p.Y - vy) * 65535L / Math.Max(1, vh - 1)), 0, IntPtr.Zero);
    }

    /// <summary>Przeciąganie lewym przyciskiem w kilku krokach (aplikacja dostaje zwykłe ruchy myszy).</summary>
    public void Drag(double x1, double y1, double x2, double y2, VirtualKeyShort? modifier = null)
    {
        Focus();
        IDisposable? held = modifier is { } m ? Keyboard.Pressing(m) : null;
        try
        {
            MoveTo(Screen(x1, y1));
            Thread.Sleep(100);
            Mouse.Down(MouseButton.Left);
            for (int i = 1; i <= 15; i++)
            {
                MoveTo(Screen(x1 + (x2 - x1) * i / 15, y1 + (y2 - y1) * i / 15));
                Thread.Sleep(25);
            }
            Thread.Sleep(150);
            Mouse.Up(MouseButton.Left);
            Thread.Sleep(400);
        }
        finally
        {
            held?.Dispose();
        }
        Thread.Sleep(300);
    }

    public void Type(string text)
    {
        Focus();
        Keyboard.Type(text);
        Thread.Sleep(300);
    }

    public void Press(params VirtualKeyShort[] keys)
    {
        Focus();
        Keyboard.TypeSimultaneously(keys);
        Thread.Sleep(500);
    }

    /// <summary>Klik w środek elementu interfejsu (np. pola w panelu właściwości) po jego AutomationId.</summary>
    public void ClickElement(string automationId)
    {
        var e = Retry(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)))
                ?? throw new InvalidOperationException($"Nie ma elementu „{automationId}”.");
        Focus();
        // element może leżeć poza widocznym fragmentem przewijanego panelu — fokus przewija go do widoku
        e.Focus();
        Thread.Sleep(300);
        var r = e.BoundingRectangle;
        MoveTo(new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
        Thread.Sleep(80);
        Mouse.Click();
        Thread.Sleep(400);
    }

    /// <summary>Tekst elementu (np. licznik kafelków na pasku stanu) po jego nazwie w XAML.</summary>
    public string TextOf(string automationId) =>
        Retry(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)))?.Name ?? "";

    public string Counts => TextOf("CountText");

    /// <summary>Ctrl+S (mapa ma już plik — zapis bez okna) i odczyt zapisanej mapy.</summary>
    public MindMap SaveAndRead()
    {
        var before = File.GetLastWriteTimeUtc(MapPath);
        Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_S);
        var sw = Stopwatch.StartNew();
        while (File.GetLastWriteTimeUtc(MapPath) == before && sw.ElapsedMilliseconds < 8000) Thread.Sleep(100);
        Assert.NotEqual(before, File.GetLastWriteTimeUtc(MapPath));
        Thread.Sleep(200);
        return MapSerializer.Load(MapPath);
    }

    public void Dispose()
    {
        try { App.Kill(); } catch { }
        App.Dispose();
        Automation.Dispose();
        for (int i = 0; i < 10; i++)
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }
}
