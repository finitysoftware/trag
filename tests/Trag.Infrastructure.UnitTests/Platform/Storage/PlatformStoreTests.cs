using Trag.Domain.Catalog;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

public sealed class PlatformStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "trag-stores-" + Guid.NewGuid().ToString("N"));
    public PlatformStoreTests() => Directory.CreateDirectory(directory);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private string File(string name) => Path.Combine(directory, name);

    [Fact]
    public async Task WhenTheLastRepositoryIsSavedConcurrently_ShouldKeepOneWholeValueAndLeaveNoTemporaryFiles()
    {
        var store = new LastRepositoryStore(File("settings.json"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveAsync("repo-" + i, CancellationToken.None)));
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.NotNull(loaded); Assert.StartsWith("repo-", loaded);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task WhenABlankLastRepositoryIsSaved_ShouldLoadNone()
    {
        var store = await SavedConcurrentlyAsync();
        await store.SaveAsync("   ", CancellationToken.None);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenASaveOfTheLastRepositoryIsCancelled_ShouldLeaveNeitherTheFileNorATemporaryFile()
    {
        var path = File(Path.Combine("nested", "settings.json"));
        var store = new LastRepositoryStore(path);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync("repo", cancelled.Token));
        Assert.False(Directory.Exists(File("nested")) && Directory.GetFiles(File("nested")).Length != 0);
    }

    [Fact]
    public async Task WhenASaveFollowsACancelledOne_ShouldSaveTheLastRepository()
    {
        var path = File(Path.Combine("nested", "settings.json"));
        var store = await AfterACancelledSaveAsync(path);
        await store.SaveAsync("repo", CancellationToken.None);
        Assert.Equal("repo", await new LastRepositoryStore(path).LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheLayoutIsSavedConcurrently_ShouldKeepOneWholeValueAndLeaveNoTemporaryFiles()
    {
        var store = new LayoutStore(File("diff.json"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveLayoutAsync(new(i % 2, 0.1 + i * 0.04), CancellationToken.None)));
        var loaded = await store.LoadLayoutAsync(CancellationToken.None);
        Assert.InRange(loaded.SplitRatio, 0.1, 0.9);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task WhenTheMemoryStoreIsNew_ShouldHaveNoLastRepository()
    {
        var store = new MemoryLastRepositoryStore();
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheMemoryStoreSavesARepository_ShouldLoadItBack()
    {
        var store = new MemoryLastRepositoryStore();
        await store.LoadAsync(CancellationToken.None);
        await store.SaveAsync("repo", CancellationToken.None);
        Assert.Equal("repo", await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheMemoryStoreSavesNoRepository_ShouldForgetTheOneItHad()
    {
        var store = await RememberedInMemoryAsync();
        await store.SaveAsync(null, CancellationToken.None);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheCatalogIsSavedWithTooManyEntries_ShouldKeepAtMostTheLimitAndSanitizeTheFields()
    {
        var store = new RepositoryCatalogStore(File("repositories.json"));
        var when = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 600).Select(i => new RepositoryEntry(File("r" + i), "r" + i, when)).ToList();
        entries.Insert(0, new(File("ctl"), "control", when));
        entries.Insert(0, new(File("long"), "  ", when, RemoteUrl: new string('u', 2049), Branch: new string('b', 513)));
        await store.UpdateAsync(_ => entries, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(RepositoryEntry.MaximumListed, loaded.Count);
        Assert.Equal("long", loaded[0].Name); Assert.Null(loaded[0].RemoteUrl); Assert.Null(loaded[0].Branch);
        Assert.DoesNotContain(loaded, entry => entry.Name == "control");
    }

    [Fact]
    public async Task WhenTheCatalogIsSavedConcurrently_ShouldKeepOneWholeListAndLeaveNoTemporaryFiles()
    {
        var store = new RepositoryCatalogStore(File("repositories.json"));
        var when = DateTimeOffset.UtcNow;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.UpdateAsync(_ => [new(File("r" + i), "r" + i, when)], CancellationToken.None)));
        Assert.Single(await store.LoadAsync(CancellationToken.None));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    // Twenty saves at once, and the one that won loaded back.
    private async Task<LastRepositoryStore> SavedConcurrentlyAsync()
    {
        var store = new LastRepositoryStore(File("settings.json"));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SaveAsync("repo-" + i, CancellationToken.None)));
        await store.LoadAsync(CancellationToken.None);
        return store;
    }

    // A store whose first save into a folder not made yet was cancelled before it began.
    private static async Task<LastRepositoryStore> AfterACancelledSaveAsync(string path)
    {
        var store = new LastRepositoryStore(path);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync("repo", cancelled.Token));
        return store;
    }

    // Read while empty, then given a repository and read again.
    private static async Task<MemoryLastRepositoryStore> RememberedInMemoryAsync()
    {
        var store = new MemoryLastRepositoryStore();
        await store.LoadAsync(CancellationToken.None);
        await store.SaveAsync("repo", CancellationToken.None);
        await store.LoadAsync(CancellationToken.None);
        return store;
    }
}
