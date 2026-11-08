using MapoMyslacz.Core.Updates;
using MapoMyslacz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapoMyslacz;

/// <summary>Okno: powiadomienie o nowej wersji, pobieranie, podmiana i ponowne uruchomienie.</summary>
public sealed partial class MainWindow
{
    private ReleaseInfo? _update;
    private bool _updating, _updateInstalled, _updateFailed;

    /// <summary>Automatyczne sprawdzenie kilka sekund po starcie — najwyżej raz na ~20 godzin.</summary>
    private void SetupUpdates()
    {
        var s = _settings;
        AutoUpdateCheck.IsChecked = s.CheckForUpdates;
        if (!s.CheckForUpdates || s.LastUpdateCheck is { } last && DateTime.UtcNow - last < TimeSpan.FromHours(20)) return;
        DispatcherQueue.TryEnqueue(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            await CheckForUpdatesAsync(manual: false);
        });
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        var s = _settings;
        if (manual) ShowToast("Sprawdzam aktualizacje…", InfoBarSeverity.Informational);
        try
        {
            var release = await Updater.CheckAsync();
            s.LastUpdateCheck = DateTime.UtcNow;
            s.Save();
            if (release is null)
            {
                if (manual) ShowToast($"Masz najnowszą wersję ({AppInfo.Version}).");
                return;
            }
            if (!manual && s.SkippedVersion == release.Tag) return; // „pomiń tę wersję” — nie przypominamy
            if (manual) Toast.IsOpen = false;
            ShowUpdateBar(release);
        }
        catch
        {
            // bez internetu, GitHub niedostępny… — przy automatycznym sprawdzaniu po cichu
            if (manual) ShowToast("Nie udało się sprawdzić (brak połączenia z GitHubem?).", InfoBarSeverity.Warning);
        }
    }

    private bool CanInstall(ReleaseInfo r) => !_updateFailed && r.CanInstall && Updater.CanSelfUpdate;

    private void ShowUpdateBar(ReleaseInfo r)
    {
        _update = r;
        _updateInstalled = _updateFailed = false;
        UpdateBar.Severity = InfoBarSeverity.Informational;
        UpdateBar.Title = $"Nowa wersja {r.DisplayVersion}";
        UpdateBar.Message = CanInstall(r)
            ? "Pobiorę ją z GitHuba, sprawdzę i podmienię program — potem wystarczy uruchomić go ponownie."
            : "Pobierzesz ją ze strony wydania na GitHubie.";
        UpdateActionButton.Content = CanInstall(r) ? "Zaktualizuj" : "Otwórz stronę";
        UpdateActionButton.IsEnabled = true;
        UpdateSkipLink.Visibility = Visibility.Visible;
        UpdateNotesLink.Visibility = string.IsNullOrWhiteSpace(r.Notes) ? Visibility.Collapsed : Visibility.Visible;
        UpdateBar.IsOpen = true;
    }

    private async void UpdateAction_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r || _updating) return;
        if (_updateInstalled)
        {
            await RestartAfterUpdateAsync();
            return;
        }
        if (!CanInstall(r))
        {
            try { Updater.OpenReleasePage(r); } catch { }
            UpdateBar.IsOpen = false;
            return;
        }

        _updating = true;
        UpdateActionButton.IsEnabled = false;
        UpdateSkipLink.Visibility = Visibility.Collapsed;
        try
        {
            UpdateBar.Message = "Pobieram…";
            // postęp dociera do okna z opóźnieniem — po zakończeniu nie nadpisuje już końcowego komunikatu
            var progress = new Progress<double>(p => { if (_updating) UpdateBar.Message = $"Pobieram… {p * 100:0}%"; });
            await Updater.InstallAsync(r, progress);
            _updating = false;
            _updateInstalled = true;
            UpdateBar.Severity = InfoBarSeverity.Success;
            UpdateBar.Title = $"Wersja {r.DisplayVersion} jest gotowa";
            UpdateBar.Message = "Uruchom program ponownie, żeby z niej korzystać (albo po prostu przy następnym starcie).";
            UpdateActionButton.Content = "Uruchom ponownie";
        }
        catch (Exception ex)
        {
            _updating = false;
            _updateFailed = true;
            UpdateBar.Severity = InfoBarSeverity.Error;
            UpdateBar.Title = "Aktualizacja nie powiodła się";
            UpdateBar.Message = $"{ex.Message} Program został bez zmian — nową wersję pobierzesz ze strony wydania.";
            UpdateActionButton.Content = "Otwórz stronę";
        }
        finally
        {
            _updating = false;
            UpdateActionButton.IsEnabled = true;
        }
    }

    /// <summary>Ponowne uruchomienie po aktualizacji: najpierw zapis (albo odrzucenie) zmian, potem nowy starter.</summary>
    private async Task RestartAfterUpdateAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            Updater.Restart(_session.FilePath);
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się uruchomić programu ponownie: {ex.Message}", InfoBarSeverity.Error);
            return;
        }
        _forceClose = true;
        Close();
    }

    private async void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r) return;
        var dlg = new ContentDialog
        {
            Title = $"Co nowego w wersji {r.DisplayVersion}",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = r.Notes.Trim(), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, MaxWidth = 520 },
            },
            CloseButtonText = "Zamknij",
        };
        await ShowDialogAsync(dlg);
    }

    private void UpdateSkip_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not { } r) return;
        _settings.SkippedVersion = r.Tag;
        _settings.Save();
        UpdateBar.IsOpen = false;
    }

    private void AutoUpdate_Click(object sender, RoutedEventArgs e)
    {
        _settings.CheckForUpdates = AutoUpdateCheck.IsChecked == true;
        _settings.Save();
    }

    private async void CheckUpdateNow_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);
}
