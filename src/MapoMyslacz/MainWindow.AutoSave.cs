using System.Diagnostics;
using MapoMyslacz.Core.Editing;
using MapoMyslacz.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MapoMyslacz;

/// <summary>
/// Autozapis: co minutę niezapisane zmiany trafiają do %LOCALAPPDATA%\MapoMyslacz\autosave. Po zapisaniu mapy
/// albo poprawnym zamknięciu kopia znika; po awarii program przy następnym starcie proponuje jej przywrócenie.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string AutoSaveFolder = Path.Combine(AppInfo.DataFolder, "autosave");
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(60);

    private readonly AutoSaveStore _autoSave = new(AutoSaveFolder);
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _autoSaveTimer;
    private long _autoSavedVersion = -1;

    /// <summary>Okno do eksportu z linii poleceń — bez autozapisu, odzyskiwania i aktualizacji.</summary>
    internal bool Headless { get; set; }

    private static readonly long ProcessStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;

    private static bool IsProcessAlive(int pid, long startTicks)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            // ten sam numer może dostać inny program — porównujemy też czas startu (z tolerancją 2 s)
            return Math.Abs(p.StartTime.ToUniversalTime().Ticks - startTicks) < TimeSpan.TicksPerSecond * 2;
        }
        catch
        {
            return false;
        }
    }

    private void StartAutoSave()
    {
        if (Headless || _autoSaveTimer is not null) return;
        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = AutoSaveInterval;
        _autoSaveTimer.Tick += (_, _) => AutoSaveNow();
        _autoSaveTimer.Start();
        Closed += (_, _) =>
        {
            _autoSaveTimer.Stop();
            _autoSave.Delete(); // okno zamyka się dopiero po zapisaniu albo świadomym odrzuceniu zmian
        };
    }

    /// <summary>Zapisuje kopię, jeśli od ostatniej coś się zmieniło; po zapisaniu mapy usuwa kopię.</summary>
    private void AutoSaveNow()
    {
        if (Headless) return;
        try
        {
            if (_session.IsDirty || _session.IsBatching)
            {
                // w trakcie pisania tekstu kafelka zmiana nie jest jeszcze zatwierdzona — zapisujemy i tak
                if (_session.Version == _autoSavedVersion && !_session.IsBatching) return;
                _autoSave.Write(_session, Map.CurrentView, Environment.ProcessId, ProcessStartTicks);
                _autoSavedVersion = _session.Version;
            }
            else if (_autoSave.Exists)
            {
                _autoSave.Delete();
                _autoSavedVersion = -1;
            }
        }
        catch (Exception ex)
        {
            App.LogError("Autozapis", ex);
        }
    }

    /// <summary>Nowa mapa w oknie: poprzednia została zapisana albo świadomie odrzucona — jej kopia jest zbędna.</summary>
    private void ResetAutoSave()
    {
        _autoSave.Delete();
        _autoSavedVersion = -1;
    }

    /// <summary>Po starcie: osierocone autozapisy (awaria) — propozycja przywrócenia najnowszego.</summary>
    private async Task OfferRecoveryAsync()
    {
        if (Headless) return;
        List<AutoSaveInfo> orphans;
        try
        {
            AutoSaveStore.Prune(AutoSaveFolder, TimeSpan.FromDays(30), IsProcessAlive);
            orphans = AutoSaveStore.Orphans(AutoSaveFolder, IsProcessAlive);
        }
        catch (Exception ex)
        {
            App.LogError("Odzyskiwanie autozapisu", ex);
            return;
        }
        if (orphans.Count == 0) return;
        var info = orphans[0];

        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "MapoMyślacz nie został poprawnie zamknięty. Znaleziono niezapisane zmiany mapy " });
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"„{info.DisplayName}”", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $" z {info.SavedAt:d MMMM, HH:mm}." });
        var panel = new StackPanel { Spacing = 10, Children = { text } };
        panel.Children.Add(new TextBlock
        {
            Text = info.OriginalPath is { } p
                ? $"Przywrócona mapa otworzy się jako niezapisana — Ctrl+S zapisze ją w miejscu oryginału:\n{p}"
                : "Mapa nie była jeszcze zapisana — po przywróceniu zapisz ją (Ctrl+S).",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Opacity = 0.75, IsTextSelectionEnabled = true,
        });
        if (orphans.Count > 1)
            panel.Children.Add(new TextBlock { Text = $"Starszych kopii: {orphans.Count - 1} — program zapyta o nie przy następnym uruchomieniu.", FontSize = 12.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });

        var dlg = new ContentDialog
        {
            Title = "Odzyskiwanie mapy",
            Content = panel,
            PrimaryButtonText = "Przywróć",
            SecondaryButtonText = "Odrzuć zmiany",
            CloseButtonText = "Później",
            DefaultButton = ContentDialogButton.Primary,
        };
        var r = await ShowDialogAsync(dlg);
        if (r == ContentDialogResult.Secondary)
        {
            AutoSaveStore.Delete(info);
            return;
        }
        if (r != ContentDialogResult.Primary) return;
        if (!await ConfirmDiscardAsync()) return;
        try
        {
            var map = AutoSaveStore.Load(info);
            var session = new MapSession(map, info.OriginalPath);
            session.MarkDirty();
            LoadSession(session);
            ClearBackStack();
            AutoSaveStore.Delete(info);
            AutoSaveNow(); // od razu nowa kopia w slocie tego okna
            ShowToast("Przywrócono niezapisane zmiany — zapisz mapę (Ctrl+S), żeby je zachować.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Nie udało się przywrócić mapy", $"{info.MapPath}\n\n{ex.Message}");
        }
    }
}
