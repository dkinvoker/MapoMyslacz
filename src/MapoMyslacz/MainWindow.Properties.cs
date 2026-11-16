using MapoMyslacz.Core.Model;
using MapoMyslacz.Editor;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using MapFrame = MapoMyslacz.Core.Model.Frame;

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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            RebuildPropertiesCore();
        }
        finally
        {
            Editor.MapCanvas.Trace($"panel {sw.Elapsed.TotalMilliseconds:0.0} ms ({PropsHost.Children.Count} elementów)");
        }
    }

    private void RebuildPropertiesCore()
    {
        _refreshers.Clear();
        PropsHost.Children.Clear();
        var nodes = _session.SelectedNodes.Count;
        var links = _session.SelectedLinks.Count;
        var frames = _session.SelectedFrames.Count;
        var branches = _session.SelectedBranches.Count;
        if (nodes > 0) BuildNodePanel(nodes);
        if (links > 0) BuildLinkPanel(links, separated: nodes > 0);
        if (frames > 0) BuildFramePanel(frames, separated: nodes + links > 0);
        if (branches > 0) BuildBranchPanel(branches, separated: nodes + links + frames > 0);
        if (nodes == 0 && links == 0 && frames == 0 && branches == 0) BuildMapPanel();
        RefreshProperties();
    }

    private void BuildBranchPanel(int count, bool separated)
    {
        if (separated) Add(new Rectangle { Height = 1, Fill = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Margin = new Thickness(0, 14, 0, 4) });
        Header(count == 1 ? "Gałąź" : $"Gałęzie ({count})",
            "Gałąź łączy kafelek z rodzicem w drzewie i rysuje się sama (Tab, Enter, kółko „+”). Jej kolor przechodzi na dalsze gałęzie.");
        List<Node> Children() => _session.SelectedBranchChildren.ToList();
        void EditChildren(Action<Node> change)
        {
            if (_refreshing) return;
            Map.CommitEdit();
            var c = Children();
            if (c.Count > 0) _session.Edit(_ => c.ForEach(change));
        }

        Section("Kolor");
        Add(Swatches(Styles.BranchPalette.Select(c => c.ToString()).Append("#8A8794").Append("#1E1B26"),
            () => Children().FirstOrDefault()?.Style?.BranchColor, c => EditChildren(n => MapOps.EditStyle(n, s => s.BranchColor = c)),
            autoTip: "Automatyczny (dziedziczony / z palety)", allowCustom: true));

        Section("Polecenia");
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 128, ItemHeight = 38, MaximumRowsOrColumns = 2 };
        actions.Children.Add(ActionButton("", "Odłącz", Map.DeleteSelection));
        actions.Children.Add(ActionButton("", "Na strzałkę", Map.BranchesToLinks));
        actions.Children.Add(ActionButton("", "Kafelek", () => _session.Select(Children().Select(n => n.Id))));
        Add(actions);
        Add(new TextBlock
        {
            Text = "„Odłącz” (albo Delete) — kafelek zostaje w miejscu, ale już bez gałęzi. „Na strzałkę” — zamiast gałęzi zwykła strzałka, którą można wyginać i podpisać.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0),
        });
    }

    private List<MapFrame> SelFrames() => _session.SelectedFrameObjects.ToList();

    private void EditFrames(Action<MapFrame> change)
    {
        if (_refreshing) return;
        Map.CommitEdit();
        var frames = SelFrames();
        if (frames.Count > 0) _session.Edit(_ => frames.ForEach(change));
    }

    private void BuildFramePanel(int count, bool separated)
    {
        if (separated) Add(new Rectangle { Height = 1, Fill = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"], Margin = new Thickness(0, 14, 0, 4) });
        Header(count == 1 ? "Ramka" : $"Ramki ({count})",
            "Ramka obejmuje swoje kafelki razem z gałęziami i przesuwa się razem z nimi. Chwyć ją za nagłówek, żeby przesunąć całą grupę.");

        if (count == 1)
        {
            Section("Nazwa");
            var title = new TextBox { PlaceholderText = "np. „Wioska”, „Las”, „Zamek”" };
            BindText(title, () => SelFrames() is [{ } f] ? f.Title : null, t =>
            {
                var v = string.IsNullOrWhiteSpace(t) ? null : t.Trim();
                if (SelFrames() is [{ } f] && f.Title != v) EditFrames(x => x.Title = v);
            });
            title.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) Map.Focus(FocusState.Programmatic); };
            Add(title);
        }

        Section("Kolor");
        Add(Swatches(Styles.FrameColors.Skip(1).Select(c => c.Color), () => SelFrames().FirstOrDefault()?.Color,
            c => EditFrames(f => f.Color = c), autoTip: $"Domyślny ({Styles.FrameColors[0].Name.ToLowerInvariant()})", allowCustom: true));

        var info = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 10, 0, 0) };
        _refreshers.Add(() =>
        {
            var idx = _session.Index;
            int n = SelFrames().SelectMany(f => MapOps.FrameContent(idx, f)).Distinct().Count();
            info.Text = $"Obejmuje {n} {Plural(n, "kafelek", "kafelki", "kafelków")}. Kafelki dodasz z menu kafelka (prawy przycisk → „Dodaj do ramki”).";
        });
        Add(info);

        Section("Polecenia");
        var actions = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 128, ItemHeight = 38, MaximumRowsOrColumns = 2 };
        actions.Children.Add(ActionButton("", "Zaznacz", () => _session.Select(SelFrames().SelectMany(f => f.Nodes))));
        actions.Children.Add(ActionButton("", "Usuń ramkę", Map.DeleteSelection));
        Add(actions);
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

    /// <summary>Wygląd pierwszej zaznaczonej strzałki z uwzględnieniem motywu (null — brak strzałki).</summary>
    private ResolvedLinkStyle? LinkLook() =>
        SelLinks().FirstOrDefault() is { } l ? MapThemes.ResolveLink(_session.Map, _session.Index, l) : null;

    /// <summary>Domyślne strzałki mapy (z motywu albo wybrane osobno).</summary>
    private LinkTheme LinkDefaults => MapThemes.LinksFor(_session.Map);

    // ================================================================== motyw mapy

    private void BuildThemeSection()
    {
        Section("Motyw");
        var grid = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, ItemWidth = 132, ItemHeight = 74, MaximumRowsOrColumns = 2 };
        var cards = new List<(Button Card, MapTheme Theme)>();
        foreach (var t in MapThemes.All)
        {
            var theme = t;
            var card = new Button { Width = 128, Height = 70, Padding = new Thickness(0), CornerRadius = new CornerRadius(8), Content = ThemePreview(theme) };
            ToolTipService.SetToolTip(card, $"{theme.Name} — {theme.Description}");
            card.Click += (_, _) =>
            {
                if (_refreshing) return;
                Map.CommitEdit();
                // własne tło mapy zasłoniłoby tło motywu — przy zmianie motywu wraca do motywu
                _session.Edit(m =>
                {
                    m.Theme = theme == MapThemes.Default ? null : theme.Id;
                    m.Background = null;
                });
            };
            grid.Children.Add(card);
            cards.Add((card, theme));
        }
        var accent = Xaml.Brush(Editor.MapCanvas.Accent);
        var stroke = (Brush)Application.Current.Resources["ControlStrongStrokeColorDefaultBrush"];
        _refreshers.Add(() =>
        {
            var current = MapThemes.Find(_session.Map.Theme);
            foreach (var (c, t) in cards)
            {
                c.BorderThickness = new Thickness(t == current ? 3 : 1);
                c.BorderBrush = t == current ? accent : stroke;
            }
        });
        Add(grid);

        Add(Caption("Strzałki"));
        var arrows = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        arrows.Items.Add("Jak w motywie");
        foreach (var lt in MapThemes.LinkThemes) arrows.Items.Add($"{lt.Name} — {lt.Description}");
        ToolTipService.SetToolTip(arrows, "Domyślny wygląd strzałek — można go łączyć z dowolnym motywem. Strzałki z własnym stylem go zachowują.");
        arrows.SelectionChanged += (_, _) =>
        {
            if (_refreshing || arrows.SelectedIndex < 0) return;
            string? id = arrows.SelectedIndex == 0 ? null : MapThemes.LinkThemes[arrows.SelectedIndex - 1].Id;
            if (id == _session.Map.ArrowStyle) return;
            Map.CommitEdit();
            _session.Edit(m => m.ArrowStyle = id);
        };
        _refreshers.Add(() =>
        {
            int i = MapThemes.LinkThemes.ToList().FindIndex(l => l.Id == _session.Map.ArrowStyle);
            arrows.SelectedIndex = i < 0 ? 0 : i + 1;
        });
        Add(arrows);

        var unify = new Button { Content = "Ujednolić z motywem…", Margin = new Thickness(0, 6, 0, 0) };
        ToolTipService.SetToolTip(unify, "Usuwa własne kolory kafelków i strzałek, żeby cała mapa miała kolory motywu (Ctrl+Z cofa)");
        unify.Click += (_, _) => _ = UnifyWithThemeAsync();
        Add(unify);
        Add(new TextBlock
        {
            Text = "Motyw ustala kolory tego, czego kafelki i strzałki nie mają ustawione same. „Ujednolić” usuwa własne kolory — kształty, czcionki i znaczniki zostają.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 4, 0, 0),
        });
    }

    private async Task UnifyWithThemeAsync()
    {
        var dlg = new ContentDialog
        {
            Title = "Ujednolić mapę z motywem?",
            Content = new TextBlock
            {
                Text = "Kafelki stracą własne wypełnienia, kolory tekstu, ramek i gałęzi, a strzałki — własne kolory, wzory i grubości. " +
                       "Kształty, czcionki, znaczniki i wygięcia strzałek zostają. Ctrl+Z cofa całość.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Ujednolić",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ShowDialogAsync(dlg) != ContentDialogResult.Primary) return;
        Map.CommitEdit();
        int n = 0;
        _session.Edit(m => n = MapThemes.ClearCustomColors(m));
        ShowToast(n == 0 ? "Mapa ma już wyłącznie kolory motywu." : $"Ujednolicono: {n} {Plural(n, "element", "elementy", "elementów")}.", InfoBarSeverity.Informational);
    }

    /// <summary>Miniatura motywu: płótno, temat główny, kafelek, kolory gałęzi i strzałka.</summary>
    private static FrameworkElement ThemePreview(MapTheme t)
    {
        var canvas = t.Canvas ?? Styles.CanvasBackground;
        var central = Styles.Resolve(null, t.BranchPalette[0], canvas, t, central: true);
        var node = Styles.Resolve(null, t.BranchPalette[0], canvas, t);
        var links = Styles.Resolve(null, t.Links, t.BranchPalette[0]);
        var root = new Microsoft.UI.Xaml.Controls.Canvas { Width = 126, Height = 68, Background = Xaml.Brush(canvas) };
        Border Pill(ResolvedNodeStyle s, string text, double x, double y, double w) => new Border
        {
            Width = w, Height = 16, CornerRadius = new CornerRadius(4),
            Background = Xaml.Brush(s.Fill), BorderBrush = Xaml.Brush(s.Border), BorderThickness = new Thickness(Math.Min(1.5, s.BorderWidth)),
            Child = new TextBlock { Text = text, FontSize = 9, Foreground = Xaml.Brush(s.TextColor), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        }.With(b => { Microsoft.UI.Xaml.Controls.Canvas.SetLeft(b, x); Microsoft.UI.Xaml.Controls.Canvas.SetTop(b, y); });
        // gałęzie do dwóch dzieci
        for (int i = 0; i < 2; i++)
        {
            var line = new Line { X1 = 52, Y1 = 22, X2 = 70, Y2 = 14 + i * 18, Stroke = Xaml.Brush(t.BranchPalette[i]), StrokeThickness = 2 };
            root.Children.Add(line);
        }
        var arrow = new Line { X1 = 22, Y1 = 30, X2 = 80, Y2 = 52, Stroke = Xaml.Brush(links.Color), StrokeThickness = Math.Min(2, links.Width) };
        if (Xaml.Dashes(links.Pattern, 2) is { } d) arrow.StrokeDashArray = d;
        root.Children.Add(arrow);
        root.Children.Add(Pill(central, "", 6, 14, 46));
        root.Children.Add(Pill(node, "", 70, 6, 40));
        root.Children.Add(Pill(node, "", 70, 24, 40));
        for (int i = 0; i < 5; i++)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = Xaml.Brush(t.BranchPalette[i]) };
            Microsoft.UI.Xaml.Controls.Canvas.SetLeft(dot, 84 + i * 8);
            Microsoft.UI.Xaml.Controls.Canvas.SetTop(dot, 48);
            root.Children.Add(dot);
        }
        var label = new TextBlock { Text = t.Name, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Xaml.Brush(canvas.PrefersLightText ? Rgba.White : Styles.DarkText) };
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(label, 6);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(label, 50);
        root.Children.Add(label);
        return new Border { CornerRadius = new CornerRadius(7), Child = root };
    }

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

        BuildThemeSection();

        Section("Tło płótna");
        Add(Swatches(
            ["#FFFFFF", "#FAF7F0", "#F3F6FA", "#EEF7EE", "#FFF8E6", "#2B2A33", "#1F1E24", "#0F172A"],
            () => _session.Map.Background,
            c => _session.Edit(m => m.Background = c),
            autoTip: "Automatyczne (jasne albo ciemne jak motyw Windows)", allowCustom: true));

        if (_backlinks.Count > 0 && _backlinksFor == _session.FilePath)
        {
            Section($"Linki do tej mapy ({_backlinks.Count})");
            Add(new TextBlock { Text = "Kafelki innych map z tego folderu, które prowadzą tutaj:", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7 });
            foreach (var b in _backlinks.Take(15))
            {
                var link = b;
                var text = b.NodeText.Replace('\n', ' ').Trim();
                if (text.Length > 32) text = text[..31] + "…";
                var button = new HyperlinkButton
                {
                    Content = new TextBlock { Text = $"{System.IO.Path.GetFileNameWithoutExtension(b.MapPath)} — „{text}”", TextTrimming = TextTrimming.CharacterEllipsis },
                    Padding = new Thickness(4, 3, 4, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                };
                ToolTipService.SetToolTip(button, $"Otwórz mapę {b.MapPath} i pokaż ten kafelek");
                button.Click += (_, _) => _ = OpenBacklinkAsync(link);
                Add(button);
            }
        }

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
            "• Ctrl+F — szukaj, Ctrl+G — ramka wokół zaznaczonych\n" +
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
        foreach (var p in Map.CustomPresets.Concat(Styles.Presets)) presets.Children.Add(PresetButton(p));
        Add(presets);

        BuildImageSection(count);
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
            var link = new TextBox { PlaceholderText = "https://…, inna mapa (.mapo), plik albo kafelek" };
            BindText(link, () => SelNodes() is [{ } n] ? n.Link : null, t =>
            {
                // wklejona pełna ścieżka staje się względna, a link do kafelka tej mapy — krótkim „#id”
                var v = string.IsNullOrWhiteSpace(t) ? null : LinkTargets.Normalize(t, _session.FilePath);
                if (SelNodes() is [{ } n] && n.Link != v) EditNodes(x => x.Link = v);
            });
            link.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) Map.Focus(FocusState.Programmatic); };
            Add(link);
            var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            var pick = new Button { Content = "Wybierz plik…" };
            ToolTipService.SetToolTip(pick, "Inna mapa (np. następna sesja kampanii) albo dowolny plik — zapisany względem tej mapy");
            pick.Click += (_, _) => _ = PickLinkTargetAsync();
            var pickNode = new Button { Content = "Kafelek…" };
            ToolTipService.SetToolTip(pickNode, "Link do innego kafelka tej mapy — kliknij go po naciśnięciu przycisku. Do kafelka w innej mapie: prawy przycisk na nim → „Kopiuj link do kafelka” i wklej tutaj.");
            pickNode.Click += (_, _) => Map.EnterLinkPickMode();
            var open = new Button { Content = "Otwórz" };
            open.Click += (_, _) => { if (SelNodes() is [{ } n]) _ = OpenLinkAsync(n); };
            _refreshers.Add(() => open.IsEnabled = SelNodes() is [{ Link.Length: > 0 }]);
            linkRow.Children.Add(pick);
            linkRow.Children.Add(pickNode);
            linkRow.Children.Add(open);
            Add(linkRow);
            Add(new TextBlock
            {
                Text = "Ctrl+klik na kafelku albo klik w niebieski znaczek otwiera link. Mapa .mapo otwiera się w tym oknie — Alt+← wraca. " +
                       "Link do kafelka w innej mapie: prawy przycisk na nim → „Kopiuj link do kafelka”, potem wklej tutaj.",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0),
            });
        }

        if (count >= 2)
        {
            Section("Wyrównanie");
            var alignRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var (mode, text, glyph) in Editor.MapCanvas.AlignCommands)
            {
                var m = mode;
                var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Width = 38, Height = 32, Padding = new Thickness(0) };
                ToolTipService.SetToolTip(b, text);
                b.Click += (_, _) => Map.AlignSelection(m);
                alignRow.Children.Add(b);
            }
            Add(alignRow);
            var distRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (horizontal, text) in new[] { (true, "Rozłóż ↔"), (false, "Rozłóż ↕") })
            {
                var h = horizontal;
                var b = new Button { Content = text, Height = 32, IsEnabled = count >= 3 };
                ToolTipService.SetToolTip(b, h ? "Równe odstępy w poziomie (skrajne kafelki zostają)" : "Równe odstępy w pionie (skrajne kafelki zostają)");
                b.Click += (_, _) => Map.DistributeSelection(h);
                distRow.Children.Add(b);
            }
            Add(distRow);
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
            if (LinkLook() is { } look && Math.Abs(look.Width - e.NewValue) < 0.01) return;
            EditLinkStyles(s => s.Width = Math.Abs(e.NewValue - LinkDefaults.Width) < 0.01 ? null : e.NewValue);
        };
        _refreshers.Add(() => width.Value = LinkLook()?.Width ?? LinkDefaults.Width);
        row.Children.Add(width);
        row.Children.Add(new TextBlock { Text = "grubość", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
        Add(row);
        var patternRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (p, label) in new[] { (LinePattern.Solid, "Ciągła"), (LinePattern.Dashed, "Kreski"), (LinePattern.Dotted, "Kropki") })
        {
            var pat = p;
            patternRow.Children.Add(TextToggle(label, () => LinkLook()?.Pattern == pat,
                () => EditLinkStyles(s => s.Pattern = pat == LinkDefaults.Pattern ? null : pat)));
        }
        Add(patternRow);

        Section("Groty i kształt");
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        arrows.Children.Add(TextToggle("◀ Początek", () => LinkLook()?.ArrowStart == true,
            () => { bool on = LinkLook()?.ArrowStart == true; EditLinkStyles(s => s.ArrowStart = !on); }));
        arrows.Children.Add(TextToggle("Koniec ▶", () => LinkLook()?.ArrowEnd == true,
            () => { bool on = LinkLook()?.ArrowEnd == true; EditLinkStyles(s => s.ArrowEnd = !on); }));
        Add(arrows);
        var curve = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (c, name) in new[] { (LinkCurve.Curved, "Łuk"), (LinkCurve.Straight, "Prosta"), (LinkCurve.Elbow, "Łamana") })
        {
            var cv = c;
            curve.Children.Add(TextToggle(name, () => LinkLook()?.Curve == cv,
                () => EditLinks(l => Editor.MapCanvas.SetCurve(_session.Map, l, cv))));
        }
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
