using System.Text.Json.Serialization;

namespace MapoMyslacz.Core.Model;

/// <summary>
/// Mapa myśli = kafelki (<see cref="Node"/>) na nieskończonym płótnie + strzałki między nimi (<see cref="Link"/>).
/// Kafelek może mieć rodzica — wtedy tworzy drzewo (gałęzie rysowane są automatycznie); kafelki bez rodzica
/// to tematy główne albo swobodne notatki. Zapisywana jako JSON w pliku <c>.mapo</c>.
/// </summary>
public sealed class MindMap
{
    public const string FormatName = "MapoMyslacz";
    public const int CurrentFormatVersion = 1;
    public const string FileExtension = ".mapo";

    /// <summary>Znacznik formatu — pozwala rozpoznać plik nawet po zmianie rozszerzenia.</summary>
    public string Format { get; set; } = FormatName;

    /// <summary>Wersja formatu (rośnie tylko przy niekompatybilnych zmianach).</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string? Title { get; set; }

    /// <summary>Kolor tła płótna (#RRGGBB). Brak = domyślny biały.</summary>
    public string? Background { get; set; }

    /// <summary>Zapamiętany widok (środek ekranu w układzie mapy i powiększenie).</summary>
    public ViewState? View { get; set; }

    public List<Node> Nodes { get; set; } = [];

    public List<Link> Links { get; set; } = [];
}

