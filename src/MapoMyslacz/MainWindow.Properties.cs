using MapoMyslacz.Core.Model;
using MapoMyslacz.Editor;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;

namespace MapoMyslacz;

/// <summary>
/// Panel właściwości po prawej: zależnie od zaznaczenia — kafelki, strzałki albo cała mapa.
/// Kontrolki budowane są w kodzie przy zmianie zaznaczenia; przy zmianie treści (np. cofnięcie) tylko
/// odświeżają wartości (lista <see cref="_refreshers"/>), żeby nie gubić fokusu w trakcie pisania notatki.
/// Każda zmiana z panelu to jeden krok cofania.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<Action> _refreshers = [];
    private bool _refreshing;

    private void RebuildProperties()
    {
        _refreshers.Clear();
        PropsHost.Children.Clear();
        var nodes = _session.SelectedNodes.Count;
        var links = _session.SelectedLinks.Count;
        if (nodes > 0) BuildNodePanel(nodes);
        if (links > 0) BuildLinkPanel(links, separated: nodes > 0);
        if (nodes == 0 && links == 0) BuildMapPanel();
        RefreshProperties();
    }

    private void RefreshProperties()
    {
        _refreshing = true;
        try
        {
            foreach (var r in _refreshers) r();
        }
        finally
        {
            _refreshing = false;
        }
    }

    // ================================================================== dostęp do zaznaczenia (zawsze aktualny — po cofnięciu obiekty są nowe)

    private List<Node> SelNodes() => _session.SelectedNodeObjects.ToList();
    private List<Link> SelLinks() => _session.SelectedLinkObjects.ToList();

    private void EditNodes(Action<Node> change)
    {
        if (_refreshing) return;
        Map.CommitEdit();
        var nodes = SelNodes();
        if (nodes.Count > 0) _session.Edit(_ => nodes.ForEach(change));
    }

    private void EditNodeStyles(Action<NodeStyle> change) => EditNodes(n => MapOps.EditStyle(n, change));

    private void EditLinks(Action<Link> change)
    {
        if (_refreshing) return;
        Map.CommitEdit();
        var links = SelLinks();
        if (links.Count > 0) _session.Edit(_ => links.ForEach(change));
    }

    private void EditLinkStyles(Action<LinkStyle> change) => EditLinks(l => MapOps.EditStyle(l, change));

    // ================================================================== panele

    private void BuildMapPanel()
    {
        Header("Mapa", "Nic nie jest zaznaczone — ustawienia całej mapy.");

        Section("Tytuł");
        var title = new TextBox { PlaceholderText = "Tytuł mapy (podpowiedź nazwy pliku)" };
        BindText(title, () => _session.Map.Title, t =>
        {
            var v = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
            if (v != _session.Map.Title) _session.Edit(m => m.Title = v);
        });
        title.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) Map.Focus(FocusState.Programmatic); };
        Add(title);

        Section("Tło płótna");
        Add(Swatches(
            ["#FFFFFF", "#FAF7F0", "#F3F6FA", "#EEF7EE", "#FFF8E6", "#2B2A33", "#1F1E24", "#0F172A"],
            () => _session.Map.Background,
            c => _session.Edit(m => m.Background = c),
            autoTip: "Automatyczne (jasne albo ciemne jak motyw Windows)", allowCustom: true));

        Section("Wskazówki");
        var tips = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, FontSize = 12.5, LineHeight = 20 };
        tips.Text =
            "• Dwuklik na pustym miejscu — nowy kafelek\n" +
            "• Tab — dziecko, Enter — rodzeństwo\n" +
            "• Zacznij pisać, żeby zmienić tekst zaznaczonego kafelka\n" +
            "• Kółko „+” przy kafelku przeciągnij na inny kafelek — strzałka\n" +
            "• Przeciągnij zaznaczoną strzałkę, żeby ją wygiąć\n" +
            "• Przeciągnij puste miejsce — przesuwanie, Shift/Ctrl — zaznaczanie\n" +
            "• Wklejony tekst z wcięciami zamienia się w gałęzie\n" +
            "• F1 — wszystkie skróty";
        Add(tips);
    }

    private void BuildNodePanel(int count)
    {
        Header(count == 1 ? "Kafelek" : $"Kafelki ({count})", count == 1 ? null : "Zmiany obejmą wszystkie zaznaczone kafelki.");

        if (count == 1)
        {
            Section("Tekst");
            var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 34, MaxHeight = 140 };
            BindText(text, () => SelNodes() is [{ } n] ? n.Text : null, t =>
            {
                var v = t.Trim();
                if (SelNodes() is [{ } n] && n.Text != v) EditNodes(x => x.Text = v);
            });
            Add(text);
        }

        Section("Gotowe style");
        var presets = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 128, ItemHeight = 38, MaximumRowsOrColumns = 2 };
        foreach (var p in Styles.Presets) presets.Children.Add(PresetButton(p));
        Add(presets);

        BuildMarkersSection();

        Section("Kształt");
        var shapes = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 37, ItemHeight = 34, MaximumRowsOrColumns = 7 };
        var shapeButtons = new List<(ToggleButton Button, NodeShape Shape)>();
        foreach (var (shape, name) in Editor.MapCanvas.ShapeNames)
        {
            var s = shape;
            var b = new ToggleButton { Content = ShapeIcon(s), Padding = new Thickness(4), Width = 34, Height = 30 };
            ToolTipService.SetToolTip(b, name);
            b.Click += (_, _) => EditNodeStyles(st => st.Shape = s == NodeShape.Rounded ? null : s);
            shapes.Children.Add(b);
            shapeButtons.Add((b, s));
        }
        _refreshers.Add(() =>
        {
            var cur = SelNodes().FirstOrDefault()?.Style?.Shape ?? NodeShape.Rounded;
            foreach (var (b, s) in shapeButtons) b.IsChecked = s == cur;
        });
        Add(shapes);

        Section("Wypełnienie");
        Add(Swatches(Styles.Swatches.Prepend("#FFFFFF00"), () => SelNodes().FirstOrDefault()?.Style?.Fill,
            c => EditNodeStyles(s => s.Fill = c), autoTip: "Domyślne", allowCustom: true));

        Section("Tekst — wygląd");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var size = new NumberBox
        {
            Minimum = 6,
            Maximum = 120,
            SmallChange = 1,
            LargeChange = 4,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        ToolTipService.SetToolTip(size, "Rozmiar czcionki");
        size.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            EditNodeStyles(s => s.FontSize = Math.Abs(e.NewValue - Styles.DefaultFontSize) < 0.01 ? null : e.NewValue);
        };
        _refreshers.Add(() => size.Value = SelNodes().FirstOrDefault()?.Style?.FontSize ?? Styles.DefaultFontSize);
        row.Children.Add(size);
        row.Children.Add(StyleToggle("", "Pogrubienie (Ctrl+B)", n => n.Style?.Bold == true, on => EditNodeStyles(s => s.Bold = on ? true : null)));
        row.Children.Add(StyleToggle("", "Kursywa (Ctrl+I)", n => n.Style?.Italic == true, on => EditNodeStyles(s => s.Italic = on ? true : null)));
        row.Children.Add(StyleToggle("", "Przekreślenie", n => n.Style?.Strike == true, on => EditNodeStyles(s => s.Strike = on ? true : null)));
        Add(row);

        var align = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (a, glyph, tip) in new[] { (TextAlign.Left, "", "Do lewej"), (TextAlign.Center, "", "Do środka"), (TextAlign.Right, "", "Do prawej") })
        {
            var al = a;
            align.Children.Add(StyleToggle(glyph, tip, n => (n.Style?.Align ?? TextAlign.Center) == al,
                _ => EditNodeStyles(s => s.Align = al == TextAlign.Center ? null : al), exclusive: true));
        }
        Add(align);
        Add(Caption("Kolor tekstu"));
        Add(Swatches(["#1E1B26", "#4B4757", "#FFFFFF", "#E5484D", "#F76B15", "#15831C", "#0090FF", "#7F00AC"],
            () => SelNodes().FirstOrDefault()?.Style?.TextColor, c => EditNodeStyles(s => s.TextColor = c),
            autoTip: "Automatyczny (czytelny na tle kafelka)", allowCustom: true));

        Section("Ramka");
        Add(Swatches(["#1E1B26", "#4B4757", "#8A8794", "#E5484D", "#F76B15", "#F5D90A", "#15831C", "#0090FF", "#7F00AC", "#D6409F"],
            () => SelNodes().FirstOrDefault()?.Style?.Border, c => EditNodeStyles(s =>
            {
                s.Border = c;
                if (c is not null && (s.BorderWidth ?? 0) <= 0) s.BorderWidth = 2;
            }), autoTip: "Domyślny kolor ramki", allowCustom: true));
        var borderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        var bw = new NumberBox
        {
            Minimum = 0,
            Maximum = 24,
            SmallChange = 1,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        ToolTipService.SetToolTip(bw, "Grubość ramki (0 = bez ramki)");
        bw.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            EditNodeStyles(s => s.BorderWidth = e.NewValue <= 0 ? null : e.NewValue);
        };
        _refreshers.Add(() => bw.Value = SelNodes().FirstOrDefault()?.Style?.BorderWidth ?? 0);
        borderRow.Children.Add(bw);
        borderRow.Children.Add(new TextBlock { Text = "grubość", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
        Add(borderRow);
        var patternRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (p, label) in new[] { (LinePattern.Solid, "Ciągła"), (LinePattern.Dashed, "Kreski"), (LinePattern.Dotted, "Kropki") })
        {
            var pat = p;
            patternRow.Children.Add(TextToggle(label, () => (SelNodes().FirstOrDefault()?.Style?.BorderPattern ?? LinePattern.Solid) == pat,
                () => EditNodeStyles(s =>
                {
                    s.BorderPattern = pat == LinePattern.Solid ? null : pat;
                    if ((s.BorderWidth ?? 0) <= 0) s.BorderWidth = 2;
                })));
        }
        Add(patternRow);

        var idx = _session.Index;
        if (SelNodes().Any(n => n.Parent is not null || idx.ChildrenOf(n.Id).Count > 0))
        {
            Section("Kolor gałęzi");
            Add(Swatches(Styles.BranchPalette.Select(c => c.ToString()).Append("#8A8794").Append("#1E1B26"),
                () => SelNodes().FirstOrDefault()?.Style?.BranchColor, c => EditNodeStyles(s => s.BranchColor = c),
                autoTip: "Automatyczny (dziedziczony / z palety)", allowCustom: true));
        }

        Section("Szerokość tekstu");
        var width = new NumberBox
        {
            Minimum = 0,
            Maximum = 1200,
            SmallChange = 20,
            LargeChange = 100,
            Width = 120,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        ToolTipService.SetToolTip(width, "Po ilu pikselach tekst się zawija (0 = automatycznie)");
        width.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            EditNodes(n => n.Width = e.NewValue <= 0 ? null : e.NewValue);
        };
        _refreshers.Add(() => width.Value = SelNodes().FirstOrDefault()?.Width ?? 0);
        Add(width);

        if (count == 1)
        {
            Section("Notatka");
            var note = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 90,
                MaxHeight = 260,
                PlaceholderText = "Dłuższy opis, szczegóły, linki… (na kafelku pojawi się znaczek)",
            };
            ScrollViewer.SetVerticalScrollBarVisibility(note, ScrollBarVisibility.Auto);
            BindText(note, () => SelNodes() is [{ } n] ? n.Note : null, t =>
            {
                var v = string.IsNullOrWhiteSpace(t) ? null : t.TrimEnd();
                if (SelNodes() is [{ } n] && n.Note != v) EditNodes(x => x.Note = v);
            });
            Add(note);

            Section("Link");
            var link = new TextBox { PlaceholderText = "https://… albo inna mapa (.mapo), plik…" };
            BindText(link, () => SelNodes() is [{ } n] ? n.Link : null, t =>
            {
                var v = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
                if (SelNodes() is [{ } n] && n.Link != v) EditNodes(x => x.Link = v);
            });
            link.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) Map.Focus(FocusState.Programmatic); };
            Add(link);
            var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            var pick = new Button { Content = "Wybierz plik…" };
            ToolTipService.SetToolTip(pick, "Inna mapa (np. następna sesja kampanii) albo dowolny plik — zapisany względem tej mapy");
            pick.Click += (_, _) => _ = PickLinkTargetAsync();
            var open = new Button { Content = "Otwórz" };
            open.Click += (_, _) => { if (SelNodes() is [{ } n]) _ = OpenLinkAsync(n); };
            _refreshers.Add(() => open.IsEnabled = SelNodes() is [{ Link.Length: > 0 }]);
            linkRow.Children.Add(pick);
            linkRow.Children.Add(open);
            Add(linkRow);
            Add(new TextBlock
            {
                Text = "Ctrl+klik na kafelku albo klik w niebieski znaczek otwiera link. Mapa .mapo otwiera się w tym oknie — Alt+← wraca.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0),
            });
        }

        Section("Polecenia");
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 128, ItemHeight = 38, MaximumRowsOrColumns = 2 };
        actions.Children.Add(ActionButton("", "Uporządkuj", Map.ArrangeSelection));
        actions.Children.Add(ActionButton("", "Zwiń / rozwiń", Map.ToggleCollapse));
        actions.Children.Add(ActionButton("", "Odłącz", Map.DetachSelection));
        actions.Children.Add(ActionButton("", "Usuń", Map.DeleteSelection));
        Add(actions);
    }

    private void BuildLinkPanel(int count, bool separated)
    {
        if (separated) Add(new Rectangle { Height = 1, Fill = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Margin = new Thickness(0, 14, 0, 4) });
        Header(count == 1 ? "Strzałka" : $"Strzałki ({count})", null);

        if (count == 1)
        {
            Section("Podpis");
            var label = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, PlaceholderText = "np. „jest bratem”, „prowadzi do”" };
            BindText(label, () => SelLinks() is [{ } l] ? l.Label : null, t =>
            {
                var v = t.Trim().Length == 0 ? null : t.Trim();
                if (SelLinks() is [{ } l] && l.Label != v) EditLinks(x => x.Label = v);
            });
            Add(label);
        }

        Section("Kolor");
        Add(Swatches(["#8A8794", "#00000066", "#1E1B26", "#E5484D", "#F76B15", "#30A46C", "#0090FF", "#7F00AC", "#D6409F"],
            () => SelLinks().FirstOrDefault()?.Style?.Color, c => EditLinkStyles(s => s.Color = c), autoTip: "Domyślny", allowCustom: true));

        Section("Linia");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var width = new NumberBox
        {
            Minimum = 0.5,
            Maximum = 24,
            SmallChange = 0.5,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        ToolTipService.SetToolTip(width, "Grubość linii");
        width.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            EditLinkStyles(s => s.Width = Math.Abs(e.NewValue - Styles.DefaultLinkWidth) < 0.01 ? null : e.NewValue);
        };
        _refreshers.Add(() => width.Value = SelLinks().FirstOrDefault()?.Style?.Width ?? Styles.DefaultLinkWidth);
        row.Children.Add(width);
        row.Children.Add(new TextBlock { Text = "grubość", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
        Add(row);
        var patternRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (p, label) in new[] { (LinePattern.Solid, "Ciągła"), (LinePattern.Dashed, "Kreski"), (LinePattern.Dotted, "Kropki") })
        {
            var pat = p;
            patternRow.Children.Add(TextToggle(label, () => Styles.Resolve(SelLinks().FirstOrDefault()?.Style).Pattern == pat,
                () => EditLinkStyles(s => s.Pattern = pat == LinePattern.Dashed ? null : pat)));
        }
        Add(patternRow);

        Section("Groty i kształt");
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        arrows.Children.Add(TextToggle("◀ Początek", () => Styles.Resolve(SelLinks().FirstOrDefault()?.Style).ArrowStart,
            () => EditLinkStyles(s => s.ArrowStart = Styles.Resolve(s).ArrowStart ? null : true)));
        arrows.Children.Add(TextToggle("Koniec ▶", () => Styles.Resolve(SelLinks().FirstOrDefault()?.Style).ArrowEnd,
            () => EditLinkStyles(s => s.ArrowEnd = Styles.Resolve(s).ArrowEnd ? false : null)));
        Add(arrows);
        var curve = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        curve.Children.Add(TextToggle("Zakrzywiona", () => Styles.Resolve(SelLinks().FirstOrDefault()?.Style).Curve == LinkCurve.Curved,
            () => EditLinkStyles(s => s.Curve = null)));
        curve.Children.Add(TextToggle("Prosta", () => Styles.Resolve(SelLinks().FirstOrDefault()?.Style).Curve == LinkCurve.Straight,
            () => EditLinkStyles(s => s.Curve = LinkCurve.Straight)));
        Add(curve);

        Section("Rozmiar podpisu");
        var fs = new NumberBox
        {
            Minimum = 6,
            Maximum = 72,
            SmallChange = 1,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        fs.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            EditLinkStyles(s => s.FontSize = Math.Abs(e.NewValue - Styles.DefaultLinkFontSize) < 0.01 ? null : e.NewValue);
        };
        _refreshers.Add(() => fs.Value = SelLinks().FirstOrDefault()?.Style?.FontSize ?? Styles.DefaultLinkFontSize);
        Add(fs);

        Section("Polecenia");
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 128, ItemHeight = 38, MaximumRowsOrColumns = 2 };
        actions.Children.Add(ActionButton("", "Odwróć", () => EditLinks(MapOps.Reverse)));
        actions.Children.Add(ActionButton("", "Zwykły łuk", () => EditLinks(l => { l.C1 = l.C2 = null; l.Via = null; })));
        actions.Children.Add(ActionButton("", "Usuń", Map.DeleteSelection));
        Add(actions);
    }

    // ================================================================== klocki panelu

    private void Add(UIElement e) => PropsHost.Children.Add(e);

    /// <summary>Znaczniki: priorytet i postęp (po jednym), symbole (dowolnie wiele), własne emoji.</summary>
    private void BuildMarkersSection()
    {
        Section("Znaczniki");
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 37, ItemHeight = 36, MaximumRowsOrColumns = 7 };
        var emojiFont = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Emoji");

        void AddToggle(Marker m, UIElement content)
        {
            var b = new ToggleButton { Content = content, Width = 34, Height = 32, Padding = new Thickness(0) };
            ToolTipService.SetToolTip(b, m.Name);
            b.Click += (_, _) =>
            {
                if (_refreshing) return;
                bool on = !SelNodes().All(n => Markers.Has(n, m.Id));
                EditNodes(n => Markers.Set(n, m.Id, on));
                RefreshProperties();
            };
            _refreshers.Add(() => b.IsChecked = SelNodes().Count > 0 && SelNodes().All(n => Markers.Has(n, m.Id)));
            grid.Children.Add(b);
        }

        foreach (var m in Markers.Priorities)
        {
            var color = Markers.PriorityColors[Markers.PriorityValue(m.Id) - 1];
            AddToggle(m, new Grid
            {
                Width = 20,
                Height = 20,
                Children =
                {
                    new Microsoft.UI.Xaml.Shapes.Ellipse { Fill = Xaml.Brush(color) },
                    new TextBlock
                    {
                        Text = m.Glyph, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Xaml.Brush(Rgba.White),
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                },
            });
        }
        // przerwa do końca wiersza (7 kolumn): priorytety 1–5 + 2 puste miejsca
        grid.Children.Add(new Border());
        grid.Children.Add(new Border());
        foreach (var m in Markers.Progress)
        {
            AddToggle(m, new TextBlock
            {
                Text = m.Id == "progress-100" ? "✔" : m.Glyph.TrimEnd('%'),
                FontSize = m.Id == "progress-100" ? 14 : 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = Xaml.Brush(Rgba.Parse("#30A46C", Rgba.Black)),
            });
        }
        grid.Children.Add(new Border());
        grid.Children.Add(new Border());
        foreach (var m in Markers.Symbols)
            AddToggle(m, new TextBlock { Text = m.Glyph, FontFamily = emojiFont, FontSize = 15 });
        Add(grid);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var custom = new TextBox { PlaceholderText = "inne emoji (Win+.)", Width = 150 };
        var addCustom = new Button { Content = "Dodaj" };
        void AddEmoji()
        {
            var t = custom.Text.Trim();
            if (t.Length == 0) return;
            EditNodes(n => Markers.Set(n, t, true));
            custom.Text = "";
        }
        addCustom.Click += (_, _) => AddEmoji();
        custom.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            AddEmoji();
        };
        var clear = new Button { Content = new FontIcon { Glyph = "", FontSize = 13 } };
        ToolTipService.SetToolTip(clear, "Usuń wszystkie znaczniki");
        clear.Click += (_, _) => EditNodes(n => n.Icons = null);
        row.Children.Add(custom);
        row.Children.Add(addCustom);
        row.Children.Add(clear);
        Add(row);
    }

    /// <summary>
    /// Pole tekstowe panelu: zapis przy opuszczeniu pola, ale tylko gdy tekst różni się od ostatnio pokazanego —
    /// samo „przejście” fokusu przez pole niczego nie nadpisuje.
    /// </summary>
    private void BindText(TextBox box, Func<string?> get, Action<string> apply)
    {
        string synced = "";
        static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
        box.LostFocus += (_, _) =>
        {
            var t = Normalize(box.Text);
            if (_refreshing || t == synced) return;
            synced = t;
            apply(t);
        };
        _refreshers.Add(() =>
        {
            if (box.FocusState != FocusState.Unfocused) return;
            synced = Normalize(get() ?? "");
            box.Text = synced;
        });
    }

    private void Header(string title, string? subtitle)
    {
        Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
        if (subtitle is not null) Add(new TextBlock { Text = subtitle, TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 });
    }

    private void Section(string title) =>
        Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 2), Opacity = 0.9 });

    private static TextBlock Caption(string text) => new() { Text = text, FontSize = 12, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>Kolorowe kółka do wyboru + „automatycznie” + własny kolor (ColorPicker).</summary>
    private UIElement Swatches(IEnumerable<string> colors, Func<string?> current, Action<string?> apply, string autoTip, bool allowCustom)
    {
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 30, ItemHeight = 30, MaximumRowsOrColumns = 8 };
        var buttons = new List<(Button Button, string? Color)>();
        var stroke = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];

        Button Make(string? color)
        {
            var b = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                BorderBrush = stroke,
            };
            if (color is null)
            {
                b.Content = new TextBlock { Text = "A", FontSize = 12, FontWeight = FontWeights.SemiBold }; // „automatycznie”
                ToolTipService.SetToolTip(b, autoTip);
            }
            else
            {
                var rgba = Rgba.Parse(color, Rgba.White);
                b.Background = Xaml.Brush(rgba);
                if (rgba.A < 40) b.Content = new FontIcon { Glyph = "", FontSize = 11, Opacity = 0.6 }; // przezroczysty
                ToolTipService.SetToolTip(b, rgba.A < 40 ? "Przezroczyste" : color);
            }
            b.Click += (_, _) => { if (!_refreshing) apply(color); };
            buttons.Add((b, color));
            return b;
        }

        grid.Children.Add(Make(null));
        foreach (var c in colors) grid.Children.Add(Make(c));

        if (allowCustom)
        {
            var custom = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                Content = new FontIcon { Glyph = "", FontSize = 12 },
            };
            ToolTipService.SetToolTip(custom, "Inny kolor…");
            var picker = new ColorPicker
            {
                IsAlphaEnabled = true,
                IsMoreButtonVisible = false,
                IsColorSliderVisible = true,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
            };
            var ok = new Button { Content = "Użyj koloru", HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            var flyout = new Flyout { Content = new StackPanel { Spacing = 8, Children = { picker, ok } } };
            flyout.Opening += (_, _) =>
            {
                var cur = Rgba.Parse(current(), Rgba.White);
                picker.Color = cur.ToColor();
            };
            ok.Click += (_, _) =>
            {
                var c = picker.Color;
                flyout.Hide();
                apply(new Rgba(c.R, c.G, c.B, c.A).ToString());
            };
            custom.Flyout = flyout;
            grid.Children.Add(custom);
        }

        var accent = Xaml.Brush(Editor.MapCanvas.Accent);
        _refreshers.Add(() =>
        {
            var cur = current();
            bool matched = false;
            foreach (var (b, c) in buttons)
            {
                bool on = c is null ? cur is null : cur is not null && Rgba.TryParse(cur, out var x) && Rgba.TryParse(c, out var y) && x == y;
                matched |= on;
                b.BorderThickness = new Thickness(on ? 3 : 1);
                b.BorderBrush = on ? accent : stroke;
            }
        });
        return grid;
    }

    /// <summary>Przełącznik z ikoną (pogrubienie, wyrównanie…).</summary>
    private ToggleButton StyleToggle(string glyph, string tip, Func<Node, bool> isOn, Action<bool> set, bool exclusive = false)
    {
        var b = new ToggleButton { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Width = 36, Height = 32, Padding = new Thickness(0) };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) =>
        {
            if (_refreshing) return;
            var first = SelNodes().FirstOrDefault();
            bool currentlyOn = first is not null && isOn(first);
            set(exclusive || !currentlyOn);
            RefreshProperties();
        };
        _refreshers.Add(() => b.IsChecked = SelNodes().FirstOrDefault() is { } n && isOn(n));
        return b;
    }

    private ToggleButton TextToggle(string text, Func<bool> isOn, Action apply)
    {
        var b = new ToggleButton { Content = text, Height = 32, Padding = new Thickness(10, 0, 10, 0) };
        b.Click += (_, _) =>
        {
            if (_refreshing) return;
            apply();
            RefreshProperties();
        };
        _refreshers.Add(() => b.IsChecked = isOn());
        return b;
    }

    private static Button ActionButton(string glyph, string text, Action action)
    {
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Width = 124,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, new TextBlock { Text = text } },
            },
        };
        b.Click += (_, _) => action();
        return b;
    }

    /// <summary>Przycisk gotowego stylu z małym podglądem kafelka.</summary>
    private Button PresetButton(StylePreset p)
    {
        var s = Styles.Resolve(p.Style, Styles.BranchPalette[0], Map.CanvasColor);
        var preview = ShapePreview(s.Shape, 20, 13, s.Fill, s.BorderWidth > 0 ? s.Border : null, Math.Min(2, s.BorderWidth), s.BorderPattern);
        var b = new Button
        {
            Width = 124,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(5, 4, 5, 4),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    preview,
                    new TextBlock
                    {
                        Text = p.Name,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = s.Bold ? FontWeights.SemiBold : FontWeights.Normal,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            },
        };
        ToolTipService.SetToolTip(b, p.Description);
        b.Click += (_, _) => Map.ApplyPreset(p);
        return b;
    }

    private static UIElement ShapeIcon(NodeShape shape)
    {
        var fg = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var fill = Xaml.Brush(Rgba.Parse("#B98BF0", Rgba.White));
        if (shape == NodeShape.None)
            return new TextBlock { Text = "Aa", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var el = ShapePreview(shape, 22, 14, Rgba.Parse("#B98BF0", Rgba.White), null, 0, LinePattern.Solid);
        _ = fg;
        _ = fill;
        return el;
    }

    private static FrameworkElement ShapePreview(NodeShape shape, double w, double h, Rgba fill, Rgba? border, double borderWidth, LinePattern pattern)
    {
        Shape el = shape switch
        {
            NodeShape.Ellipse => new Ellipse(),
            NodeShape.Diamond => new Polygon { Points = Xaml.Points([new(w / 2, 0), new(w, h / 2), new(w / 2, h), new(0, h / 2)]) },
            NodeShape.Hexagon => new Polygon { Points = Xaml.Points(GeometryMath.HexagonPoints(new Box(0, 0, w, h))) },
            NodeShape.None => new Rectangle { Fill = null },
            _ => new Rectangle
            {
                RadiusX = shape switch { NodeShape.Pill => h / 2, NodeShape.Rectangle => 1, _ => 4 },
                RadiusY = shape switch { NodeShape.Pill => h / 2, NodeShape.Rectangle => 1, _ => 4 },
            },
        };
        el.Width = w;
        el.Height = h;
        el.VerticalAlignment = VerticalAlignment.Center;
        if (shape != NodeShape.None) el.Fill = Xaml.Brush(fill.A < 40 ? Rgba.White.WithAlpha(0) : fill);
        if (border is { } bc && borderWidth > 0)
        {
            el.Stroke = Xaml.Brush(bc);
            el.StrokeThickness = borderWidth;
            if (Xaml.Dashes(pattern, borderWidth) is { } d) el.StrokeDashArray = d;
        }
        else if (shape == NodeShape.None || fill.A < 40)
        {
            el.Stroke = Xaml.Brush(Rgba.Parse("#8A8794", Rgba.Black));
            el.StrokeThickness = 1;
            el.StrokeDashArray = new DoubleCollection { 2, 2 };
        }
        return el;
    }
}
