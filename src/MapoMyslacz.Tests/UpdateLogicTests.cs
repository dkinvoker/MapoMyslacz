using MapoMyslacz.Core.Updates;

namespace MapoMyslacz.Tests;

/// <summary>Aktualizacje: odczyt wydania z GitHuba, porównanie wersji, plik exe i jego suma SHA-256.</summary>
public class UpdateLogicTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string ReleaseJson(string tag = "v1.2.0", bool draft = false, bool pre = false, string digest = "sha256:" + Hash,
        string assetName = "MapoMyslacz.exe", string body = "Nowości") => $$"""
        {
          "tag_name": "{{tag}}", "name": "MapoMyślacz 1.2", "body": "{{body}}",
          "html_url": "https://github.com/dkinvoker/MapoMyslacz/releases/tag/{{tag}}",
          "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(pre ? "true" : "false")}},
          "assets": [
            { "name": "notatki.txt", "browser_download_url": "https://example.invalid/n.txt", "size": 10 },
            { "name": "MapoMyslacz-arm64.exe", "browser_download_url": "https://example.invalid/arm.exe", "size": 30000000, "digest": "sha256:{{new string('a', 64)}}" },
            { "name": "{{assetName}}", "browser_download_url": "https://example.invalid/m.exe", "size": 31000000, "digest": "{{digest}}" }
          ]
        }
        """;

    [Theory]
    [InlineData("v1.10.0", 1, 10, 0)]
    [InlineData("1.9.1", 1, 9, 1)]
    [InlineData("v2", 2, 0, 0)]
    [InlineData("MapoMyślacz 1.10", 1, 10, 0)]
    public void ParseVersion(string text, int major, int minor, int build) =>
        Assert.Equal(new Version(major, minor, build), UpdateLogic.ParseVersion(text));

    [Fact]
    public void ParseVersion_Invalid() => Assert.Null(UpdateLogic.ParseVersion("brak numeru"));

    [Fact]
    public void IsNewer_ComparesNumbers_NotText()
    {
        Assert.True(UpdateLogic.IsNewer(new Version(1, 10, 0), new Version(1, 9, 1)));
        Assert.True(UpdateLogic.IsNewer(new Version(1, 9, 2), new Version(1, 9, 1)));
        Assert.False(UpdateLogic.IsNewer(new Version(1, 9, 1), new Version(1, 9, 1)));
        Assert.False(UpdateLogic.IsNewer(new Version(1, 8, 0), new Version(1, 9, 1)));
        Assert.False(UpdateLogic.IsNewer(new Version(1, 9), new Version(1, 9, 0)));
    }

    [Fact]
    public void Display_HidesZeroPatch()
    {
        Assert.Equal("1.2", UpdateLogic.Display(new Version(1, 2, 0)));
        Assert.Equal("1.2.3", UpdateLogic.Display(new Version(1, 2, 3)));
    }

    [Fact]
    public void Parse_PicksX64ExeAndDigest()
    {
        var r = UpdateLogic.Parse(ReleaseJson())!;
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("https://example.invalid/m.exe", r.AssetUrl);
        Assert.Equal(31000000, r.AssetSize);
        Assert.Equal(Hash, r.Sha256);
        Assert.True(r.CanInstall);
        Assert.Equal("1.2", r.DisplayVersion);
    }

    [Fact]
    public void Parse_PicksArm64Asset()
    {
        var r = UpdateLogic.Parse(ReleaseJson(), arm64: true)!;
        Assert.Equal("https://example.invalid/arm.exe", r.AssetUrl);
    }

    [Fact]
    public void Parse_SkipsDraftsAndPrereleases()
    {
        Assert.Null(UpdateLogic.Parse(ReleaseJson(draft: true)));
        Assert.Null(UpdateLogic.Parse(ReleaseJson(pre: true)));
    }

    [Fact]
    public void Parse_HashFromNotes_WhenNoDigest()
    {
        var r = UpdateLogic.Parse(ReleaseJson(digest: "", body: $"SHA-256: {Hash.ToUpperInvariant()}"))!;
        Assert.Equal(Hash, r.Sha256);
    }

    [Fact]
    public void Parse_NoHash_CannotInstall()
    {
        var r = UpdateLogic.Parse(ReleaseJson(digest: ""))!;
        Assert.Null(r.Sha256);
        Assert.False(r.CanInstall);
    }

    [Fact]
    public void Parse_OtherExeName_StillFoundForX64()
    {
        var r = UpdateLogic.Parse(ReleaseJson(assetName: "MapoMyslacz-1.2.exe"))!;
        Assert.Equal("https://example.invalid/m.exe", r.AssetUrl);
    }
}
