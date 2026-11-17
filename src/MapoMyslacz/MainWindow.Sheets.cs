using MapoMyslacz.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace MapoMyslacz;

/// <summary>
/// Arkusze (zakładki na dole okna): kilka map w jednym pliku — np. osobny arkusz na każdą sesję gry.
/// Każdy arkusz ma własne zaznaczenie, historię cofania i zapamiętany widok.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<ToggleButton> _sheetButtons = [];

    private int ActiveSheet => _document.Sheets.IndexOf(_session);

    /// <summary>Przełącza na arkusz (widok bieżącego zostaje zapamiętany).</summary>
    private void ShowSheet(int index)
    {
        if (index < 0 || index >= _document.Sheets.Count) return;
        var target = _document.Sheets[index];
        if (ReferenceEquals(target, _session))
        {
            UpdateSheetTabLabels();
            return;
        }
        CommitAllEdits();
        if (_document.Sheets.Contains(_session)) _session.RememberView(Map.CurrentView);
        ActivateSession(target);
        UpdateSheetTabLabels();
    }

    /// <summary>Zakładki od nowa (zmieniła się liczba albo kolejność arkuszy).</summary>
    private void BuildSheetTabs()
    {
        SheetTabs.Children.Clear();
        _sheetButtons.Clear();
        for (int i = 0; i < _document.Sheets.Count; i++)
        {
            int index = i;
            var tab = new ToggleButton
            {
                Padding = new Thickness(12, 3, 12, 4),
                MinHeight = 28,
                FontSize = 12.5,
                CornerRadius = new CornerRadius(6),
            };
            tab.Click += (_, _) => ShowSheet(index);
            tab.DoubleTapped += (_, _) => _ = RenameSheetAsync(index);
            tab.ContextFlyout = SheetMenu(index);
            ToolTipService.SetToolTip(tab, "Klik — przejdź · dwuklik — zmień nazwę · prawy przycisk — więcej (Ctrl+PageUp/PageDown — poprzedni/następny)");
            _sheetButtons.Add(tab);
            SheetTabs.Children.Add(tab);
        }
        var add = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Padding = new Thickness(9, 5, 9, 5),
            MinHeight = 28,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(add, "Nowy arkusz (Ctrl+T) — np. osobna sesja kampanii w tym samym pliku");
        add.Click += (_, _) => AddSheet();
        SheetTabs.Children.Add(add);
        UpdateSheetTabLabels();
    }

    /// <summary>Nazwy i podświetlenie aktywnej zakładki (bez przebudowy).</summary>
    private void UpdateSheetTabLabels()
    {
        if (_document is null) return;
        int active = ActiveSheet;
        for (int i = 0; i < _sheetButtons.Count && i < _document.Sheets.Count; i++)
        {
            _sheetButtons[i].Content = _document.SheetName(i);
            _sheetButtons[i].IsChecked = i == active;
        }
        UpdateTitle();
    }

    private MenuFlyout SheetMenu(int index)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string text, string glyph, Action action, bool enabled = true)
        {
            var it = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            it.Click += (_, _) => action();
            return it;
        }
        menu.Items.Add(Item("Zmień nazwę…", "", () => _ = RenameSheetAsync(index)));
        menu.Items.Add(Item("Duplikuj arkusz", "", () => ShowSheet(_document.DuplicateSheet(index))));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Przesuń w lewo", "", () => MoveSheet(index, index - 1), index > 0));
        menu.Items.Add(Item("Przesuń w prawo", "", () => MoveSheet(index, index + 1), index < _document.Sheets.Count - 1));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Usuń arkusz…", "", () => _ = RemoveSheetAsync(index), _document.Sheets.Count > 1));
        return menu;
    }

    private void AddSheet()
    {
        int at = _document.AddSheet(Math.Max(0, ActiveSheet));
        ShowSheet(at);
        ShowToast("Nowy arkusz — dwuklik na zakładce zmienia jego nazwę.", InfoBarSeverity.Informational);
    }

    private void MoveSheet(int from, int to)
    {
        var active = _session;
        if (_document.MoveSheet(from, to)) ShowSheet(_document.Sheets.IndexOf(active));
    }

    private void StepSheet(int direction)
    {
        int n = _document.Sheets.Count;
        if (n > 1) ShowSheet(((ActiveSheet + direction) % n + n) % n);
    }

    /// <summary>Nazwa arkusza to tytuł jego mapy — zmiana jest krokiem cofania w tym arkuszu.</summary>
    private async Task RenameSheetAsync(int index)
    {
        if (index < 0 || index >= _document.Sheets.Count) return;
        var sheet = _document.Sheets[index];
        var box = new TextBox { Text = _document.SheetName(index), PlaceholderText = "np. „Sesja 3 — Las”" };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var dlg = new ContentDialog
        {
            Title = "Nazwa arkusza",
            Content = box,
            PrimaryButtonText = "Zmień",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return;
        var name = box.Text.Trim();
        sheet.Edit(m => m.Title = name.Length == 0 ? null : name);
        UpdateSheetTabLabels();
        if (ReferenceEquals(sheet, _session)) RefreshProperties();
    }

    private async Task RemoveSheetAsync(int index)
    {
        if (_document.Sheets.Count <= 1) return;
        var sheet = _document.Sheets[index];
        int nodes = sheet.Map.Nodes.Count;
        var dlg = new ContentDialog
        {
            Title = "Usunąć arkusz?",
            Content = new TextBlock
            {
                Text = $"Arkusz „{_document.SheetName(index)}” ({nodes} {Plural(nodes, "kafelek", "kafelki", "kafelków")}) zniknie z pliku. " +
                       "Tego nie da się cofnąć (Ctrl+Z) — ale dopóki nie zapiszesz mapy, plik na dysku go zawiera.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Usuń arkusz",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return;
        bool wasActive = ReferenceEquals(sheet, _session);
        var keep = _session;
        if (!_document.RemoveSheet(index)) return;
        ShowSheet(wasActive ? Math.Max(0, index - 1) : _document.Sheets.IndexOf(keep));
    }
}
