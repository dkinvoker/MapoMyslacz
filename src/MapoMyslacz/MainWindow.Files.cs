using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Import;
using MapoMyslacz.Core.Model;
using MapoMyslacz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace MapoMyslacz;

/// <summary>Pliki: nowa mapa, otwieranie, zapis, import z XMind, eksport PNG.</summary>
public sealed partial class MainWindow
{
    // ================================================================== dialogi — pomocnicze

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try
        {
            dialog.XamlRoot = RootHost.XamlRoot;
            dialog.RequestedTheme = RootHost.ActualTheme;
            if (Application.Current.Resources.TryGetValue("DefaultContentDialogStyle", out var style) && style is Style s)
                dialog.Style = s;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private async Task ShowErrorAsync(string title, string message) =>
        await ShowDialogAsync(new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            CloseButtonText = "OK",
        });

    private async Task<bool> ConfirmDiscardAsync()
    {
        CommitAllEdits();
        if (!_document.IsDirty) return true;
        var dlg = new ContentDialog
        {
            Title = "Niezapisane zmiany",
            Content = $"Zapisać zmiany w mapie „{_document.DisplayName}”?",
            PrimaryButtonText = "Zapisz",
            SecondaryButtonText = "Nie zapisuj",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await ShowDialogAsync(dlg);
        return r switch
        {
            ContentDialogResult.Primary => await SaveAsync(saveAs: false),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    // ================================================================== wybór plików

    private async Task<string?> PickOpenAsync(params string[] extensions)
    {
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };
            foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
            var result = await picker.PickSingleFileAsync();
            return result?.Path;
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się otworzyć okna wyboru pliku: {ex.Message}", InfoBarSeverity.Error);
            return null;
        }
    }

    private async Task<string?> PickSaveAsync(string suggestedName, string typeName, string extension)
    {
        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedName,
            };
            picker.FileTypeChoices.Add(typeName, new List<string> { extension });
            var result = await picker.PickSaveFileAsync();
            if (result?.Path is not { } path) return null;
            return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? path : path + extension;
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się otworzyć okna zapisu: {ex.Message}", InfoBarSeverity.Error);
            return null;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return s.Length == 0 ? "Mapa" : s.Length > 80 ? s[..80] : s;
    }

    // ================================================================== polecenia

