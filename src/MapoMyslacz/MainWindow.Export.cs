using System.Diagnostics;
using MapoMyslacz.Core.Export;
using MapoMyslacz.Core.Model;
using MapoMyslacz.Editor;
using MapoMyslacz.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MapoMyslacz;

/// <summary>Druk i eksport PDF (z ustawieniami i podglądem) oraz linki między mapami.</summary>
public sealed partial class MainWindow
{
    // ================================================================== druk i PDF

    /// <summary>Ostatni wybór „PDF ze wszystkich arkuszy” (pamiętany do końca pracy z programem).</summary>
    private bool _pdfAllSheets = true;

    private async Task ExportPdfAsync()
    {
        CommitAllEdits();
        if (Map.ExportBounds is null)
        {
            ShowToast("Mapa jest pusta — nie ma czego eksportować.", InfoBarSeverity.Warning);
            return;
        }
        var o = await ShowPrintOptionsAsync(forPrinter: false);
        if (o is null) return;
        var path = await PickSaveAsync(SafeFileName(_session.DisplayName), "Dokument PDF", ".pdf");
        if (path is null) return;
        try
        {
            ShowToast("Tworzę PDF…", InfoBarSeverity.Informational);
            int pages = _pdfAllSheets && _document.Sheets.Count > 1
                ? await Map.ExportPdfAsync(path, o, _document.Sheets.Select((s, i) => (s.Map, s.Index, _document.SheetName(i))).ToList(), AppInfo.FullName)
                : await Map.ExportPdfAsync(path, o, MapTitle, AppInfo.FullName);
            ShowToast($"Zapisano PDF: {Path.GetFileName(path)} ({pages} {Plural(pages, "strona", "strony", "stron")})");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się zapisać PDF", ex.Message);
        }
    }

    private async Task PrintAsync()
    {
        CommitAllEdits();
        if (Map.ExportBounds is not { } content)
        {
            ShowToast("Mapa jest pusta — nie ma czego drukować.", InfoBarSeverity.Warning);
            return;
        }
        var o = await ShowPrintOptionsAsync(forPrinter: true);
        if (o is null) return;
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        using var scene = await Map.CreatePrintSceneAsync(o);
        var title = MapTitle;
        var r = PrintService.Print(hwnd, content.Inflate(4), o, title,
            (page, plan, n, count, dpi) => MapCanvas.RenderPage(scene, page, plan, o, title, n, count, dpi));
        if (r.Printed) ShowToast($"Wysłano do drukarki: {r.Pages} {Plural(r.Pages, "strona", "strony", "stron")}.");
        else if (r.Error is not null) await ShowErrorAsync("Nie udało się wydrukować", r.Error);
    }

    private string MapTitle => _session.Map.Title is { Length: > 0 } t ? t : _session.DisplayName;

