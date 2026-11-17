using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace MapoMyslacz;

/// <summary>Wyszukiwarka (Ctrl+F), fokus na gałęzi i przełączniki widoku (minimapa, siatka).</summary>
public sealed partial class MainWindow
{
    private List<string> _searchMatches = [];
    private int _searchIndex = -1;

    private bool SearchOpen => SearchHost.Visibility == Visibility.Visible;

    private void OpenSearch()
    {
        CommitAllEdits();
        SearchHost.Visibility = Visibility.Visible;
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
    /// Szuka od nowa. <paramref name="jump"/> — przejście do pierwszego wyniku (przy pisaniu); bez niego
    /// (np. po zmianie mapy) zostaje bieżący wynik, jeśli nadal pasuje.
    /// </summary>
    private void RunSearch(bool jump)
    {
        var keep = _searchIndex >= 0 && _searchIndex < _searchMatches.Count ? _searchMatches[_searchIndex] : null;
        _searchMatches = MapSearch.Find(_session.Map, SearchBox.Text).Select(n => n.Id).ToList();
        _searchIndex = jump ? (_searchMatches.Count > 0 ? 0 : -1) : keep is null ? -1 : _searchMatches.IndexOf(keep);
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

    /// <summary>Zaznacza bieżący wynik i płynnie przesuwa do niego widok (zwinięte gałęzie rozwija).</summary>
    private void GoToMatch()
    {
        var id = _searchMatches[_searchIndex];
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
        Map.SetSearchHits(_searchMatches, _searchIndex >= 0 ? _searchMatches[_searchIndex] : null);
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