    private async Task NewAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        LoadDocument(NewDocument());
        ClearBackStack();
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (await PickOpenAsync(MindMap.FileExtension, ".xmind", ".json") is { } path && await OpenFileAsync(path, confirm: false))
            ClearBackStack();
    }

    /// <summary>„Wstecz” dotyczy tylko przechodzenia linkami — zwykłe otwarcie innej mapy zaczyna od nowa.</summary>
    private void ClearBackStack()
    {
        _backStack.Clear();
        UpdateBackButton();
    }

    /// <summary>Otwiera mapę .mapo albo importuje plik XMind. Zwraca true, gdy mapa została otwarta.</summary>
    public async Task<bool> OpenFileAsync(string path, bool confirm = true)
    {
        if (confirm && !await ConfirmDiscardAsync()) return false;
        try
        {
            if (path.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase)) return await ImportXmindFileAsync(path);
            if (IsOutlineFile(path)) return ImportOutlineFile(path);
            LoadDocument(MapDocument.Open(path));
            _settings.AddRecent(path);
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się otworzyć mapy", $"{Path.GetFileName(path)}\n\n{ex.Message}");
            return false;
        }
    }

    internal static bool IsOutlineFile(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    private async Task ImportOutlineAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (await PickOpenAsync(".md", ".txt", ".markdown") is { } path) await OpenFileAsync(path, confirm: false);
    }

    /// <summary>
    /// Konspekt (Markdown albo tekst z wcięciami) jako nowa, jeszcze niezapisana mapa. Konspekt z kilkoma nagłówkami
    /// „#” (np. eksport wszystkich arkuszy) — arkusz na każdy nagłówek.
    /// </summary>
    private bool ImportOutlineFile(string path)
    {
        var maps = MarkdownOutline.ParseSheets(File.ReadAllText(path, System.Text.Encoding.UTF8));
        int count = maps.Sum(m => m.Nodes.Count);
        if (count == 0) throw new InvalidDataException("Plik nie zawiera żadnych punktów konspektu.");
        if (maps.Count == 1) maps[0].Title ??= Path.GetFileNameWithoutExtension(path);
        var doc = MapDocument.FromMaps(maps);
        doc.MarkDirty();
        LoadDocument(doc);
        ClearBackStack();
        var sheets = maps.Count > 1 ? $", {maps.Count} arkusze" : "";
        ShowToast($"Zaimportowano konspekt ({count} {Plural(count, "kafelek", "kafelki", "kafelków")}{sheets}) — zapisz go (Ctrl+S) jako mapę .mapo.", InfoBarSeverity.Informational);
        return true;
    }

    private async Task ExportMarkdownAsync()
    {
        CommitAllEdits();
        var path = await PickSaveAsync(SafeFileName(_document.Sheets[0].Map.Title ?? _document.DisplayName), "Konspekt Markdown", ".md");
        if (path is null) return;
        try
        {
            // wszystkie arkusze: każdy pod nagłówkiem „#” ze swoją nazwą (import odtworzy z nich arkusze)
            var sheets = _document.Sheets.Select((s, i) => (_document.SheetName(i), s.Map)).ToList();
            File.WriteAllText(path, MarkdownOutline.WriteSheets(sheets), new System.Text.UTF8Encoding(false));
            var all = sheets.Count > 1 ? $" (wszystkie arkusze: {sheets.Count})" : "";
            ShowToast($"Zapisano konspekt: {Path.GetFileName(path)}{all}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się zapisać konspektu", ex.Message);
        }
    }

    private void ImportOutline_Click(object sender, RoutedEventArgs e) => _ = ImportOutlineAsync();
    private void ExportMarkdown_Click(object sender, RoutedEventArgs e) => _ = ExportMarkdownAsync();

    private async Task ImportXmindAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (await PickOpenAsync(".xmind") is { } path) await OpenFileAsync(path, confirm: false);
    }

    /// <summary>Wszystkie arkusze pliku XMind jako zakładki jednej mapy.</summary>
    private static MapDocument ImportXmindDocument(string path)
    {
        var maps = XmindImporter.Import(path);
        if (maps.Count == 1) maps[0].Title ??= Path.GetFileNameWithoutExtension(path);
        return MapDocument.FromMaps(maps);
    }

    private Task<bool> ImportXmindFileAsync(string path)
    {
        var doc = ImportXmindDocument(path);
        doc.MarkDirty();
        LoadDocument(doc);
        ShowToast(doc.Sheets.Count > 1
            ? $"Zaimportowano mapę z XMind ({doc.Sheets.Count} arkusze — zakładki na dole) — zapisz ją (Ctrl+S) jako plik .mapo."
            : "Zaimportowano mapę z XMind — zapisz ją (Ctrl+S) jako plik .mapo.", InfoBarSeverity.Informational);
        return Task.FromResult(true);
    }

    /// <summary>Zapis (albo „Zapisz jako”). Zwraca false, gdy użytkownik zrezygnował albo wystąpił błąd.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        CommitAllEdits();
        var path = _document.FilePath;
        if (saveAs || path is null)
        {
            path = await PickSaveAsync(SafeFileName(_document.Sheets[0].Map.Title ?? _document.DisplayName), "Mapa myśli MapoMyślacza", MindMap.FileExtension);
            if (path is null) return false;
        }
        try
        {
            bool moved = _document.FilePath != path;
            BackupBeforeSave(path); // poprzednia wersja pliku — do historii wersji
            _document.Save(path, ViewOf);
            _settings.AddRecent(path);
            UpdateTitle();
            if (moved) RefreshBacklinks();
            ShowToast($"Zapisano: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się zapisać mapy", $"{path}\n\n{ex.Message}");
            return false;
        }
    }

    private async Task ExportPngAsync()
    {
        CommitAllEdits();
        var path = await PickSaveAsync(SafeFileName(_session.DisplayName), "Obraz PNG", ".png");
        if (path is null) return;
        try
        {
            await Map.ExportPngAsync(path);
            ShowToast($"Zapisano obraz: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się zapisać obrazu", ex.Message);
        }
    }

    /// <summary>
    /// Tryb wiersza poleceń: MapoMyslacz.exe mapa.mapo --export-png obraz.png [--scale 2] albo --export-pdf mapa.pdf.
    /// Okno jest wtedy poza ekranem; po zapisie program się kończy (kod 0 = sukces).
    /// </summary>
    public async Task<int> RunCliExportAsync(string input, string output, double scale, bool pdf = false)
    {
        try
        {
            await WhenLoaded;
            if (input.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase))
            {
                LoadDocument(ImportXmindDocument(input));
            }
            else
            {
                LoadDocument(MapDocument.Open(input));
            }
            await Task.Delay(150);
            // PDF pliku z arkuszami — wszystkie arkusze (każdy od nowej strony); PNG — pierwszy arkusz
            if (pdf && _document.Sheets.Count > 1)
                await Map.ExportPdfAsync(Path.GetFullPath(output), _settings.Print.Clone(),
                    _document.Sheets.Select((s, i) => (s.Map, s.Index, _document.SheetName(i))).ToList(), AppInfo.FullName);
            else if (pdf) await Map.ExportPdfAsync(Path.GetFullPath(output), _settings.Print.Clone(), MapTitle, AppInfo.FullName);
            else await Map.ExportPngAsync(Path.GetFullPath(output), scale);
            return 0;
        }
        catch (Exception ex)
        {
            App.LogError($"Eksport {(pdf ? "PDF" : "PNG")}: {input} → {output}", ex);
            return 1;
        }
    }
}
