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
        Map.CommitEdit();
        if (!_session.IsDirty) return true;
        var dlg = new ContentDialog
        {
            Title = "Niezapisane zmiany",
            Content = $"Zapisać zmiany w mapie „{_session.DisplayName}”?",
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
        LoadSession(new MapSession());
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (await PickOpenAsync(MindMap.FileExtension, ".xmind", ".json") is { } path) await OpenFileAsync(path, confirm: false);
    }

    /// <summary>Otwiera mapę .mapo albo importuje plik XMind.</summary>
    public async Task OpenFileAsync(string path, bool confirm = true)
    {
        if (confirm && !await ConfirmDiscardAsync()) return;
        try
        {
            if (path.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase))
            {
                await ImportXmindFileAsync(path);
                return;
            }
            var session = MapSession.Open(path);
            LoadSession(session);
            _settings.AddRecent(path);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się otworzyć mapy", $"{Path.GetFileName(path)}\n\n{ex.Message}");
        }
    }

    private async Task ImportXmindAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        if (await PickOpenAsync(".xmind") is { } path) await OpenFileAsync(path, confirm: false);
    }

    private async Task ImportXmindFileAsync(string path)
    {
        var maps = XmindImporter.Import(path);
        var map = maps[0];
        if (maps.Count > 1)
        {
            // kilka arkuszy: wybór, który otworzyć
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 360 };
            for (int i = 0; i < maps.Count; i++) list.Items.Add($"{i + 1}. {maps[i].Title ?? "Arkusz"} ({maps[i].Nodes.Count} kafelków)");
            list.SelectedIndex = 0;
            var dlg = new ContentDialog
            {
                Title = "Plik XMind ma kilka arkuszy",
                Content = list,
                PrimaryButtonText = "Otwórz",
                CloseButtonText = "Anuluj",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return;
            map = maps[Math.Max(0, list.SelectedIndex)];
        }
        map.Title ??= Path.GetFileNameWithoutExtension(path);
        var session = new MapSession(map);
        session.MarkDirty();
        LoadSession(session);
        ShowToast("Zaimportowano mapę z XMind — zapisz ją (Ctrl+S) jako plik .mapo.", InfoBarSeverity.Informational);
    }

    /// <summary>Zapis (albo „Zapisz jako”). Zwraca false, gdy użytkownik zrezygnował albo wystąpił błąd.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        Map.CommitEdit();
        var path = _session.FilePath;
        if (saveAs || path is null)
        {
            path = await PickSaveAsync(SafeFileName(_session.Map.Title ?? _session.DisplayName), "Mapa myśli MapoMyślacza", MindMap.FileExtension);
            if (path is null) return false;
        }
        try
        {
            _session.SaveAs(path, Map.CurrentView);
            _settings.AddRecent(path);
            UpdateTitle();
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
        Map.CommitEdit();
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
    /// Tryb wiersza poleceń: MapoMyslacz.exe mapa.mapo --export-png obraz.png [--scale 2].
    /// Okno jest wtedy poza ekranem; po zapisie program się kończy (kod 0 = sukces).
    /// </summary>
    public async Task<int> RunCliExportAsync(string input, string output, double scale)
    {
        try
        {
            await WhenLoaded;
            if (input.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase))
            {
                var map = XmindImporter.Import(input)[0];
                LoadSession(new MapSession(map));
            }
            else
            {
                LoadSession(MapSession.Open(input));
            }
            await Task.Delay(150);
            await Map.ExportPngAsync(Path.GetFullPath(output), scale);
            return 0;
        }
        catch (Exception ex)
        {
            App.LogError($"Eksport PNG: {input} → {output}", ex);
            return 1;
        }
    }
}
