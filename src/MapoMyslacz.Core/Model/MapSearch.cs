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

    /// <summary>Wyniki ze wszystkich arkuszy pliku — arkusz po arkuszu, w każdym w kolejności czytania mapy.</summary>
    public static List<(int Sheet, Node Node)> FindInSheets(IReadOnlyList<MindMap> sheets, string? query)
    {
        var result = new List<(int, Node)>();
        for (int i = 0; i < sheets.Count; i++)
            foreach (var n in Find(sheets[i], query)) result.Add((i, n));
        return result;
    }

    // ------------------------------------------------------------------ zamiana

    /// <summary>
    /// Zamienia każde wystąpienie <paramref name="phrase"/> w tekście — tak jak szuka wyszukiwarka: bez wielkości liter
    /// i polskich znaków („zolw” trafi w „Żółw”), odstępy (też nowe wiersze) liczą się jak jedna spacja. Zmienia się
    /// tylko znaleziony fragment; reszta tekstu zostaje co do znaku.
    /// </summary>
    public static string Replace(string text, string phrase, string replacement, out int count)
    {
        count = 0;
        var needle = CollapseSpaces(Normalize(phrase)).Trim();
        if (needle.Length == 0 || string.IsNullOrEmpty(text)) return text;

        // tekst znormalizowany znak po znaku, z zapamiętanym zakresem oryginału każdego znaku
        var norm = new StringBuilder(text.Length);
        var from = new List<int>(text.Length);
        var to = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int len = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            var piece = len == 2 ? text.Substring(i, 2) : Normalize(text[i].ToString());
            foreach (var c in piece)
            {
                if (c == ' ' && norm.Length > 0 && norm[^1] == ' ')
                {
                    to[^1] = i + len; // kolejny odstęp — ta sama spacja, dłuższy zakres
                    continue;
                }
                norm.Append(c);
                from.Add(i);
                to.Add(i + len);
            }
            i += len - 1;
        }

        var hay = norm.ToString();
        var sb = new StringBuilder(text.Length);
        int copied = 0, at = 0;
        while ((at = hay.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            int start = from[at], end = to[at + needle.Length - 1];
            sb.Append(text, copied, start - copied).Append(replacement);
            copied = end;
            at += needle.Length;
            count++;
        }
        if (count == 0) return text;
        sb.Append(text, copied, text.Length - copied);
        return sb.ToString();
    }

    /// <summary>Zamiana w tekście i notatce kafelka. Zwraca liczbę zamienionych wystąpień.</summary>
    public static int Replace(Node node, string phrase, string replacement)
    {
        node.Text = Replace(node.Text, phrase, replacement, out int inText);
        int inNote = 0;
        if (node.Note is { } note)
        {
            var replaced = Replace(note, phrase, replacement, out inNote);
            node.Note = string.IsNullOrWhiteSpace(replaced) ? null : replaced;
        }
        return inText + inNote;
    }

    private static string CollapseSpaces(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (c != ' ' || sb.Length == 0 || sb[^1] != ' ') sb.Append(c);
        return sb.ToString();
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
