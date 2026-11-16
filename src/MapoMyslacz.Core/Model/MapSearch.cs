using System.Globalization;
using System.Text;

namespace MapoMyslacz.Core.Model;

/// <summary>
/// Wyszukiwanie kafelków po tekście i notatce. Bez rozróżniania wielkości liter i polskich znaków
/// („zolw” znajdzie „Żółw”); kilka słów — każde musi wystąpić (w dowolnej kolejności).
/// </summary>
public static class MapSearch
{
    /// <summary>Tekst do porównań: małe litery, bez znaków diakrytycznych (także „ł” → „l”).</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { 'ł' => 'l', 'ß' => 's', '\n' or '\r' or '\t' => ' ', _ => c });
        }
        return sb.ToString();
    }

    /// <summary>Słowa zapytania (znormalizowane). Puste zapytanie — brak słów.</summary>
    public static string[] Terms(string? query) =>
        Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static bool Matches(Node node, string[] terms)
    {
        if (terms.Length == 0) return false;
        var text = Normalize(node.Text);
        var note = Normalize(node.Note);
        foreach (var t in terms)
            if (!text.Contains(t, StringComparison.Ordinal) && !note.Contains(t, StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>
    /// Pasujące kafelki w kolejności czytania mapy: tematy od góry, a w każdym najpierw kafelek, potem jego gałęzie
    /// od góry do dołu — Enter w wyszukiwarce idzie po mapie „gałąź po gałęzi”.
    /// </summary>
    public static List<Node> Find(MindMap map, string? query)
    {
        var terms = Terms(query);
        var result = new List<Node>();
        if (terms.Length == 0) return result;
        var idx = new MapIndex(map);
        var seen = new HashSet<string>();
        void Visit(Node n)
        {
            if (!seen.Add(n.Id)) return;
            if (Matches(n, terms)) result.Add(n);
            foreach (var c in idx.ChildrenOf(n.Id).OrderBy(c => c.Y).ThenBy(c => c.X)) Visit(c);
        }
        foreach (var root in map.Nodes.Where(n => idx.ParentOf(n) is null).OrderBy(n => n.Y).ThenBy(n => n.X)) Visit(root);
        return result;
    }

    /// <summary>Przodkowie kafelka, którzy są zwinięci (trzeba ich rozwinąć, żeby kafelek było widać).</summary>
    public static List<Node> CollapsedAncestors(MapIndex idx, Node node)
    {
        var result = new List<Node>();
        var cur = idx.ParentOf(node);
        int guard = 0;
        while (cur is not null && guard++ < 100_000)
        {
            if (cur.Collapsed) result.Add(cur);
            cur = idx.ParentOf(cur);
        }
        return result;
    }
}
