using MapoMyslacz.Core.Model;
using MapoMyslacz.Editor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Storage;

namespace MapoMyslacz;

/// <summary>Obrazki w kafelkach: wybór pliku, upuszczanie na płótno, sekcja w panelu.</summary>
public sealed partial class MainWindow
{
    /// <summary>Okno wyboru obrazka — do kafelka <paramref name="nodeId"/> albo jako nowy kafelek.</summary>
    private async Task PickImageAsync(string? nodeId)
    {
        string? path;
        try
        {
            var picker = new FileOpenPicker(AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
            foreach (var ext in ImagePrep.Extensions) picker.FileTypeFilter.Add(ext);
            path = (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się otworzyć okna wyboru pliku: {ex.Message}", InfoBarSeverity.Error);
            return;
        }
        if (path is null) return;
        await InsertImageFileAsync(path, null, nodeId);
    }

    private async Task InsertImageFileAsync(string path, Vec? at, string? nodeId)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            await Map.InsertImageAsync(stream, at, nodeId);
        }
        catch (Exception ex)
        {
            ShowToast($"Nie udało się wczytać obrazka: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>Obrazki upuszczone na okno: na kafelek — do niego, na puste miejsce — nowe kafelki w tym miejscu.</summary>
    private async Task DropImagesAsync(IReadOnlyList<StorageFile> files, Windows.Foundation.Point onCanvas)
    {
        var (world, nodeId) = Map.HitAt(onCanvas);
        int i = 0;
        foreach (var f in files)
        {
            using var stream = await f.OpenReadAsync();
            // kilka plików na puste miejsce — schodkami, żeby się nie nakładały
            await Map.InsertImageAsync(stream, world + new Vec(i * 40, i * 40), i == 0 ? nodeId : null);
            i++;
        }
    }

    /// <summary>Sekcja „Obrazek” w panelu kafelka.</summary>
    private void BuildImageSection(int count)
    {
        Section("Obrazek");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var pick = new Button { Content = count == 1 ? "Wstaw…" : "Wstaw do pierwszego…" };
        _refreshers.Add(() => pick.Content = SelNodes() is [{ Image: not null }] ? "Zmień…" : "Wstaw…");
        pick.IsEnabled = count == 1;
        ToolTipService.SetToolTip(pick, "Obrazek nad tekstem kafelka — można też wkleić (Ctrl+V) albo przeciągnąć plik na kafelek");
        pick.Click += (_, _) => _ = PickImageAsync(SelNodes().FirstOrDefault()?.Id);
        var remove = new Button { Content = "Usuń" };
        remove.Click += (_, _) => Map.RemoveImages();
        var open = new Button { Content = "Otwórz" };
        open.Click += (_, _) => { if (SelNodes() is [{ Image: not null } n]) Map.OpenImage(n); };
        _refreshers.Add(() =>
        {
            bool any = SelNodes().Any(n => n.Image is not null);
            remove.IsEnabled = any;
            open.IsEnabled = SelNodes() is [{ Image: not null }];
        });
        row.Children.Add(pick);
        row.Children.Add(remove);
        row.Children.Add(open);
        Add(row);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        var width = new NumberBox
        {
            Minimum = MapImages.MinWidth,
            Maximum = MapImages.MaxWidth,
            SmallChange = 10,
            LargeChange = 50,
            Width = 120,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        ToolTipService.SetToolTip(width, "Szerokość obrazka na mapie (wysokość według proporcji); też kwadracikiem w rogu kafelka");
        width.ValueChanged += (_, e) =>
        {
            if (_refreshing || double.IsNaN(e.NewValue)) return;
            if (SelNodes().FirstOrDefault(n => n.Image is not null)?.Image is { } img && Math.Abs(img.Width - e.NewValue) < 0.5) return;
            Map.SetImageWidth(e.NewValue);
        };
        _refreshers.Add(() =>
        {
            var img = SelNodes().FirstOrDefault(n => n.Image is not null)?.Image;
            width.IsEnabled = img is not null;
            width.Value = img?.Width ?? double.NaN;
        });
        sizeRow.Children.Add(width);
        sizeRow.Children.Add(new TextBlock { Text = "szerokość", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 12 });
        Add(sizeRow);
        Add(new TextBlock
        {
            Text = "Wklej obrazek (Ctrl+V) albo przeciągnij plik na kafelek. Obrazek zapisuje się w pliku mapy — mapa zostaje jednym plikiem.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0),
        });
    }
}