    /// <summary>Okno ustawień: papier, orientacja, dopasowanie albo podział na strony, marginesy, nagłówek, tło, jakość.</summary>
    private async Task<PrintOptions?> ShowPrintOptionsAsync(bool forPrinter)
    {
        var o = _settings.Print.Clone();
        var content = Map.ExportBounds!.Value.Inflate(4);

        // Pola po dwa w rzędzie (stała szerokość 140 + odstęp 12 mieści się w lewej kolumnie 300 px).
        var paper = new ComboBox { Header = "Papier", Width = 140 };
        foreach (var p in Enum.GetValues<PaperSize>()) paper.Items.Add(PrintLayout.PaperName(p));
        paper.SelectedIndex = (int)o.Paper;

        var orientation = new ComboBox { Header = "Orientacja", Width = 140 };
        foreach (var t in new[] { "Automatyczna", "Pionowa", "Pozioma" }) orientation.Items.Add(t);
        orientation.SelectedIndex = (int)o.Orientation;

        var scaling = new RadioButtons { Header = "Rozmiar mapy" };
        scaling.Items.Add("Dopasuj całą mapę do jednej strony");
        scaling.Items.Add("Podziel na kilka stron w skali:");
        scaling.SelectedIndex = (int)o.Scaling;
        var scale = new NumberBox
        {
            Value = o.ScalePercent, Minimum = 10, Maximum = 400, SmallChange = 10, LargeChange = 25,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 110, Margin = new Thickness(28, 0, 0, 0),
        };
        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { scale, new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center } } };

        var margin = new NumberBox
        {
            Header = "Marginesy (mm)", Value = o.MarginMm, Minimum = 0, Maximum = 40, SmallChange = 1, LargeChange = 5,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 140,
        };
        var dpi = new ComboBox { Header = "Jakość", Width = 140 };
        foreach (var d in new[] { 150, 200, 300, 600 }) dpi.Items.Add($"{d} dpi");
        dpi.SelectedIndex = Math.Max(0, Array.IndexOf([150, 200, 300, 600], o.Dpi));
        var header = new CheckBox { Content = "Tytuł mapy u góry strony", IsChecked = o.Header };
        var white = new CheckBox { Content = "Białe tło (zalecane — oszczędza tusz)", IsChecked = o.WhiteBackground };
        // PDF pliku z kilkoma arkuszami: wszystkie naraz, każdy od nowej strony (druk — tylko bieżący arkusz)
        var allSheets = new CheckBox
        {
            Content = $"Wszystkie arkusze ({_document.Sheets.Count}) — każdy od nowej strony, z nazwą w nagłówku",
            IsChecked = _pdfAllSheets,
            Visibility = !forPrinter && _document.Sheets.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(allSheets, "PdfAllSheets");

        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12.5 };
        var preview = new Image { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Width = 230, Height = 300 };
        var previewFrame = new Border
        {
            Child = preview,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(6),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Top,
        };

        var left = new StackPanel { Spacing = 12, Width = 300 };
        if (!forPrinter) left.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { paper, dpi } });
        left.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { orientation, margin } });
        left.Children.Add(scaling);
        left.Children.Add(scaleRow);
        left.Children.Add(header);
        left.Children.Add(white);
        left.Children.Add(allSheets);
        if (forPrinter)
        {
            left.Children.Add(new TextBlock
            {
                Text = "Rozmiar papieru i drukarkę wybierzesz w następnym oknie.", TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12,
            });
        }
        left.Children.Add(info);
        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            Children = { left, new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Podgląd 1. strony", FontSize = 12, Opacity = 0.7 }, previewFrame } } },
        };

        void Read()
        {
            o.Paper = (PaperSize)Math.Max(0, paper.SelectedIndex);
            o.Orientation = (PageOrientation)Math.Max(0, orientation.SelectedIndex);
            o.Scaling = (PrintScaling)Math.Max(0, scaling.SelectedIndex);
            if (!double.IsNaN(scale.Value)) o.ScalePercent = scale.Value;
            if (!double.IsNaN(margin.Value)) o.MarginMm = margin.Value;
            o.Dpi = new[] { 150, 200, 300, 600 }[Math.Max(0, dpi.SelectedIndex)];
            o.Header = header.IsChecked == true;
            o.WhiteBackground = white.IsChecked == true;
            scale.IsEnabled = o.Scaling == PrintScaling.Tiles;
        }

        int version = 0;
        async void Update()
        {
            Read();
            int my = ++version;
            var (pw, ph) = PrintLayout.PaperMm(forPrinter ? PaperSize.A4 : o.Paper);
            var plan = PrintLayout.Plan(content, pw, ph, o);
            string paperName = forPrinter ? "A4 (przykładowo)" : PrintLayout.PaperName(o.Paper);
            string orient = plan.Landscape ? "pozioma" : "pionowa";
            info.Text = plan.Pages.Count == 1
                ? $"1 strona {paperName}, orientacja {orient}, skala {plan.ScalePercent:0}%."
                : $"{plan.Pages.Count} {Plural(plan.Pages.Count, "strona", "strony", "stron")} {paperName} ({plan.Rows} × {plan.Columns}), orientacja {orient}, skala {plan.ScalePercent:0}% — po wydruku można je skleić w plakat.";
            await Task.Delay(120); // przy szybkim klikaniu rysujemy tylko ostatni podgląd
            if (my != version) return;
            try
            {
                using var scene = await Map.CreatePrintSceneAsync(o);
                var (bgra, w, h) = MapCanvas.RenderPage(scene, plan.Pages[0], plan, o, MapTitle, 1, plan.Pages.Count, 40);
                if (my != version) return;
                using var bmp = CanvasBitmap.CreateFromBytes(MapScene.SharedDevice, bgra, w, h, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
                using var ms = new InMemoryRandomAccessStream();
                await bmp.SaveAsync(ms, CanvasBitmapFileFormat.Png);
                ms.Seek(0);
                var image = new BitmapImage();
                await image.SetSourceAsync(ms);
                if (my == version) preview.Source = image;
            }
            catch
            {
                // podgląd jest tylko pomocą — błąd nie blokuje druku
            }
        }

        paper.SelectionChanged += (_, _) => Update();
        dpi.SelectionChanged += (_, _) => Read();
        orientation.SelectionChanged += (_, _) => Update();
        scaling.SelectionChanged += (_, _) => Update();
        scale.ValueChanged += (_, _) => Update();
        margin.ValueChanged += (_, _) => Update();
        header.Click += (_, _) => Update();
        white.Click += (_, _) => Update();
        Update();

        var dlg = new ContentDialog
        {
            Title = forPrinter ? "Drukowanie mapy" : "Eksport do PDF",
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = forPrinter ? "Drukuj…" : "Zapisz PDF…",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 720.0;
        if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return null;
        Read();
        if (!forPrinter && _document.Sheets.Count > 1) _pdfAllSheets = allSheets.IsChecked == true;
        _settings.Print = o.Clone();
        _settings.Save();
        return o;
    }

    // ================================================================== linki między mapami

    private readonly Stack<string> _backStack = new();

    /// <summary>Otwiera link kafelka: stronę w przeglądarce, inną mapę (z możliwością powrotu) albo plik w jego programie.</summary>
    private async Task OpenLinkAsync(Node node)
    {
        var target = LinkTargets.Resolve(node.Link, _session.FilePath);
        try
        {
            switch (target.Kind)
            {
                case LinkKind.Web:
                    Process.Start(new ProcessStartInfo(target.Target) { UseShellExecute = true });
                    break;
                case LinkKind.Node:
                    RevealNode(target.NodeId!);
                    break;
                case LinkKind.Map when target.NodeId is { } same && _session.FilePath is { } cur && LinkTargets.SamePath(cur, target.Target):
                    RevealNode(same);
                    break;
                case LinkKind.Map when File.Exists(target.Target):
                    await OpenLinkedMapAsync(target.Target, target.NodeId);
                    break;
                case LinkKind.File when File.Exists(target.Target) || Directory.Exists(target.Target):
                    Process.Start(new ProcessStartInfo(target.Target) { UseShellExecute = true });
                    break;
                case LinkKind.Map or LinkKind.File:
                    ShowToast($"Nie znaleziono pliku: {target.Target}", InfoBarSeverity.Warning);
                    break;
                default:
                    ShowToast("Ten kafelek nie ma poprawnego linku.", InfoBarSeverity.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się otworzyć linku: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>Otwiera inną mapę kampanii (z możliwością powrotu) i ewentualnie pokazuje w niej kafelek.</summary>
    private async Task OpenLinkedMapAsync(string path, string? nodeId)
    {
        var from = _session.FilePath;
        if (!await ConfirmDiscardAsync()) return;
        if (!await OpenFileAsync(path, confirm: false)) return;
        if (from is not null)
        {
            _backStack.Push(from);
            UpdateBackButton();
        }
        if (nodeId is not null) RevealNode(nodeId);
        ShowToast($"Otwarto mapę „{Path.GetFileNameWithoutExtension(path)}”" +
                  (from is null ? "." : " — Alt+← albo strzałka w lewo na pasku wraca do poprzedniej."), InfoBarSeverity.Informational);
    }

    /// <summary>Pokazuje kafelek: rozwija zwinięte gałęzie, zaznacza go i płynnie przesuwa do niego widok.</summary>
    private void RevealNode(string id)
    {
        // kafelek może być na innym arkuszu tego pliku — wtedy najpierw przełączamy zakładkę
        if (_session.Index.Get(id) is null && _document.SheetOfNode(id) is var sheet and >= 0) ShowSheet(sheet);
        if (_session.Index.Get(id) is not { } n)
        {
            ShowToast("Kafelka z linku nie ma już w tej mapie.", InfoBarSeverity.Warning);
            return;
        }
        var collapsed = MapSearch.CollapsedAncestors(_session.Index, n);
        if (collapsed.Count > 0) _session.Edit(_ => collapsed.ForEach(c => c.Collapsed = false));
        _session.SelectNode(id);
        Map.CenterOn(id);
    }

    // ------------------------------------------------------------------ linki zwrotne

    private List<Backlink> _backlinks = [];
    private string? _backlinksFor;
    private CancellationTokenSource? _backlinksCts;

    /// <summary>W tle: które mapy z tego folderu prowadzą do bieżącej (pokazywane w panelu mapy).</summary>
    private async void RefreshBacklinks()
    {
        _backlinksCts?.Cancel();
        var path = _session.FilePath;
        if (_backlinks.Count > 0)
        {
            // panel mapy pokazywał linki zwrotne poprzedniej mapy
            _backlinks = [];
            DropPanel("0000");
            if (_session.SelectionCount == 0) RebuildProperties();
        }
        _backlinksFor = null;
        if (path is null || Headless) return;
        var cts = new CancellationTokenSource();
        _backlinksCts = cts;
        try
        {
            var found = await Task.Run(() => Backlinks.Find(path, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || _session.FilePath != path) return;
            _backlinks = found;
            _backlinksFor = path;
            if (found.Count > 0)
            {
                DropPanel("0000"); // panel mapy pokazuje linki zwrotne — zbuduje się od nowa
                if (_session.SelectionCount == 0) RebuildProperties();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.LogError("Linki zwrotne", ex);
        }
    }

    private async Task OpenBacklinkAsync(Backlink b) => await OpenLinkedMapAsync(b.MapPath, b.NodeId);

    private async Task GoBackAsync()
    {
        if (_backStack.Count == 0) return;
        if (!await ConfirmDiscardAsync()) return;
        var path = _backStack.Pop();
        UpdateBackButton();
        await OpenFileAsync(path, confirm: false);
    }

    private void UpdateBackButton()
    {
        BackButton.Visibility = _backStack.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_backStack.Count > 0) ToolTipService.SetToolTip(BackButton, $"Wróć do „{Path.GetFileNameWithoutExtension(_backStack.Peek())}” (Alt+←)");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _ = GoBackAsync();
    private void ExportPdf_Click(object sender, RoutedEventArgs e) => _ = ExportPdfAsync();
    private void Print_Click(object sender, RoutedEventArgs e) => _ = PrintAsync();

    /// <summary>Wybór mapy do linku kafelka — zapisywana ścieżka względna wobec bieżącej mapy.</summary>
    private async Task PickLinkTargetAsync()
    {
        if (await PickOpenAsync(MindMap.FileExtension, ".xmind", ".pdf", ".docx", ".txt", ".png", ".jpg", "*") is not { } path) return;
        var stored = LinkTargets.ToStored(path, _session.FilePath);
        EditNodes(n => n.Link = stored);
        if (_session.FilePath is null)
            ShowToast("Mapa nie jest jeszcze zapisana, więc link ma pełną ścieżkę. Po zapisaniu możesz wybrać plik ponownie, żeby był względny.", InfoBarSeverity.Informational);
    }
}
