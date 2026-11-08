using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapoMyslacz.Core.Model;

/// <summary>Zapis i odczyt pliku .mapo (JSON, UTF-8, czytelny dla człowieka i narzędzi).</summary>
public static class MapSerializer
{
    // polskie litery zapisujemy wprost (nie jako ł) — plik ma być czytelny w Notatniku
    private static readonly MapJson Pretty = new(new JsonSerializerOptions(MapJson.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private static readonly MapJsonCompact Compact = new(new JsonSerializerOptions(MapJsonCompact.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>Do zapisu na dysk — z wcięciami.</summary>
    public static string Serialize(MindMap map) => JsonSerializer.Serialize(map, Pretty.MindMap);

    /// <summary>Zwarta postać — do historii cofania i porównań.</summary>
    public static string SerializeCompact(MindMap map) => JsonSerializer.Serialize(map, Compact.MindMap);

    /// <exception cref="InvalidDataException">Plik nie jest mapą MapoMyślacza.</exception>
    public static MindMap Deserialize(string json)
    {
        MindMap? map;
        try
        {
            map = JsonSerializer.Deserialize(json, MapJson.Default.MindMap);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Plik nie jest poprawną mapą (błąd w JSON: {ex.Message}).", ex);
        }
        if (map is null) throw new InvalidDataException("Plik jest pusty.");
        if (!string.IsNullOrEmpty(map.Format) && !string.Equals(map.Format, MindMap.FormatName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"To nie jest mapa MapoMyślacza (format „{map.Format}”).");
        map.Format = MindMap.FormatName;
        MapOps.Repair(map);
        return map;
    }

    public static MindMap Load(string path) => Deserialize(File.ReadAllText(path, Encoding.UTF8));

    /// <summary>Zapis „bezpieczny”: najpierw do pliku tymczasowego, potem podmiana — awaria w trakcie nie niszczy mapy.</summary>
    public static void Save(MindMap map, string path)
    {
        map.Format = MindMap.FormatName;
        map.FormatVersion = MindMap.CurrentFormatVersion;
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Environment.ProcessId}.tmp");
        File.WriteAllText(temp, Serialize(map), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        try
        {
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    public static string SerializeFragment(MapFragment fragment) => JsonSerializer.Serialize(fragment, Compact.MapFragment);

    public static MapFragment? TryDeserializeFragment(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart().StartsWith('{')) return null;
        try
        {
            var f = JsonSerializer.Deserialize(text, MapJsonCompact.Default.MapFragment);
            return f is { Format: MapFragment.FormatName } ? f : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(MindMap))]
[JsonSerializable(typeof(MapFragment))]
internal partial class MapJson : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    WriteIndented = false)]
[JsonSerializable(typeof(MindMap))]
[JsonSerializable(typeof(MapFragment))]
internal partial class MapJsonCompact : JsonSerializerContext
{
}
