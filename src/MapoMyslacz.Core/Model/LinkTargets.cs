namespace MapoMyslacz.Core.Model;

public enum LinkKind { None, Web, Map, File }

/// <summary>Rozwiązany link kafelka: rodzaj i pełny adres/ścieżka.</summary>
public sealed record LinkTarget(LinkKind Kind, string Target)
{
    public bool IsMap => Kind == LinkKind.Map;
}

/// <summary>
/// Linki w kafelkach: adresy internetowe (http, https, mailto…) i pliki — także inne mapy, dzięki czemu mapy łączą
/// się w „kampanię”. Ścieżki zapisywane są względem pliku mapy, więc cały folder z mapami można przenieść.
/// </summary>
public static class LinkTargets
{
    private static readonly string[] WebSchemes = ["http://", "https://", "mailto:", "ftp://", "www."];

    public static LinkTarget Resolve(string? link, string? mapFilePath)
    {
        if (string.IsNullOrWhiteSpace(link)) return new LinkTarget(LinkKind.None, "");
        var s = link.Trim().Trim('"');
        if (WebSchemes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return new LinkTarget(LinkKind.Web, s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + s : s);

        if (s.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(s, UriKind.Absolute, out var fileUri))
            s = fileUri.LocalPath;

        string full;
        try
        {
            var baseDir = mapFilePath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(mapFilePath))!;
            full = Path.GetFullPath(Path.IsPathRooted(s) ? s : Path.Combine(baseDir, s));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LinkTarget(LinkKind.None, s);
        }
        bool isMap = full.EndsWith(MindMap.FileExtension, StringComparison.OrdinalIgnoreCase)
                     || full.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase);
        return new LinkTarget(isMap ? LinkKind.Map : LinkKind.File, full);
    }

    /// <summary>Ścieżka do zapisania w kafelku: względna wobec mapy, gdy plik jest na tym samym dysku.</summary>
    public static string ToStored(string targetPath, string? mapFilePath)
    {
        if (mapFilePath is null) return targetPath;
        try
        {
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(mapFilePath))!;
            var full = Path.GetFullPath(targetPath);
            if (!string.Equals(Path.GetPathRoot(baseDir), Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)) return full;
            return Path.GetRelativePath(baseDir, full);
        }
        catch
        {
            return targetPath;
        }
    }

    /// <summary>Krótki opis linku do podpowiedzi (nazwa pliku albo adres).</summary>
    public static string Describe(string? link) =>
        string.IsNullOrWhiteSpace(link) ? "" :
        link.Contains("://", StringComparison.Ordinal) || link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? link.Trim() :
        Path.GetFileName(link.Trim().TrimEnd('\\', '/'));
}
