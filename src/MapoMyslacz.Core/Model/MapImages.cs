using System.Security.Cryptography;

namespace MapoMyslacz.Core.Model;

/// <summary>Obrazki w kafelkach: identyfikatory z zawartości, adresy „data:”, rozmiary, dołączanie do kafelka.</summary>
public static class MapImages
{
    /// <summary>Domyślna szerokość nowego obrazka na mapie (mniejsze zostają w naturalnym rozmiarze).</summary>
    public const double DefaultWidth = 240;

    public const double MinWidth = 16, MaxWidth = 2400;

    /// <summary>Id z zawartości (12 znaków SHA-256) — ten sam obrazek wklejony dwa razy zajmuje miejsce raz.</summary>
    public static string IdFor(byte[] bytes) => "img-" + Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();

    public static string ToDataUri(byte[] bytes, string mediaType) => $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";

    /// <summary>Bajty obrazka z adresu „data:…;base64,…” (null, gdy to nie jest poprawny adres).</summary>
    public static byte[]? FromDataUri(string? uri)
    {
        if (uri is null || !uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return null;
        int comma = uri.IndexOf(',');
        if (comma < 0 || !uri.AsSpan(0, comma).EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Rozszerzenie pliku dla typu z adresu „data:” (do zapisu obrazka na dysk).</summary>
    public static string ExtensionOf(string? uri) =>
        uri?.StartsWith("data:image/jpeg", StringComparison.OrdinalIgnoreCase) == true ? ".jpg"
        : uri?.StartsWith("data:image/gif", StringComparison.OrdinalIgnoreCase) == true ? ".gif"
        : ".png";

    /// <summary>Rozmiar na mapie: szerokość nie większa niż <paramref name="maxWidth"/>, proporcje zachowane.</summary>
    public static (double W, double H) FitSize(double pixelW, double pixelH, double maxWidth = DefaultWidth)
    {
        if (pixelW <= 0 || pixelH <= 0) return (maxWidth, maxWidth);
        double w = Math.Min(pixelW, maxWidth);
        return (Math.Round(w), Math.Max(1, Math.Round(w * pixelH / pixelW)));
    }

    /// <summary>Typ obrazka z pierwszych bajtów (PNG, JPEG, GIF) — null, gdy to nie jest obsługiwany obrazek.</summary>
    public static string? MediaTypeOf(ReadOnlySpan<byte> b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G' ? "image/png"
        : b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length >= 6 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' ? "image/gif"
        : null;

    /// <summary>
    /// Rozmiar w pikselach odczytany z nagłówka (PNG: IHDR, GIF: ekran logiczny, JPEG: znacznik SOF) — bez dekodowania
    /// obrazka. (0, 0), gdy nagłówka nie da się odczytać.
    /// </summary>
    public static (int W, int H) PixelSize(ReadOnlySpan<byte> b)
    {
        switch (MediaTypeOf(b))
        {
            case "image/png" when b.Length >= 24:
                return ((b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19], (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);
            case "image/gif" when b.Length >= 10:
                return (b[6] | (b[7] << 8), b[8] | (b[9] << 8));
            case "image/jpeg":
                int i = 2;
                while (i + 9 < b.Length)
                {
                    if (b[i] != 0xFF) { i++; continue; }
                    byte marker = b[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) { i += 2; continue; }
                    int len = (b[i + 2] << 8) | b[i + 3];
                    // SOF0–SOF15 bez DHT (C4), JPG (C8) i DAC (CC): wysokość, potem szerokość
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                        return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                    if (len < 2) break;
                    i += 2 + len;
                }
                break;
        }
        return (0, 0);
    }

    /// <summary>Wstawia obrazek do magazynu mapy i dołącza go do kafelka (zastępuje poprzedni).</summary>
    public static void Attach(MindMap map, Node node, string id, string dataUri, double width, double height)
    {
        (map.Images ??= [])[id] = dataUri;
        node.Image = new NodeImage { Id = id, Width = Math.Round(width), Height = Math.Round(height) };
    }

    /// <summary>Nowa szerokość obrazka z zachowaniem proporcji.</summary>
    public static void Resize(NodeImage image, double width)
    {
        double w = Math.Clamp(Math.Round(width), MinWidth, MaxWidth);
        if (image.Width > 0) image.Height = Math.Max(1, Math.Round(image.Height * w / image.Width));
        image.Width = w;
    }

    /// <summary>Obrazki, do których odwołują się kafelki (tylko one trafiają do pliku).</summary>
    public static Dictionary<string, string>? Referenced(IEnumerable<Node> nodes, IReadOnlyDictionary<string, string>? store)
    {
        if (store is null) return null;
        var result = new Dictionary<string, string>();
        foreach (var n in nodes)
            if (n.Image is { } img && store.TryGetValue(img.Id, out var data)) result[img.Id] = data;
        return result.Count == 0 ? null : result;
    }
}
