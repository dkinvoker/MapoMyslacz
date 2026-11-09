using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Model;
using MapoMyslacz.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.System;

namespace MapoMyslacz;

public sealed partial class MainWindow : Window
{
    private MapSession _session = null!;
    private bool _forceClose, _dialogOpen, _loaded;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _toastTimer;
    private readonly TaskCompletionSource _loadedTcs = new();

    public MainWindow(string? openPath = null)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        SystemBackdrop = new MicaBackdrop();
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch { }
        PlaceWindow();
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += (_, _) => UpdateCaptionInset();
        RootHost.Loaded += OnLoaded;

        _toastTimer = DispatcherQueue.CreateTimer();
        _toastTimer.Interval = TimeSpan.FromSeconds(4);
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.IsOpen = false;
        };

        // tło komunikatów pojawia się i znika razem z nimi; cień odcina je od płótna
        Toast.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty,
            (_, _) => ToastHost.Visibility = Toast.IsOpen ? Visibility.Visible : Visibility.Collapsed);
        UpdateBar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty,
            (_, _) => UpdateHost.Visibility = UpdateBar.IsOpen ? Visibility.Visible : Visibility.Collapsed);
        foreach (var host in new[] { ToastHost, UpdateHost })
        {
            host.Shadow = new ThemeShadow();
            host.Translation = new System.Numerics.Vector3(0, 0, 24);
        }

        Map.ViewChanged += UpdateZoomText;
        Map.Notify += m => ShowToast(m, InfoBarSeverity.Informational);
        Map.HintChanged += h => StatusText.Text = h ?? DefaultHint;
        Map.OpenLinkRequested += n => _ = OpenLinkAsync(n);
        Closed += (_, _) => Map.Shutdown();
        BuildStyleMenu();
        AddAccelerators();
        PanelToggle.IsChecked = _settings.ShowProperties;
        PropsPanel.Visibility = _settings.ShowProperties ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = DefaultHint;
        LoadSession(OpenAtStartup(openPath) ?? new MapSession());
    }

    private string? _startupError;

    /// <summary>Mapa z linii poleceń (dwuklik w Eksploratorze) — wczytana od razu, żeby okno pokazało się już z nią.</summary>
    private MapSession? OpenAtStartup(string? path)
    {
        if (path is null) return null;
        try
        {
            if (path.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase))
            {
                var map = Core.Import.XmindImporter.Import(path)[0];
                map.Title ??= Path.GetFileNameWithoutExtension(path);
                var imported = new MapSession(map);
                imported.MarkDirty();
                return imported;
            }
            var s = MapSession.Open(path);
            _settings.AddRecent(path);
            return s;
        }
        catch (Exception ex)
        {
            _startupError = $"{Path.GetFileName(path)}\n\n{ex.Message}";
            return null;
        }
    }

    private const string DefaultHint =
        "Przeciągnij płótno — przesuwanie · Shift/Ctrl + przeciągnij — zaznaczanie · kółko myszy — powiększenie · dwuklik — nowy kafelek · F1 — pomoc";

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        UpdateCaptionInset();
        Map.Focus(FocusState.Programmatic);
        _loadedTcs.TrySetResult();
        if (_startupError is not null) await ShowErrorAsync("Nie udało się otworzyć mapy", _startupError);
        SetupUpdates();
    }

    /// <summary>Czeka, aż okno ma rozmiar i drzewo XAML (dla trybu eksportu z linii poleceń).</summary>
    public Task WhenLoaded => _loadedTcs.Task;

    // ================================================================== okno

    private void PlaceWindow()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        double scale = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = _settings.WindowWidth > 0 ? _settings.WindowWidth : (int)(1360 * scale);
        int h = _settings.WindowHeight > 0 ? _settings.WindowHeight : (int)(860 * scale);
        w = Math.Min(w, area.Width);
        h = Math.Min(h, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            op.PreferredMinimumWidth = (int)(720 * scale);
            op.PreferredMinimumHeight = (int)(480 * scale);
            if (_settings.WindowMaximized) op.Maximize();
        }
    }

    private void UpdateCaptionInset()
    {
        var xr = RootHost.XamlRoot;
        if (xr is null) return;
        double inset = AppWindow.TitleBar.RightInset / xr.RasterizationScale;
        if (inset > 0) CaptionColumn.Width = new GridLength(inset + 8);
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        SaveWindowPlacement();
        if (_forceClose || !_session.IsDirty) return;
        args.Cancel = true;
        if (_dialogOpen) return;
        if (await ConfirmDiscardAsync())
        {
            _forceClose = true;
            Close();
        }
    }

    private void SaveWindowPlacement()
    {
        if (AppWindow.Presenter is OverlappedPresenter op)
        {
            _settings.WindowMaximized = op.State == OverlappedPresenterState.Maximized;
            if (op.State == OverlappedPresenterState.Restored)
            {
                _settings.WindowWidth = AppWindow.Size.Width;
                _settings.WindowHeight = AppWindow.Size.Height;
            }
        }
        _settings.Save();
    }

    private void AddAccelerators()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action action)
        {
            var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
            acc.Invoked += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            RootHost.KeyboardAccelerators.Add(acc);
        }
        RootHost.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Add(VirtualKey.N, VirtualKeyModifiers.Control, () => _ = NewAsync());
        Add(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = OpenAsync());
        Add(VirtualKey.S, VirtualKeyModifiers.Control, () => _ = SaveAsync(saveAs: false));
        Add(VirtualKey.S, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = SaveAsync(saveAs: true));
        Add(VirtualKey.E, VirtualKeyModifiers.Control, () => _ = ExportPngAsync());
        Add(VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = ExportPdfAsync());
        Add(VirtualKey.P, VirtualKeyModifiers.Control, () => _ = PrintAsync());
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, () => _ = GoBackAsync());
        Add(VirtualKey.F1, VirtualKeyModifiers.None, () => _ = ShowHelpAsync());
    }

    // ================================================================== sesja

    private void LoadSession(MapSession s)
    {
        if (_session is not null)
        {
            _session.Changed -= OnSessionChanged;
            _session.SelectionChanged -= OnSelectionChanged;
        }
        _session = s;
        s.Changed += OnSessionChanged;
        s.SelectionChanged += OnSelectionChanged;
        Map.Session = s;
        UpdateTitle();
        RebuildProperties();
        UpdateCounts();
    }

    private void OnSessionChanged()
    {
        UpdateTitle();
        RefreshProperties();
        UpdateCounts();
    }

    private bool _propertiesPending;

    /// <summary>
    /// Panel przebudowujemy raz, gdy interfejs jest bezczynny — seria zmian zaznaczenia (np. kilka kliknięć z Ctrl,
    /// Zaznacz wszystko) nie przebudowuje go po kilkadziesiąt razy.
    /// </summary>
    private void OnSelectionChanged()
    {
        UpdateCounts();
        if (_propertiesPending) return;
        _propertiesPending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _propertiesPending = false;
            RebuildProperties();
        });
    }

    private void UpdateTitle()
    {
        var text = $"{(_session.IsDirty ? "● " : "")}{_session.DisplayName} — {AppInfo.Name} {AppInfo.Version}";
        TitleText.Text = text;
        Title = text;
    }

    private void UpdateCounts()
    {
        int nodes = _session.Map.Nodes.Count, links = _session.Map.Links.Count;
        int sel = _session.SelectedNodes.Count + _session.SelectedLinks.Count;
        CountText.Text = $"{nodes} {Plural(nodes, "kafelek", "kafelki", "kafelków")} · {links} {Plural(links, "strzałka", "strzałki", "strzałek")}"
                         + (sel > 0 ? $" · zaznaczono {sel}" : "");
    }

    internal static string Plural(int n, string one, string few, string many)
    {
        if (n == 1) return one;
        int last = n % 10, last2 = n % 100;
        return last is >= 2 and <= 4 && last2 is < 12 or > 14 ? few : many;
    }

    private void UpdateZoomText() => ZoomText.Text = $"{Math.Round(Map.Zoom * 100)}%";

    // ================================================================== komunikaty

    private void ShowToast(string message, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        Toast.Severity = severity;
        Toast.Message = message;
        Toast.IsOpen = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ================================================================== pasek narzędzi

    private void New_Click(object sender, RoutedEventArgs e) => _ = NewAsync();
    private void Open_Click(object sender, RoutedEventArgs e) => _ = OpenAsync();
    private void Save_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(saveAs: false);
    private void SaveAs_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(saveAs: true);
    private void ImportXmind_Click(object sender, RoutedEventArgs e) => _ = ImportXmindAsync();
    private void ExportPng_Click(object sender, RoutedEventArgs e) => _ = ExportPngAsync();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void Undo_Click(object sender, RoutedEventArgs e) => Map.Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Map.Redo();
    private void AddFloating_Click(object sender, RoutedEventArgs e) => Map.AddFloating();
    private void AddChild_Click(object sender, RoutedEventArgs e) => Map.AddChild();
    private void AddSibling_Click(object sender, RoutedEventArgs e) => Map.AddSibling();
    private void Connect_Click(object sender, RoutedEventArgs e) => Map.ConnectSelected();
    private void Delete_Click(object sender, RoutedEventArgs e) => Map.DeleteSelection();
    private void Arrange_Click(object sender, RoutedEventArgs e) => Map.ArrangeSelection();
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1.2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1 / 1.2);
    private void Zoom100_Click(object sender, RoutedEventArgs e) => Map.ZoomTo(1);
    private void Fit_Click(object sender, RoutedEventArgs e) => Map.FitToContent();
    private void Help_Click(object sender, RoutedEventArgs e) => _ = ShowHelpAsync();
    private void About_Click(object sender, RoutedEventArgs e) => _ = ShowAboutAsync();

    private void PanelToggle_Click(object sender, RoutedEventArgs e)
    {
        bool show = PanelToggle.IsChecked == true;
        PropsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _settings.ShowProperties = show;
        _settings.Save();
    }

    private void BuildStyleMenu()
    {
        StyleMenu.Items.Clear();
        foreach (var p in Styles.Presets)
        {
            var preset = p;
            var item = new MenuFlyoutItem { Text = $"{p.Name} — {p.Description}" };
            item.Click += (_, _) =>
            {
                if (_session.SelectedNodes.Count == 0) ShowToast("Najpierw zaznacz kafelki, którym chcesz nadać styl.", InfoBarSeverity.Informational);
                else Map.ApplyPreset(preset);
            };
            StyleMenu.Items.Add(item);
        }
    }

    private void FileMenu_Opening(object sender, object e)
    {
        RecentMenu.Items.Clear();
        var recent = _settings.RecentFiles.Where(File.Exists).ToList();
        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutItem { Text = "(pusto)", IsEnabled = false });
            return;
        }
        foreach (var path in recent)
        {
            var p = path;
            var item = new MenuFlyoutItem { Text = Path.GetFileNameWithoutExtension(p) };
            ToolTipService.SetToolTip(item, p);
            item.Click += (_, _) => _ = OpenFileAsync(p);
            RecentMenu.Items.Add(item);
        }
    }

    // ================================================================== przeciąganie plików na okno

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Otwórz mapę";
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault(f =>
            f.FileType.Equals(MindMap.FileExtension, StringComparison.OrdinalIgnoreCase) ||
            f.FileType.Equals(".xmind", StringComparison.OrdinalIgnoreCase) ||
            f.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            ShowToast("Upuść plik mapy (.mapo) albo XMind (.xmind).", InfoBarSeverity.Warning);
            return;
        }
        await OpenFileAsync(file.Path);
    }

    // ================================================================== pomoc

    private async Task ShowHelpAsync()
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, LineHeight = 22 };
        void H(string s) => text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = s + "\n", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        void L(string s) => text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = s + "\n" });
        H("Mysz");
        L("Przeciągnij puste miejsce — przesuwa płótno (też środkowy przycisk albo spacja + przeciągnięcie)");
        L("Shift albo Ctrl + przeciągnij od pustego miejsca — zaznacza prostokątem (dokłada do zaznaczenia)");
        L("Kółko myszy — powiększenie w miejscu kursora, Shift + kółko — przewijanie w poziomie");
        L("Klik — zaznacza, Ctrl+klik — dodaje/odejmuje od zaznaczenia, Shift+klik — dodaje");
        L("Przeciągnij kafelek — przesuwa go z całą gałęzią (Alt — bez gałęzi, Shift — tylko poziomo/pionowo)");
        L("Dwuklik na kafelku — edycja tekstu, na strzałce — podpis, na pustym miejscu — nowy kafelek");
        L("Kółko „+” przy zaznaczonym kafelku: klik — nowe dziecko, przeciągnij na kafelek — strzałka, na puste miejsce — dziecko w tym miejscu");
        L("Zaznaczona strzałka: przeciągnij ją, żeby wygiąć, albo przesuń białe uchwyty krzywizny");
        L("Prawy przycisk — menu z poleceniami\n");
        H("Klawiatura");
        L("Tab / Insert — dziecko · Enter — rodzeństwo · Shift+Tab — przejdź do rodzica");
        L("F2 albo po prostu zacznij pisać — edycja tekstu · Shift+Enter — nowy wiersz w tekście · Esc — anuluj");
        L("Strzałki — przejście do sąsiedniego kafelka · Ctrl+strzałki — przesunięcie (z Shift co 1 px)");
        L("Delete — usuń · Ctrl+Z / Ctrl+Y — cofnij / ponów · Ctrl+A — zaznacz wszystko");
        L("Ctrl+C / Ctrl+X / Ctrl+V — kopiuj / wytnij / wklej (wklejony tekst z wcięciami staje się gałęziami) · Ctrl+D — duplikuj");
        L("Ctrl+L — połącz strzałką (dwa zaznaczone kafelki albo wskaż drugi) · Ctrl+R — uporządkuj gałęzie · Ctrl+/ — zwiń/rozwiń");
        L("Ctrl+B / Ctrl+I — pogrubienie / kursywa · Ctrl+0 — cała mapa · Ctrl+1 — 100% · Ctrl++ / Ctrl+− — powiększenie");
        L("Ctrl+N / Ctrl+O / Ctrl+S / Ctrl+Shift+S — nowa / otwórz / zapisz / zapisz jako");
        L("Ctrl+P — drukuj · Ctrl+Shift+E — PDF · Ctrl+E — obraz PNG\n");
        H("Znaczniki i linki");
        L("Znaczniki (priorytet, postęp, emoji) — panel po prawej albo menu kontekstowe kafelka");
        L("Link w kafelku: adres strony albo inna mapa .mapo / dowolny plik (panel → Link)");
        L("Ctrl+klik na kafelku albo klik w niebieski znaczek — otwiera link; inna mapa otwiera się w tym oknie");
        L("Alt+← albo strzałka „wstecz” na pasku — powrót do poprzedniej mapy");
        var dlg = new ContentDialog
        {
            Title = "Obsługa MapoMyślacza",
            Content = new ScrollViewer { Content = text, MaxHeight = 520 },
            CloseButtonText = "Zamknij",
        };
        await ShowDialogAsync(dlg);
    }

    private async Task ShowAboutAsync()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"{AppInfo.Name} {AppInfo.Version}", Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "Mapy myśli: kafelki, gałęzie i strzałki na nieskończonym płótnie. WinUI 3 + Native AOT.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = $"Pliki map: *{MindMap.FileExtension} (JSON). Dane programu: {AppInfo.DataFolder}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            IsTextSelectionEnabled = true,
        });
        var link = new HyperlinkButton { Content = "Strona projektu na GitHubie", NavigateUri = new Uri($"https://github.com/{Core.Updates.UpdateLogic.Repository}") };
        panel.Children.Add(link);
        await ShowDialogAsync(new ContentDialog { Title = "O programie", Content = panel, CloseButtonText = "OK" });
    }
}
