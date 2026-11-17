using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MapoMyslacz;

/// <summary>Okno opcji (menu „?” → Opcje…, Ctrl+,) — zmiany działają od razu i zapisują się w ustawieniach.</summary>
public sealed partial class MainWindow
{
    private void ApplyChildrenAsArrows()
    {
        Map.ChildrenAsArrows = _settings.ChildrenAsArrows;
        var how = _settings.ChildrenAsArrows ? " — połączone strzałką (Opcje)" : "";
        ToolTipService.SetToolTip(AddChildButton, $"Dodaj dziecko (Tab){how}");
        ToolTipService.SetToolTip(AddSiblingButton, $"Dodaj rodzeństwo (Enter){how}");
    }

    private async Task ShowOptionsAsync()
    {
        CommitAllEdits();
        var panel = new StackPanel { Spacing = 6, MinWidth = 420, Padding = new Thickness(0, 0, 18, 0) }; // odstęp od paska przewijania
        TextBlock Title(string text) => new()
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0),
        };
        TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 };

        panel.Children.Add(new TextBlock { Text = "Nowe kafelki (Tab, Enter, kółko „+”)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var asBranch = new RadioButton { Content = "jako gałąź drzewa (dziecko kafelka)", GroupName = "children" };
        var asArrow = new RadioButton { Content = "połączone strzałką", GroupName = "children" };
        AutomationProperties.SetAutomationId(asArrow, "OptionChildrenAsArrows");
        asBranch.IsChecked = !_settings.ChildrenAsArrows;
        asArrow.IsChecked = _settings.ChildrenAsArrows;
        void SetArrows(bool on)
        {
            if (_settings.ChildrenAsArrows == on) return;
            _settings.ChildrenAsArrows = on;
            _settings.Save();
            ApplyChildrenAsArrows();
        }
        asBranch.Checked += (_, _) => SetArrows(false);
        asArrow.Checked += (_, _) => SetArrows(true);
        panel.Children.Add(asBranch);
        panel.Children.Add(asArrow);
        panel.Children.Add(Note("Gałąź: kafelek staje się dzieckiem — przesuwa się z rodzicem, zwija się z nim i układa przez „Uporządkuj”. " +
                                "Strzałka: kafelek jest swobodny, a połączenie to zwykła strzałka (grot, podpis, wyginanie, styl strzałek z motywu). " +
                                "Gałąź da się później zamienić na strzałkę: prawy przycisk na niej → „Zamień na strzałkę”."));

        panel.Children.Add(Title("Przeciąganie kafelków"));
        var neighbors = new CheckBox { Content = "Przyciągaj do krawędzi i środków sąsiednich kafelków (linie pomocnicze)", IsChecked = _settings.SnapToNeighbors };
        neighbors.Click += (_, _) =>
        {
            _settings.SnapToNeighbors = Map.SnapToNeighbors = neighbors.IsChecked == true;
            _settings.Save();
        };
        var snapGrid = new CheckBox { Content = "Przyciągaj do kropek siatki, gdy siatka jest widoczna", IsChecked = _settings.SnapToGrid };
        AutomationProperties.SetAutomationId(snapGrid, "OptionSnapToGrid");
        snapGrid.Click += (_, _) =>
        {
            _settings.SnapToGrid = Map.SnapToGrid = snapGrid.IsChecked == true;
            _settings.Save();
        };
        panel.Children.Add(neighbors);
        panel.Children.Add(snapGrid);
        panel.Children.Add(Note("Ctrl w trakcie przeciągania wyłącza przyciąganie. Upuszczenie kafelka w ramce dodaje go do niej, a na zakładce innego arkusza — przenosi go tam."));

        panel.Children.Add(Title("Nowe mapy i autozapis"));
        var theme = new ComboBox { Header = "Motyw nowych map", MinWidth = 240 };
        foreach (var t in Core.Model.MapThemes.All) theme.Items.Add(t.Name);
        theme.SelectedIndex = Math.Max(0, Core.Model.MapThemes.All.ToList().FindIndex(t => t.Id == (_settings.NewMapTheme ?? Core.Model.MapThemes.Default.Id)));
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedIndex < 0) return;
            var id = Core.Model.MapThemes.All[theme.SelectedIndex].Id;
            _settings.NewMapTheme = id == Core.Model.MapThemes.Default.Id ? null : id;
            _settings.Save();
        };
        int[] minutes = [1, 2, 5, 10];
        var autosave = new ComboBox { Header = "Autozapis niezapisanych zmian", MinWidth = 160 };
        foreach (var m in minutes) autosave.Items.Add(m == 1 ? "co minutę" : $"co {m} minut{(m < 5 ? "y" : "")}");
        autosave.SelectedIndex = Math.Max(0, Array.IndexOf(minutes, _settings.AutoSaveMinutes));
        autosave.SelectionChanged += (_, _) =>
        {
            if (autosave.SelectedIndex < 0) return;
            _settings.AutoSaveMinutes = minutes[autosave.SelectedIndex];
            _settings.Save();
            ApplyAutoSaveInterval();
        };
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { theme, autosave } });
        panel.Children.Add(Note("Autozapis chroni przed awarią (po niej program proponuje przywrócenie). Poprzednie wersje zapisanych map są w Plik → Historia wersji."));

        panel.Children.Add(Title("Widok"));
        var minimap = new CheckBox { Content = "Minimapa (gdy mapa nie mieści się w oknie)", IsChecked = _settings.ShowMinimap };
        minimap.Click += (_, _) =>
        {
            _settings.ShowMinimap = Map.ShowMinimap = minimap.IsChecked == true;
            _settings.Save();
        };
        var grid = new CheckBox { Content = "Siatka kropek na płótnie", IsChecked = _settings.ShowGrid };
        grid.Click += (_, _) =>
        {
            _settings.ShowGrid = Map.ShowGrid = grid.IsChecked == true;
            _settings.Save();
        };
        panel.Children.Add(minimap);
        panel.Children.Add(grid);

        panel.Children.Add(Title("Aktualizacje"));
        var updates = new CheckBox { Content = "Sprawdzaj przy starcie, czy jest nowa wersja (najwyżej raz dziennie)", IsChecked = _settings.CheckForUpdates };
        updates.Click += (_, _) =>
        {
            _settings.CheckForUpdates = updates.IsChecked == true;
            _settings.Save();
        };
        panel.Children.Add(updates);

        panel.Children.Add(Title("Dane programu"));
        panel.Children.Add(new TextBlock
        {
            Text = $"Ustawienia, własne style i autozapis: {Services.AppInfo.DataFolder}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
            IsTextSelectionEnabled = true,
        });

        var dlg = new ContentDialog
        {
            Title = "Opcje",
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            CloseButtonText = "Zamknij",
        };
        await ShowDialogAsync(dlg);
    }

    private void Options_Click(object sender, RoutedEventArgs e) => _ = ShowOptionsAsync();
}
