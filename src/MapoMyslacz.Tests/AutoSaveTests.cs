using MapoMyslacz.Core.Editing;

namespace MapoMyslacz.Tests;

/// <summary>Autozapis i odzyskiwanie po awarii.</summary>
public sealed class AutoSaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mapo-autosave-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static bool Dead(int pid, long ticks) => false;
    private static bool Alive(int pid, long ticks) => true;

    [Fact]
    public void Write_ThenOrphan_RestoresMapAndOrigin()
    {
        var s = new MapSession(TestData.Tree(), @"C:\mapy\Sesja 1.mapo");
        s.Edit(m => m.Get("a").Text = "Zmienione");
        var store = new AutoSaveStore(_dir);
        store.Write(s, new ViewState { X = 10, Y = 20, Zoom = 0.5 }, 1234, 99);

        Assert.True(store.Exists);
        Assert.True(s.IsDirty); // kopia nie „zapisuje” mapy
        Assert.Equal(@"C:\mapy\Sesja 1.mapo", s.FilePath);

        var orphan = Assert.Single(AutoSaveStore.Orphans(_dir, Dead));
        Assert.Equal(@"C:\mapy\Sesja 1.mapo", orphan.OriginalPath);
        Assert.Equal("Sesja 1", orphan.DisplayName);
        Assert.Equal(1234, orphan.ProcessId);
        var map = AutoSaveStore.Load(orphan);
        Assert.Equal("Zmienione", map.Get("a").Text);
        Assert.Equal(0.5, map.View!.Zoom);
    }

    [Fact]
    public void RunningWindow_IsNotAnOrphan()
    {
        var store = new AutoSaveStore(_dir);
        store.Write(new MapSession(TestData.Tree()), null, 1, 1);
        Assert.Empty(AutoSaveStore.Orphans(_dir, Alive));
        Assert.Single(AutoSaveStore.List(_dir));
    }

    [Fact]
    public void Delete_RemovesBothFiles()
    {
        var store = new AutoSaveStore(_dir);
        store.Write(new MapSession(TestData.Tree()), null, 1, 1);
        Assert.Equal(2, Directory.GetFiles(_dir).Length);
        store.Delete();
        Assert.False(store.Exists);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void UnsavedMap_UsesTitle_AndMetaWithoutMapIsIgnored()
    {
        var map = TestData.Tree();
        map.Title = "Kampania";
        new AutoSaveStore(_dir, "a").Write(new MapSession(map), null, 1, 1);
        File.WriteAllText(Path.Combine(_dir, "sierota.json"), "{}"); // opis bez mapy
        File.WriteAllText(Path.Combine(_dir, "zepsuty.json"), "{ nie json");

        var info = Assert.Single(AutoSaveStore.List(_dir));
        Assert.Null(info.OriginalPath);
        Assert.Equal("Kampania", info.DisplayName);
        Assert.False(File.Exists(Path.Combine(_dir, "sierota.json")));
    }

    [Fact]
    public void Prune_RemovesOnlyOldOrphans()
    {
        new AutoSaveStore(_dir, "nowy").Write(new MapSession(TestData.Tree()), null, 1, 1);
        new AutoSaveStore(_dir, "stary").Write(new MapSession(TestData.Tree()), null, 1, 1);
        var meta = Path.Combine(_dir, "stary.json");
        File.WriteAllText(meta, File.ReadAllText(meta).Replace(DateTime.Now.Year.ToString(), (DateTime.Now.Year - 1).ToString()));

        Assert.Equal(1, AutoSaveStore.Prune(_dir, TimeSpan.FromDays(30), Dead));
        Assert.Equal("nowy", Path.GetFileNameWithoutExtension(Assert.Single(AutoSaveStore.List(_dir)).MetaPath));
    }
}
