using System.IO.Compression;
using MapoMyslacz.Launcher;

namespace MapoMyslacz.Tests;

/// <summary>Starter jednoplikowy: rozpakowanie aplikacji (raz na wersję, sprzątanie starych) i skrypt odinstalowania.</summary>
public class LauncherTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("mapo-launcher");

    public void Dispose() => _root.Delete(recursive: true);

    private static Func<Stream> Payload(string marker) => () =>
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry(AppExtractor.AppExe).Open())) w.Write("MZ" + marker);
            using (var w = new StreamWriter(zip.CreateEntry("Assets/AppIcon.png").Open())) w.Write("png");
        }
        ms.Position = 0;
        return ms;
    };

    [Fact]
    public void Extract_OncePerVersion()
    {
        int opened = 0;
        var payload = Payload("v1");
        Stream Open() { opened++; return payload(); }

        var dir = AppExtractor.EnsureExtracted(Open, "ABCDEF0123456789", _root.FullName);
        Assert.Equal(Path.Combine(_root.FullName, "abcdef012345"), dir);
        Assert.True(File.Exists(Path.Combine(dir, "MapoMyslacz.exe")));
        Assert.True(File.Exists(Path.Combine(dir, "Assets", "AppIcon.png")));
        Assert.True(AppExtractor.IsComplete(dir, "abcdef012345"));

        AppExtractor.EnsureExtracted(Open, "abcdef0123456789", _root.FullName);
        Assert.Equal(1, opened); // druga próba nie rozpakowuje ponownie
        Assert.Single(Directory.GetDirectories(_root.FullName)); // bez folderów tymczasowych
    }

    [Fact]
    public void Extract_RepairsIncompleteFolder()
    {
        var target = Path.Combine(_root.FullName, "111111111111");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "MapoMyslacz.exe"), "przerwane rozpakowanie"); // bez znacznika .complete
        var dir = AppExtractor.EnsureExtracted(Payload("ok"), "111111111111", _root.FullName);
        Assert.Equal("MZok", File.ReadAllText(Path.Combine(dir, "MapoMyslacz.exe")));
    }

    [Fact]
    public void Extract_NewVersion_RemovesOldOnes()
    {
        AppExtractor.EnsureExtracted(Payload("v1"), "aaaaaaaaaaaa", _root.FullName);
        var dir2 = AppExtractor.EnsureExtracted(Payload("v2"), "bbbbbbbbbbbb", _root.FullName);
        Assert.Equal([dir2], Directory.GetDirectories(_root.FullName));
    }

    [Fact]
    public void Extract_KeepsOldVersionThatIsInUse()
    {
        var old = AppExtractor.EnsureExtracted(Payload("v1"), "aaaaaaaaaaaa", _root.FullName);
        using (File.Open(Path.Combine(old, "MapoMyslacz.exe"), FileMode.Open, FileAccess.Read, FileShare.Read)) // „działa”
        {
            AppExtractor.EnsureExtracted(Payload("v2"), "bbbbbbbbbbbb", _root.FullName);
            Assert.True(File.Exists(Path.Combine(old, "MapoMyslacz.exe")), "działająca wersja nie może stracić plików");
        }
        AppExtractor.EnsureExtracted(Payload("v2"), "bbbbbbbbbbbb", _root.FullName);
        AppExtractor.CleanupOldVersions(_root.FullName, "bbbbbbbbbbbb");
        Assert.False(Directory.Exists(old)); // po zamknięciu — sprzątnięta
    }

    [Fact]
    public void Extract_RejectsBadId() =>
        Assert.Throws<InvalidDataException>(() => AppExtractor.EnsureExtracted(Payload("x"), "abc", _root.FullName));

    [Fact]
    public void UninstallScript_Content()
    {
        var s = UninstallScript.Build("MapoMyslacz.exe");
        Assert.DoesNotContain("\n", s.Replace("\r\n", "")); // tylko CRLF — cmd.exe źle czyta same LF
        Assert.StartsWith("@echo off", s);
        Assert.Contains("chcp 65001", s);
        Assert.Contains("set \"EXE=%~dp0MapoMyslacz.exe\"", s);
        Assert.Contains(@"HKCU\Software\Classes\MapoMyslacz.Mapa", s);
        Assert.Contains(@"%LOCALAPPDATA%\MapoMyslacz", s);
        Assert.Contains("(goto) 2>nul & del \"%~f0\"", s); // skrypt usuwa sam siebie
        Assert.Contains("[T/N]", s); // pyta przed usunięciem
    }

    [Fact]
    public void UninstallScript_RenamedExe_AndPercentEscaped()
    {
        Assert.Contains("%~dp0Mapy 100%%.exe", UninstallScript.Build("Mapy 100%.exe"));
    }

    [Fact]
    public void UninstallScript_WrittenOnce_NextToExe()
    {
        var exe = Path.Combine(_root.FullName, "MapoMyslacz.exe");
        File.WriteAllText(exe, "MZ");
        Assert.True(UninstallScript.TryWrite(exe));
        var script = Path.Combine(_root.FullName, UninstallScript.FileName);
        Assert.True(File.Exists(script));
        Assert.False(UninstallScript.TryWrite(exe)); // bez zmian — nie nadpisuje
        Assert.NotEqual(0xEF, File.ReadAllBytes(script)[0]); // bez BOM (cmd by go wypisał)
        Assert.False(UninstallScript.TryWrite(null));
    }
}