public sealed class ViewState
{
    /// <summary>Punkt mapy widoczny na środku okna.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

/// <summary>Kafelek. Pozycja (<see cref="X"/>, <see cref="Y"/>) to jego ŚRODEK — rozmiar wynika z tekstu i stylu.</summary>
public sealed class Node
{
    public string Id { get; set; } = Ids.New();
    public string Text { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Id rodzica w drzewie (null = kafelek główny/swobodny).</summary>
    public string? Parent { get; set; }

    /// <summary>Styl — tylko to, co różni się od domyślnego.</summary>
    public NodeStyle? Style { get; set; }

    /// <summary>Dłuższa notatka do kafelka (widoczna w panelu właściwości, na kafelku jest znaczek).</summary>
    public string? Note { get; set; }

    /// <summary>Zwinięta gałąź — dzieci są ukryte.</summary>
    public bool Collapsed { get; set; }

    /// <summary>Własna maksymalna szerokość tekstu (zawijanie). Brak = zależna od rozmiaru czcionki.</summary>
    public double? Width { get; set; }
}

/// <summary>
/// Styl kafelka. Każda właściwość jest opcjonalna: null oznacza „domyślnie” (patrz <see cref="Styles"/>),
/// dzięki temu pliki są krótkie, a zmiana domyślnego wyglądu w przyszłych wersjach obejmie stare mapy.
/// </summary>
public sealed class NodeStyle
{
    public NodeShape? Shape { get; set; }
    public string? Fill { get; set; }
    public string? TextColor { get; set; }
    public string? Border { get; set; }
    public double? BorderWidth { get; set; }
    public LinePattern? BorderPattern { get; set; }
    public double? FontSize { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Strike { get; set; }
    public TextAlign? Align { get; set; }

    /// <summary>Kolor gałęzi prowadzących do dzieci (dziedziczony w dół drzewa).</summary>
    public string? BranchColor { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Shape is null && Fill is null && TextColor is null && Border is null && BorderWidth is null &&
        BorderPattern is null && FontSize is null && Bold is null && Italic is null && Strike is null &&
        Align is null && BranchColor is null;

    public NodeStyle Clone() => (NodeStyle)MemberwiseClone();

    /// <summary>Nakłada niepuste pola <paramref name="other"/> na ten styl.</summary>
    public void Apply(NodeStyle other)
    {
        Shape = other.Shape ?? Shape;
        Fill = other.Fill ?? Fill;
        TextColor = other.TextColor ?? TextColor;
        Border = other.Border ?? Border;
        BorderWidth = other.BorderWidth ?? BorderWidth;
        BorderPattern = other.BorderPattern ?? BorderPattern;
        FontSize = other.FontSize ?? FontSize;
        Bold = other.Bold ?? Bold;
        Italic = other.Italic ?? Italic;
        Strike = other.Strike ?? Strike;
        Align = other.Align ?? Align;
        BranchColor = other.BranchColor ?? BranchColor;
    }
}

/// <summary>
/// Strzałka (relacja) między dwoma dowolnymi kafelkami, z opcjonalnym podpisem.
/// Krzywiznę opisują punkty kontrolne <see cref="C1"/>/<see cref="C2"/> — przesunięcia względem środków
/// kafelków początkowego/końcowego, więc kształt strzałki „jedzie” razem z kafelkami.
/// </summary>
public sealed class Link
{
    public string Id { get; set; } = Ids.New();
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string? Label { get; set; }
    public LinkStyle? Style { get; set; }

    /// <summary>Punkt kontrolny przy początku (względem środka kafelka <see cref="From"/>). Brak = automatyczny.</summary>
    public Vec? C1 { get; set; }

    /// <summary>Punkt kontrolny przy końcu (względem środka kafelka <see cref="To"/>). Brak = automatyczny.</summary>
    public Vec? C2 { get; set; }

    /// <summary>Punkty pośrednie (współrzędne mapy), przez które przechodzi gładka krzywa — dla długich „objazdów”.</summary>
    public List<Vec>? Via { get; set; }
}

public sealed class LinkStyle
{
    public string? Color { get; set; }
    public double? Width { get; set; }
    public LinePattern? Pattern { get; set; }
    public bool? ArrowStart { get; set; }
    public bool? ArrowEnd { get; set; }
    public LinkCurve? Curve { get; set; }
    public double? FontSize { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Color is null && Width is null && Pattern is null && ArrowStart is null &&
                           ArrowEnd is null && Curve is null && FontSize is null;

    public LinkStyle Clone() => (LinkStyle)MemberwiseClone();
}

[JsonConverter(typeof(JsonStringEnumConverter<NodeShape>))]
public enum NodeShape
{
    [JsonStringEnumMemberName("rounded")] Rounded,
    [JsonStringEnumMemberName("rectangle")] Rectangle,
    [JsonStringEnumMemberName("pill")] Pill,
    [JsonStringEnumMemberName("ellipse")] Ellipse,
    [JsonStringEnumMemberName("diamond")] Diamond,
    [JsonStringEnumMemberName("hexagon")] Hexagon,
    /// <summary>Sam tekst, bez tła i ramki (np. tytuł mapy).</summary>
    [JsonStringEnumMemberName("none")] None,
}

[JsonConverter(typeof(JsonStringEnumConverter<LinePattern>))]
public enum LinePattern
{
    [JsonStringEnumMemberName("solid")] Solid,
    [JsonStringEnumMemberName("dashed")] Dashed,
    [JsonStringEnumMemberName("dotted")] Dotted,
}

[JsonConverter(typeof(JsonStringEnumConverter<TextAlign>))]
public enum TextAlign
{
    [JsonStringEnumMemberName("center")] Center,
    [JsonStringEnumMemberName("left")] Left,
    [JsonStringEnumMemberName("right")] Right,
}

[JsonConverter(typeof(JsonStringEnumConverter<LinkCurve>))]
public enum LinkCurve
{
    [JsonStringEnumMemberName("curved")] Curved,
    [JsonStringEnumMemberName("straight")] Straight,
}

/// <summary>Krótkie, losowe identyfikatory (10 znaków) — czytelniejsze w pliku niż GUID.</summary>
public static class Ids
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    public static string New()
    {
        Span<char> s = stackalloc char[10];
        for (int i = 0; i < s.Length; i++) s[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
        return new string(s);
    }
}
