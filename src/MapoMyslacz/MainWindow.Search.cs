using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MapoMyslacz;

/// <summary>Wyszukiwarka i zamiana (Ctrl+F, Ctrl+H), fokus na gałęzi i przełączniki widoku (minimapa, siatka).</summary>
public sealed partial class MainWindow
{
    /// <summary>Wyniki: arkusz (sesja) i kafelek — przy szukaniu we wszystkich arkuszach także z innych zakładek.</summary>
    private List<(Core.Editing.MapSession Session, string Id)> _searchMatches = [];
    private int _searchIndex = -1;

    private bool SearchOpen => SearchHost.Visibility == Visibility.Visible;

    /// <summary>Szukanie we wszystkich arkuszach (przełącznik widać tylko w pliku z kilkoma arkuszami).</summary>
    private bool SearchAllSheets => AllSheetsToggle.IsChecked == true && _document.Sheets.Count > 1;

    private void OpenSearch(bool replace = false)
    {
        CommitAllEdits();
        SearchHost.Visibility = Visibility.Visible;
        AllSheetsToggle.Visibility = _document.Sheets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (replace) ReplaceToggle.IsChecked = true;
        ReplaceRow.Visibility = ReplaceToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Focus(FocusState.Keyboard);
        SearchBox.SelectAll();
        if (SearchBox.Text.Length > 0) RunSearch(jump: false);
    }

    private void CloseSearch()
    {
        SearchHost.Visibility = Visibility.Collapsed;
        _searchMatches = [];
        _searchIndex = -1;
        Map.SetSearchHits([], null);
        Map.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Szuka od nowa. <paramref name="jump"/> — przejście do pierwszego wyniku (przy pisaniu; najpierw w bieżącym
    /// arkuszu); bez niego (np. po zmianie mapy) zostaje bieżący wynik, jeśli nadal pasuje.
    /// </summary>
    private void RunSearch(bool jump)
    {
        (Core.Editing.MapSession, string)? keep = _searchIndex >= 0 && _searchIndex < _searchMatches.Count ? _searchMatches[_searchIndex] : null;
        IEnumerable<Core.Editing.MapSession> sheets = SearchAllSheets ? _document.Sheets : [_session];
        _searchMatches = sheets.SelectMany(s => MapSearch.Find(s.Map, SearchBox.Text).Select(n => (s, n.Id))).ToList();
        if (jump)
        {
            int inCurrent = _searchMatches.FindIndex(m => ReferenceEquals(m.Session, _session));
            _searchIndex = inCurrent >= 0 ? inCurrent : _searchMatches.Count > 0 ? 0 : -1;
        }
        else _searchIndex = keep is null ? -1 : _searchMatches.IndexOf(keep.Value);
        UpdateSearchUi();
        if (jump && _searchIndex >= 0) GoToMatch();
    }

    private void StepSearch(int direction)
    {
        if (!SearchOpen)
        {
            OpenSearch();
            return;
        }
        if (_searchMatches.Count == 0) return;
        int n = _searchMatches.Count;
        _searchIndex = _searchIndex < 0 ? (direction > 0 ? 0 : n - 1) : ((_searchIndex + direction) % n + n) % n;
        GoToMatch();
    }

    /// <summary>
    /// Zaznacza bieżący wynik i płynnie przesuwa do niego widok (zwinięte gałęzie rozwija). Wynik z innego arkusza —
    /// najpierw przełącza zakładkę.
    /// </summary>
    private void GoToMatch()
    {
        var (session, id) = _searchMatches[_searchIndex];
        if (!ReferenceEquals(session, _session) && _document.Sheets.IndexOf(session) is var sheet and >= 0)
        {
            int keep = _searchIndex;
            ShowSheet(sheet); // przy zmianie arkusza wyniki liczą się od nowa (ten sam wynik zostaje bieżący)
            _searchIndex = keep < _searchMatches.Count && _searchMatches[keep] == (session, id) ? keep : _searchMatches.IndexOf((session, id));
            if (_searchIndex < 0) return;
        }
        if (_session.Index.Get(id) is not { } node)
        {
            RunSearch(jump: false);
            return;
        }
        var collapsed = MapSearch.CollapsedAncestors(_session.Index, node);
        if (collapsed.Count > 0) _session.Edit(_ => collapsed.ForEach(n => n.Collapsed = false));
        _session.SelectNode(id);
        Map.CenterOn(id);
        UpdateSearchUi();
    }

    private void UpdateSearchUi()
    {
        SearchCount.Text = _searchMatches.Count > 0
            ? $"{Math.Max(1, _searchIndex + 1)}/{_searchMatches.Count}"
            : SearchBox.Text.Trim().Length > 0 ? "brak" : "";
        // ile wyników jest na innych arkuszach — w podpowiedzi licznika
        int elsewhere = _searchMatches.Count(m => !ReferenceEquals(m.Session, _session));
        ToolTipService.SetToolTip(SearchCount, elsewhere > 0 ? $"W tym {elsewhere} na innych arkuszach" : null);
        var here = _searchMatches.Where(m => ReferenceEquals(m.Session, _session)).Select(m => m.Id).ToList();
        string? current = _searchIndex >= 0 && ReferenceEquals(_searchMatches[_searchIndex].Session, _session) ? _searchMatches[_searchIndex].Id : null;
        Map.SetSearchHits(here, current);
        ReplaceOneButton.IsEnabled = _searchIndex >= 0;
        ReplaceAllButton.IsEnabled = _searchMatches.Count > 0;
    }

    // ================================================================== zamiana

    /// <summary>
    /// Zamienia frazę w bieżącym wyniku (tekst i notatka) i przechodzi do następnego. Zamiana szuka całej frazy
    /// z pola wyszukiwania — bez wielkości liter i polskich znaków, tak jak wyszukiwarka.
    /// </summary>
    private void ReplaceOne()
    {
        if (_searchIndex < 0 || _searchIndex >= _searchMatches.Count) return;
        var (session, id) = _searchMatches[_searchIndex];
        int at = _searchIndex, n = 0;
        session.Edit(_ => { if (session.Index.Get(id) is { } node) n = MapSearch.Replace(node, SearchBox.Text, ReplaceBox.Text); });
        if (n == 0)
        {
            ShowToast($"W tym kafelku nie ma całej frazy „{SearchBox.Text.Trim()}” — zamiana szuka jej dosłownie (bez wielkości liter i polskich znaków).", InfoBarSeverity.Informational);
            StepSearch(1);
            return;
        }
        RunSearch(jump: false);
        // zamieniony kafelek zwykle wypada z wyników — następny wskakuje na jego miejsce
        if (_searchMatches.Count == 0) return;
        _searchIndex = _searchMatches.IndexOf((session, id)) is var same and >= 0 ? (same + 1) % _searchMatches.Count : Math.Min(at, _searchMatches.Count - 1);
        GoToMatch();
    }

    /// <summary>Zamiana we wszystkich wynikach — jeden krok cofania na każdy arkusz.</summary>
    private void ReplaceAll()
    {
        if (_searchMatches.Count == 0) return;
        CommitAllEdits();
        int total = 0, tiles = 0, sheets = 0;
        foreach (var group in _searchMatches.GroupBy(m => m.Session))
        {
            int before = total;
            group.Key.Edit(_ =>
            {
                foreach (var (_, id) in group)
                    if (group.Key.Index.Get(id) is { } node && MapSearch.Replace(node, SearchBox.Text, ReplaceBox.Text) is var k and > 0)
                    {
                        total += k;
                        tiles++;
                    }
            });
            if (total > before) sheets++;
        }
        RunSearch(jump: false);
        if (total == 0)
        {
            ShowToast($"Fraza „{SearchBox.Text.Trim()}” nie występuje dosłownie w żadnym wyniku — zamiana szuka całej frazy.", InfoBarSeverity.Informational);
            return;
        }
        var where = sheets > 1 ? $" na {sheets} arkuszach (Ctrl+Z cofa na każdym osobno)" : " (Ctrl+Z cofa)";
        ShowToast($"Zamieniono {total} {Plural(total, "wystąpienie", "wystąpienia", "wystąpień")} w {tiles} {Plural(tiles, "kafelku", "kafelkach", "kafelkach")}{where}.");
    }

    private void ReplaceToggle_Click(object sender, RoutedEventArgs e)
    {
        ReplaceRow.Visibility = ReplaceToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (ReplaceToggle.IsChecked == true) ReplaceBox.Focus(FocusState.Keyboard);
    }

    private void AllSheetsToggle_Click(object sender, RoutedEventArgs e) => RunSearch(jump: false);
    private void ReplaceOne_Click(object sender, RoutedEventArgs e) => ReplaceOne();
    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceAll();

    private void ReplaceBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (ctrl) ReplaceAll();
                else ReplaceOne();
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                CloseSearch();
                break;
        }
    }

