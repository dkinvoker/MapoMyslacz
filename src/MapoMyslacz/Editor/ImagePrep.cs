using MapoMyslacz.Core.Model;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace MapoMyslacz.Editor;

/// <summary>
/// Przygotowanie obrazka do mapy: duże zdjęcia zmniejszane (dłuższy bok do 1600 px — plik mapy nie puchnie), zapis jako
/// PNG, a duże zdjęcia bez przezroczystości jako JPEG. Wynik to adres „data:” i id z zawartości.
/// </summary>
internal static class ImagePrep
{
    public const int MaxSide = 1600;
    private const int JpegAbove = 400_000;

    public sealed record Prepared(string Id, string DataUri, int PixelWidth, int PixelHeight);

    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico"];

    public static bool IsImageFile(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static async Task<Prepared> PrepareAsync(IRandomAccessStream input)
    {
        var device = MapScene.SharedDevice;
        using var source = await CanvasBitmap.LoadAsync(device, input, 96);
        var size = source.SizeInPixels;
        double scale = Math.Min(1, (double)MaxSide / Math.Max(size.Width, size.Height));
        int w = Math.Max(1, (int)Math.Round(size.Width * scale)), h = Math.Max(1, (int)Math.Round(size.Height * scale));
        using var target = new CanvasRenderTarget(device, w, h, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Microsoft.UI.Colors.Transparent);
            ds.DrawImage(source, new Rect(0, 0, w, h), source.Bounds, 1f, CanvasImageInterpolation.HighQualityCubic);
        }
        byte[] bytes = await EncodeAsync(target, CanvasBitmapFileFormat.Png);
        string type = "image/png";
        if (bytes.Length > JpegAbove && !HasTransparency(target.GetPixelBytes()))
        {
            var jpeg = await EncodeAsync(target, CanvasBitmapFileFormat.Jpeg, 0.9f);
            if (jpeg.Length < bytes.Length)
            {
                bytes = jpeg;
                type = "image/jpeg";
            }
        }
        return new Prepared(MapImages.IdFor(bytes), MapImages.ToDataUri(bytes, type), w, h);
    }

    private static bool HasTransparency(byte[] bgra)
    {
        for (int i = 3; i < bgra.Length; i += 4)
            if (bgra[i] < 250) return true;
        return false;
    }

    private static async Task<byte[]> EncodeAsync(CanvasRenderTarget bitmap, CanvasBitmapFileFormat format, float quality = 1f)
    {
        using var ms = new InMemoryRandomAccessStream();
        await bitmap.SaveAsync(ms, format, quality);
        return await ReadAllAsync(ms);
    }

    public static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        uint size = (uint)stream.Size;
        await reader.LoadAsync(size);
        var bytes = new byte[size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static async Task<InMemoryRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var ms = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ms.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        ms.Seek(0);
        return ms;
    }
}
