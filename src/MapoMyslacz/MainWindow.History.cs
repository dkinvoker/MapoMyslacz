using System.Diagnostics;
using System.Globalization;
using MapoMyslacz.Core.Editing;
using MapoMyslacz.Core.Model;
using MapoMyslacz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MapoMyslacz;

/// <summary>
/// Historia wersji (Plik → Historia wersji…): przed każdym zapisem poprzednia zawartość pliku trafia do folderu
/// danych programu; z okna historii można wrócić do dowolnej zachowanej wersji.
/// </summary>
public sealed partial class MainWindow
{
    private MapHistory? _history;
    private MapHistory History => _history ??= new MapHistory(Path.Combine(AppInfo.DataFolder, "history"));

    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>Kopia obecnej zawartości pliku przed zapisem — błąd kopii nie może zablokować zapisu.</summary>
    private void BackupBeforeSave(string path, bool force = false)
    {
        try
        {
            History.Backup(path, DateTime.Now, force);
        }
        catch (Exception ex)
        {
            App.LogError($"Historia wersji: {path}", ex);
        }
    }

    /// <summary>„dziś 14:32”, „wczoraj 18:05”, „wt. 12 listopada, 20:11”, „3 marca 2025, 09:40”.</summary>
    private static string WhenText(DateTime t)
    {
        var today = DateTime.Today;
        if (t.Date == today) return $"dziś {t:HH:mm}";
        if (t.Date == today.AddDays(-1)) return $"wczoraj {t:HH:mm}";
        if (t.Year == today.Year) return t.ToString("ddd d MMMM, HH:mm", Polish);
        return t.ToString("d MMMM yyyy, HH:mm", Polish);
    }

    private async Task ShowHistoryAsync()
    {
        CommitAllEdits();
        if (_document.FilePath is not { } path)
        {
            ShowToast("Historia wersji powstaje dla zapisanych map — zapisz mapę (Ctrl+S), a kolejne zapisy będą zachowywać poprzednie wersje.", InfoBarSeverity.Informational);
            return;
        }
        var versions = History.List(path);
        var panel = new StackPanel { Spacing = 8, MinWidth = 440 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Przy zapisie poprzednia wersja mapy „{Path.GetFileNameWithoutExtension(path)}” trafia tutaj (najwyżej co {MapHistory.MinInterval.TotalMinutes:0} minut). " +
                   $"Zostaje {MapHistory.KeepNewest} najnowszych, a starsze — po jednej z dnia przez {MapHistory.KeepDays} dni.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            FontSize = 12.5,
        });
        var list = new ListView { MaxHeight = 360, SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetAutomationId(list, "HistoryList");
        foreach (var v in versions)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new FontIcon { Glyph = "", FontSize = 14, Opacity = 0.7 });
            row.Children.Add(new TextBlock { Text = WhenText(v.Time), Width = 230 });
            row.Children.Add(new TextBlock { Text = $"{Math.Max(1, v.Size / 1024)} KB", Opacity = 0.6, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(new ListViewItem { Content = row, Tag = v });
        }
        if (versions.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Na razie nie ma zachowanych wersji — pojawią się przy kolejnych zapisach tej mapy.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8),
            });
        }
        else
        {
            panel.Children.Add(list);
            panel.Children.Add(new TextBlock
            {
                Text = "„Przywróć” wczytuje wybraną wersję jako niezapisaną mapę — Ctrl+S zapisze ją w miejscu obecnej. " +
                       "Obecna wersja pliku trafia przedtem do historii, więc i ją da się odzyskać.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7,
            });
            list.SelectedIndex = 0;
        }
        var dlg = new ContentDialog
        {
            Title = "Historia wersji",
            Content = panel,
            PrimaryButtonText = versions.Count > 0 ? "Przywróć" : "",
            SecondaryButtonText = "Pokaż folder",
            CloseButtonText = "Zamknij",
            DefaultButton = versions.Count > 0 ? ContentDialogButton.Primary : ContentDialogButton.Close,
        };
        var r = await ShowDialogAsync(dlg);
        if (r == ContentDialogResult.Secondary)
        {
            var folder = History.FolderFor(path);
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            return;
        }
        if (r != ContentDialogResult.Primary || (list.SelectedItem as ListViewItem)?.Tag is not MapHistory.Version chosen) return;
        await RestoreVersionAsync(path, chosen);
    }

    private async Task RestoreVersionAsync(string path, MapHistory.Version version)
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            var map = MapSerializer.Load(version.Path);
            BackupBeforeSave(path, force: true); // obecna wersja pliku — do historii, zanim cokolwiek ją nadpisze
            var doc = MapDocument.FromMap(map, path);
            doc.MarkDirty();
            LoadDocument(doc);
            ClearBackStack();
            ShowToast($"Wczytano wersję z {WhenText(version.Time)} — zapisz (Ctrl+S), żeby zastąpiła obecną. Obecna jest w historii.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się wczytać wersji", $"{version.Path}\n\n{ex.Message}");
        }
    }

    private void History_Click(object sender, RoutedEventArgs e) => _ = ShowHistoryAsync();
}
