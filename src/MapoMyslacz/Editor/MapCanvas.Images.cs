using System.Diagnostics;
using MapoMyslacz.Core.Model;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace MapoMyslacz.Editor;

/// <summary>Obrazki w kafelkach: wstawianie (plik, schowek, przeciągnięcie), zmiana rozmiaru, usuwanie, podgląd.</summary>
public sealed partial class MapCanvas
{
    /// <summary>Użytkownik chce wybrać obrazek z dysku dla kafelka (okno wyboru pliku otwiera okno główne).</summary>
    public event Action<string?>? PickImageRequested;

    private void OnImageLoaded()
    {
        // nowa bitmapa — bitmapy sceny i minimapy trzeba narysować od nowa
        _sceneVersion++;
        _canvas.Invalidate();
    }

    /// <summary>
    /// Wstawia obrazek: do kafelka <paramref name="nodeId"/> (zastępuje poprzedni) albo jako nowy kafelek w punkcie
    /// <paramref name="at"/> (domyślnie pod kursorem / na środku widoku). Jeden krok cofania.
    /// </summary>
    public async Task<bool> InsertImageAsync(IRandomAccessStream stream, Vec? at = null, string? nodeId = null)
    {
        ImagePrep.Prepared img;
        try
        {
            img = await ImagePrep.PrepareAsync(stream);
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Nie udało się wczytać obrazka: {ex.Message}");
            return false;
        }
        CommitEdit();
        var (w, h) = MapImages.FitSize(img.PixelWidth, img.PixelHeight);
        if (nodeId is not null && _session.Index.Get(nodeId) is { } target)
        {
            _session.Edit(m => MapImages.Attach(m, target, img.Id, img.DataUri, w, h));
            _session.SelectNode(target.Id);
        }
        else
        {
            Node? created = null;
            var p = at ?? PastePoint;
            _session.Edit(m =>
            {
                created = MapOps.AddFloating(m, p, "");
                MapImages.Attach(m, created, img.Id, img.DataUri, w, h);
            });
            if (created is not null)
            {
                _session.SelectNode(created.Id);
                EnsureVisible(created.Id);
            }
        }
        return true;
    }

    public void RequestPickImage() => PickImageRequested?.Invoke(_session.SingleNode?.Id);

    public void RemoveImages()
    {
        var nodes = _session.SelectedNodeObjects.Where(n => n.Image is not null).ToList();
        if (nodes.Count > 0) _session.Edit(_ => nodes.ForEach(n => n.Image = null));
    }

    /// <summary>Szerokość obrazka (wysokość według proporcji) — z panelu właściwości.</summary>
    public void SetImageWidth(double width)
    {
        var nodes = _session.SelectedNodeObjects.Where(n => n.Image is not null).ToList();
        if (nodes.Count > 0) _session.Edit(_ => nodes.ForEach(n => MapImages.Resize(n.Image!, width)));
    }

    /// <summary>Naturalny rozmiar obrazka (z pikseli bitmapy, nie szerszy niż domyślny) — dwuklik na uchwycie.</summary>
    private void ResetImageSize(Node node)
    {
        if (node.Image is not { } img) return;
        var bmp = MapScene.BitmapFor(img.Id);
        var (w, _) = bmp is null ? (MapImages.DefaultWidth, 0d) : MapImages.FitSize(bmp.SizeInPixels.Width, bmp.SizeInPixels.Height);
        _session.Edit(_ => MapImages.Resize(img, w));
    }

    /// <summary>Otwiera obrazek kafelka w programie systemowym (kopia w folderze tymczasowym).</summary>
    public void OpenImage(Node node)
    {
        if (node.Image is not { } img || !_session.Images.TryGetValue(img.Id, out var data) || MapImages.FromDataUri(data) is not { } bytes) return;
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "MapoMyslacz");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, img.Id + MapImages.ExtensionOf(data));
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Notify?.Invoke($"Nie udało się otworzyć obrazka: {ex.Message}");
        }
    }

    /// <summary>Punkt mapy i kafelek pod punktem płótna (np. przy upuszczaniu pliku).</summary>
    public (Vec World, string? NodeId) HitAt(Point p)
    {
        var w = ToWorld(p);
        return (w, NodeAt(w));
    }
}
