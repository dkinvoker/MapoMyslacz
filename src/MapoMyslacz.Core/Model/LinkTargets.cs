using System.Text.RegularExpressions;

namespace MapoMyslacz.Core.Model;

public enum LinkKind { None, Web, Map, File, Node }

/// <summary>
/// Rozwiązany link kafelka: rodzaj i pełny adres/ścieżka. <see cref="NodeId"/> — konkretny kafelek
/// (w innej mapie: „Sesja 2.mapo#id”, w tej samej: „#id”).
/// </summary>
public sealed record LinkTarget(LinkKind Kind, string Target, string? NodeId = null)
{
    public bool IsMap => Kind == LinkKind.Map;
}

/// <summary>
/// Linki w kafelkach: adresy internetowe (http, https, mailto…) i pliki — także inne mapy, dzięki czemu mapy łączą
/// się w „kampanię”, oraz konkretne kafelki („#id”). Ścieżki zapisywane są względem pliku mapy, więc cały folder
/// z mapami można przenieść.
/// </summary>
public static partial class LinkTargets
{
    private static readonly string[] WebSchemes = ["http://", "https://", "mailto:", "ftp://", "www."];

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex NodeIdPattern();

    public static LinkTarget Resolve(string? link, string? mapFilePath)
    {
        if (string.IsNullOrWhiteSpace(link)) return new LinkTarget(LinkKind.None, "");
        var s = link.Trim().Trim('"');
        if (WebSchemes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return new LinkTarget(LinkKind.Web, s.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + s : s);

        // „#id” — kafelek w tej samej mapie
        if (s.StartsWith('#'))
        {
            var id = s[1..].Trim();
            return NodeIdPattern().IsMatch(id)
                ? new LinkTarget(LinkKind.Node, mapFilePath is null ? "" : Path.GetFullPath(mapFilePath), id)
                : new LinkTarget(LinkKind.None, s);
        }

        // „mapa.mapo#id” — kafelek w innej mapie (krzyżyk w nazwie zwykłego pliku zostaje częścią nazwy)
        string? nodeId = null;
        int hash = s.LastIndexOf('#');
        if (hash > 0 && NodeIdPattern().IsMatch(s[(hash + 1)..]) && IsMapPath(s[..hash]))
        {
            nodeId = s[(hash + 1)..];
            s = s[..hash];
        }

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
        return new LinkTarget(IsMapPath(full) ? LinkKind.Map : LinkKind.File, full, nodeId);
    }

    private static bool IsMapPath(string path) =>
        path.EndsWith(MindMap.FileExtension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xmind", StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Link wpisany albo wklejony przez użytkownika w postaci do zapisania: pełna ścieżka do pliku staje się względna,
    /// a link do kafelka tej samej mapy — krótkim „#id”. Adresy internetowe zostają bez zmian.
    /// </summary>
    public static string Normalize(string link, string? mapFilePath)
    {
        var t = Resolve(link, mapFilePath);
        switch (t.Kind)
        {
            case LinkKind.Map or LinkKind.File when Path.IsPathRooted(link.Trim().Trim('"')):
                if (t.NodeId is not null && mapFilePath is not null && SamePath(t.Target, mapFilePath)) return "#" + t.NodeId;
                return ToStored(t.Target, mapFilePath) + (t.NodeId is null ? "" : "#" + t.NodeId);
            default:
                return link.Trim();
        }
    }

    /// <summary>Link do kafelka do skopiowania: pełna ścieżka mapy z „#id” (albo samo „#id”, gdy mapa nie jest zapisana).</summary>
    public static string ForNode(string nodeId, string? mapFilePath) =>
        mapFilePath is null ? "#" + nodeId : $"{Path.GetFullPath(mapFilePath)}#{nodeId}";

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Krótki opis linku do podpowiedzi (nazwa pliku albo adres).</summary>
    public static string Describe(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return "";
        var s = link.Trim();
        if (s.Contains("://", StringComparison.Ordinal) || s.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return s;
        if (s.StartsWith('#')) return "kafelek w tej mapie";
        int hash = s.LastIndexOf('#');
        if (hash > 0 && NodeIdPattern().IsMatch(s[(hash + 1)..]) && IsMapPath(s[..hash]))
            return Path.GetFileName(s[..hash].TrimEnd('\\', '/')) + " → kafelek";
        return Path.GetFileName(s.TrimEnd('\\', '/'));
    }
}

/// <summary>Kafelek innej mapy, który linkuje do tej mapy (albo do jej kafelka).</summary>
public sealed record Backlink(string MapPath, string NodeId, string NodeText, string? TargetNodeId);

/// <summary>Linki zwrotne: które mapy z tego samego folderu prowadzą do danej mapy („wejścia” w kampanii).</summary>
public static class Backlinks
{
    public const int MaxFiles = 300;
    public const long MaxFileSize = 8 * 1024 * 1024;

    public static List<Backlink> Find(string mapPath, CancellationToken cancel = default)
    {
        var result = new List<Backlink>();
        string full, dir;
        try
        {
            full = Path.GetFullPath(mapPath);
            dir = Path.GetDirectoryName(full)!;
        }
        catch
        {
            return result;
        }
        if (!Directory.Exists(dir)) return result;
        foreach (var file in Directory.EnumerateFiles(dir, "*" + MindMap.FileExtension).Take(MaxFiles))
        {
            cancel.ThrowIfCancellationRequested();
            if (LinkTargets.SamePath(file, full)) continue;
            try
            {
                if (new FileInfo(file).Length > MaxFileSize) continue;
                var map = MapSerializer.Load(file);
                foreach (var n in map.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(n.Link)) continue;
                    var t = LinkTargets.Resolve(n.Link, file);
                    if (t.Kind == LinkKind.Map && LinkTargets.SamePath(t.Target, full))
                        result.Add(new Backlink(file, n.Id, n.Text, t.NodeId));
                }
            }
            catch
            {
                // uszkodzona albo zablokowana mapa — pomijamy
            }
        }
        return result.OrderBy(b => Path.GetFileNameWithoutExtension(b.MapPath), StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