    /// <summary>Mapa się zmieniła (edycja, cofnięcie, inna mapa) — wyniki liczymy od nowa.</summary>
    private void RefreshSearch()
    {
        if (SearchOpen) RunSearch(jump: false);
    }

    private void SearchBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e) => RunSearch(jump: true);

    private void SearchBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                StepSearch(shift ? -1 : 1);
                break;
            case VirtualKey.Escape:
                e.Handled = true;
                CloseSearch();
                break;
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => OpenSearch();
    private void SearchNext_Click(object sender, RoutedEventArgs e) => StepSearch(1);
    private void SearchPrev_Click(object sender, RoutedEventArgs e) => StepSearch(-1);
    private void SearchClose_Click(object sender, RoutedEventArgs e) => CloseSearch();

    // ================================================================== fokus na gałęzi

    private void OnFocusChanged()
    {
        if (Map.FocusedNodeId is { } id && _session.Index.Get(id) is { } n)
        {
            var text = n.Text.Replace('\n', ' ').Trim();
            if (text.Length > 48) text = text[..47] + "…";
            FocusText.Text = $"Gałąź „{text}” i jej powiązania · Esc — cała mapa";
            FocusHost.Visibility = Visibility.Visible;
        }
        else
        {
            FocusHost.Visibility = Visibility.Collapsed;
        }
    }

    private void Focus_Click(object sender, RoutedEventArgs e) => Map.ToggleFocus();
    private void FocusExit_Click(object sender, RoutedEventArgs e) => Map.ExitFocus();

    // ================================================================== przełączniki widoku

    private void ViewMenu_Opening(object sender, object e)
    {
        MinimapToggle.IsChecked = _settings.ShowMinimap;
        GridToggle.IsChecked = _settings.ShowGrid;
        FocusMenuItem.Text = Map.FocusedNodeId is null ? "Pokaż tylko zaznaczoną gałąź" : "Pokaż całą mapę (zakończ fokus)";
    }

    private void MinimapToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowMinimap = MinimapToggle.IsChecked;
        Map.ShowMinimap = _settings.ShowMinimap;
        _settings.Save();
    }

    private void GridToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowGrid = GridToggle.IsChecked;
        Map.ShowGrid = _settings.ShowGrid;
        _settings.Save();
    }
}
