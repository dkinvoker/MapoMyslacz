namespace MapoMyslacz.Core.Model;

/// <summary>Grupa znaczników — w grupach wyłącznych (priorytet, postęp) kafelek ma najwyżej jeden.</summary>
public enum MarkerGroup { Priority, Progress, Symbol }

/// <summary>Znacznik: <paramref name="Id"/> zapisywane w pliku, <paramref name="Glyph"/> — emoji albo cyfra rysowana na kółku.</summary>
public sealed record Marker(string Id, string Glyph, string Name, MarkerGroup Group);

/// <summary>
/// Katalog znaczników (ikon) przypinanych do kafelków. Priorytety i postęp są rysowane przez program (kolorowe kółka),
/// pozostałe to emoji — w pliku zapisuje się samo emoji, więc można też wpisać dowolne inne.
/// </summary>
public static class Markers
{
    public static readonly IReadOnlyList<Marker> Priorities =
    [
        new("priority-1", "1", "Priorytet 1", MarkerGroup.Priority),
        new("priority-2", "2", "Priorytet 2", MarkerGroup.Priority),
        new("priority-3", "3", "Priorytet 3", MarkerGroup.Priority),
        new("priority-4", "4", "Priorytet 4", MarkerGroup.Priority),
        new("priority-5", "5", "Priorytet 5", MarkerGroup.Priority),
    ];

    public static readonly IReadOnlyList<Marker> Progress =
    [
        new("progress-0", "0%", "Do zrobienia", MarkerGroup.Progress),
        new("progress-25", "25%", "Postęp 25%", MarkerGroup.Progress),
        new("progress-50", "50%", "Postęp 50%", MarkerGroup.Progress),
        new("progress-75", "75%", "Postęp 75%", MarkerGroup.Progress),
        new("progress-100", "100%", "Zrobione", MarkerGroup.Progress),
    ];

    public static readonly IReadOnlyList<Marker> Symbols =
    [
        S("✅", "Gotowe"), S("❌", "Odrzucone"), S("⚠️", "Uwaga"), S("❓", "Pytanie"), S("❗", "Ważne"), S("⭐", "Gwiazdka"),
        S("❤️", "Serce"), S("💡", "Pomysł"), S("📌", "Przypięte"), S("🚩", "Flaga"), S("🔒", "Zamknięte"), S("🗝️", "Klucz"),
        S("📅", "Termin"), S("👥", "Ludzie"), S("💰", "Pieniądze"), S("📜", "Zwój"), S("🗡️", "Miecz"), S("🛡️", "Tarcza"),
        S("🏰", "Zamek"), S("🏠", "Dom"), S("⛪", "Świątynia"), S("🌲", "Las"), S("👑", "Korona"), S("💀", "Czaszka"),
        S("🔥", "Ogień"), S("🧙", "Mag"), S("🐉", "Smok"), S("🧪", "Mikstura"), S("🎲", "Kości"), S("🗺️", "Mapa"),
    ];

    private static Marker S(string emoji, string name) => new(emoji, emoji, name, MarkerGroup.Symbol);

    public static IEnumerable<Marker> All => Priorities.Concat(Progress).Concat(Symbols);

    private static readonly Dictionary<string, Marker> ById = All.ToDictionary(m => m.Id);

    /// <summary>Znany znacznik albo — dla dowolnego tekstu (np. emoji spoza listy) — znacznik-symbol z tym tekstem.</summary>
    public static Marker Find(string id) =>
        ById.TryGetValue(id, out var m) ? m : new Marker(id, id, id, MarkerGroup.Symbol);

    /// <summary>Czy znacznik rysuje program (kółko z cyfrą / wykres postępu), a nie czcionka z emoji.</summary>
    public static bool IsDrawn(string id) => ById.TryGetValue(id, out var m) && m.Group != MarkerGroup.Symbol;

    /// <summary>Wartość postępu 0–100 dla „progress-NN”.</summary>
    public static int ProgressValue(string id) =>
        id.StartsWith("progress-", StringComparison.Ordinal) && int.TryParse(id["progress-".Length..], out var v) ? Math.Clamp(v, 0, 100) : 0;

    /// <summary>Numer priorytetu 1–5 dla „priority-N”.</summary>
    public static int PriorityValue(string id) =>
        id.StartsWith("priority-", StringComparison.Ordinal) && int.TryParse(id["priority-".Length..], out var v) ? v : 0;

    /// <summary>Kolory kółek priorytetów 1–5 (od najpilniejszego).</summary>
    public static readonly Rgba[] PriorityColors =
    [
        Rgba.Parse("#E5484D", Rgba.Black), Rgba.Parse("#F76B15", Rgba.Black), Rgba.Parse("#E2A400", Rgba.Black),
        Rgba.Parse("#0090FF", Rgba.Black), Rgba.Parse("#8A8794", Rgba.Black),
    ];

    public static bool Has(Node node, string id) => node.Icons?.Contains(id) == true;

    /// <summary>
    /// Włącza/wyłącza znacznik. W grupie wyłącznej nowy zastępuje poprzedni. Kolejność: priorytet, postęp, symbole.
    /// </summary>
    public static void Toggle(Node node, string id) => Set(node, id, !Has(node, id));

    public static void Set(Node node, string id, bool on)
    {
        var list = node.Icons?.ToList() ?? [];
        var marker = Find(id);
        if (on)
        {
            if (marker.Group != MarkerGroup.Symbol) list.RemoveAll(x => Find(x).Group == marker.Group);
            if (!list.Contains(id)) list.Add(id);
        }
        else
        {
            list.Remove(id);
        }
        node.Icons = list.Count == 0 ? null : Sorted(list);
    }

    public static List<string> Sorted(IEnumerable<string> ids) =>
        ids.Distinct().Select((id, i) => (id, i)).OrderBy(x => (int)Find(x.id).Group).ThenBy(x => x.i).Select(x => x.id).ToList();

    /// <summary>Znaczniki z XMind (markerId) → nasze. Nieznane są pomijane.</summary>
    public static string? FromXmind(string? markerId) => markerId switch
    {
        null => null,
        _ when markerId.StartsWith("priority-", StringComparison.Ordinal) =>
            int.TryParse(markerId[9..], out var p) ? $"priority-{Math.Clamp(p, 1, 5)}" : null,
        "task-start" => "progress-0",
        "task-oct" or "task-quarter" => "progress-25",
        "task-3oct" or "task-half" => "progress-50",
        "task-5oct" or "task-3quar" or "task-7oct" => "progress-75",
        "task-done" => "progress-100",
        _ when markerId.StartsWith("flag-", StringComparison.Ordinal) => "🚩",
        _ when markerId.StartsWith("star-", StringComparison.Ordinal) => "⭐",
        _ when markerId.StartsWith("smiley-", StringComparison.Ordinal) => "🙂",
        "symbol-question" => "❓",
        "symbol-exclam" => "❗",
        "symbol-wrong" or "c_simbol-wrong" => "❌",
        "symbol-right" or "c_simbol-right" => "✅",
        "symbol-idea" => "💡",
        "symbol-attention" => "⚠️",
        "symbol-lock" => "🔒",
        "people-blue" or "people-red" or "people-green" or "people-yellow" => "👥",
        _ => null,
    };
}
