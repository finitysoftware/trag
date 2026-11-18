using Trag.Domain.Presentation;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

public sealed class LayoutStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-diff-settings-" + Guid.NewGuid().ToString("N"));
    private readonly string path;

    public LayoutStoreTests()
    {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "diff.json");
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public async Task WhenNothingWasSaved_ShouldLoadTheDefaultLayout()
    {
        var store = new LayoutStore(path);
        Assert.Equal(new SavedLayout(), await store.LoadLayoutAsync(default));
    }

    [Fact]
    public async Task WhenALayoutIsSaved_ShouldLoadItBackInAnotherStore()
    {
        var store = new LayoutStore(path);
        await store.LoadLayoutAsync(default);
        var desired = new SavedLayout(1, 0.62, 0.3, 2, 1, 0);
        await store.SaveLayoutAsync(desired, default);
        Assert.Equal(desired, await new LayoutStore(path).LoadLayoutAsync(default));
    }

    // Nothing on the way here, saves and loads alike, left a temporary file behind.
    [Fact]
    public async Task WhenTheFileIsOversized_ShouldRefuseToLoadIt()
    {
        var store = await SavedAsync();
        await File.WriteAllTextAsync(path, """{"Mode":9,"SplitRatio":10,"FilesRatio":-8}""");
        await store.LoadLayoutAsync(default);
        await File.WriteAllTextAsync(path, new string(' ', 4097));
        await Assert.ThrowsAsync<IOException>(() => store.LoadLayoutAsync(default));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // Loaded once while nothing was saved, then saved and loaded back by another store.
    private async Task<LayoutStore> SavedAsync()
    {
        var store = new LayoutStore(path);
        await store.LoadLayoutAsync(default);
        await store.SaveLayoutAsync(new SavedLayout(1, 0.62, 0.3, 2, 1, 0), default);
        await new LayoutStore(path).LoadLayoutAsync(default);
        return store;
    }
}
