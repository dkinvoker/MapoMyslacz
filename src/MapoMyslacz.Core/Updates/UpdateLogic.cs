using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MapoMyslacz.Core.Updates;

/// <summary>Nowa wersja do pobrania (z wydania na GitHubie).</summary>
/// <param name="Sha256">Suma kontrolna pliku (hex) — bez niej nie instalujemy automatycznie.</param>
public sealed record ReleaseInfo(Version Version, string Tag, string Title, string Notes, string PageUrl,
    string? AssetUrl, long AssetSize, string? Sha256)
{
    /// <summary>Numer do pokazania, np. "1.2" albo "1.2.1".</summary>
    public string DisplayVersion => UpdateLogic.Display(Version);

    public bool CanInstall => AssetUrl is not null && Sha256 is not null;
}

/// <summary>
/// Logika aktualizacji niezależna od sieci i UI (łatwa do testowania): odczyt odpowiedzi API GitHuba
/// (releases/latest), porównanie wersji, wybór pliku exe i jego sumy SHA-256.
/// </summary>
public static partial class UpdateLogic
{
    /// <summary>
    /// Repozytorium z wydaniami (właściciel/nazwa). GitHub zamienił „ś” z nazwy „MapoMyślacz” na „-”.
    /// Po zmianie nazwy repozytorium GitHub przekierowuje stary adres, więc wpis działa dalej.
    /// </summary>
    public const string Repository = "dkinvoker/MapoMy-lacz";

    /// <summary>Gdzie jeszcze szukać wydań (nazwa używana w wersjach 1.0–1.1), gdy główne repozytorium zwróci 404.</summary>
    public static readonly IReadOnlyList<string> Repositories = [Repository, "dkinvoker/MapoMyslacz"];

    /// <summary>Plik wydania z jednoplikową aplikacją (x64). Wersja ARM64 ma przyrostek -arm64.</summary>
    public const string AssetName = "MapoMyslacz.exe";

    public static string LatestReleaseApi => LatestReleaseApiFor(Repository);
    public static string LatestReleaseApiFor(string repository) => $"https://api.github.com/repos/{repository}/releases/latest";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    [GeneratedRegex(@"(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex DottedVersionRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"(?i)sha-?256[^0-9a-f]{0,40}([0-9a-f]{64})")]
    private static partial Regex Sha256InTextRegex();

    /// <summary>„v1.10.0”, „1.10”, „MapoMyślacz 1.9.1” → 1.10.0 / 1.10.0 / 1.9.1 (zawsze trzy części).</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = DottedVersionRegex().Match(text);
        if (m.Success)
        {
            int Part(int i) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
            return new Version(Part(1), Part(2), Part(3));
        }
        var n = NumberRegex().Match(text);
        return n.Success && int.TryParse(n.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? new Version(major, 0, 0) : null;
    }

    /// <summary>Porównanie liczbowe (1.10 jest nowsza niż 1.9), brakujące części traktujemy jak zero.</summary>
    public static bool IsNewer(Version candidate, Version current) => Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    public static string Display(Version v) => v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";

    /// <summary>Nazwa pliku wydania dla architektury procesora.</summary>
    public static string AssetNameFor(bool arm64) => arm64 ? "MapoMyslacz-arm64.exe" : AssetName;

    /// <summary>
    /// SHA-256 pliku: GitHub sam podaje ją przy pliku wydania („digest”: „sha256:…”); zapasowo — linia
    /// „SHA-256: …” w opisie wydania.
    /// </summary>
    public static string? ParseSha256(string? digest, string? notes)
    {
        if (digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var hex = d[7..].Trim();
            if (hex.Length == 64 && hex.All(Uri.IsHexDigit)) return hex.ToLowerInvariant();
        }
        var m = notes is null ? null : Sha256InTextRegex().Match(notes);
        return m is { Success: true } ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>Odpowiedź API GitHuba → informacja o wydaniu (null dla szkiców, wersji testowych i błędnych danych).</summary>
    public static ReleaseInfo? FromGitHub(GitHubRelease? r, bool arm64 = false)
    {
        if (r is null || r.Draft || r.Prerelease) return null;
        var version = ParseVersion(r.TagName) ?? ParseVersion(r.Name);
        if (version is null) return null;
        var wanted = AssetNameFor(arm64);
        var asset = r.Assets.FirstOrDefault(a => string.Equals(a.Name, wanted, StringComparison.OrdinalIgnoreCase))
                    ?? (arm64 ? null : r.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                                                                    && !a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)));
        var notes = r.Body ?? "";
        return new ReleaseInfo(version, r.TagName ?? "", string.IsNullOrWhiteSpace(r.Name) ? $"MapoMyślacz {Display(version)}" : r.Name!,
            notes, string.IsNullOrWhiteSpace(r.HtmlUrl) ? ReleasesPage : r.HtmlUrl!,
            asset?.BrowserDownloadUrl, asset?.Size ?? 0, asset is null ? null : ParseSha256(asset.Digest, notes));
    }

    public static ReleaseInfo? Parse(string json, bool arm64 = false) =>
        FromGitHub(JsonSerializer.Deserialize(json, UpdateJson.Default.GitHubRelease), arm64);
}

/// <summary>Fragment odpowiedzi GitHub API: /repos/{owner}/{repo}/releases/latest.</summary>
public sealed class GitHubRelease
{
    public string? TagName { get; set; }
    public string? Name { get; set; }
    public string? Body { get; set; }
    public string? HtmlUrl { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
    public List<GitHubAsset> Assets { get; set; } = [];
}

public sealed class GitHubAsset
{
    public string Name { get; set; } = "";
    public string? BrowserDownloadUrl { get; set; }
    public long Size { get; set; }
    public string? Digest { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(GitHubRelease))]
internal partial class UpdateJson : JsonSerializerContext
{
}
