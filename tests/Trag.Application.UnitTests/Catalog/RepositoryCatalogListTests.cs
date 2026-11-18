using Trag.Application.Catalog;
using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;

namespace Trag.Application.UnitTests.Catalog;

public sealed class RepositoryCatalogListTests
{
    // The canceled read ignored its cancellation and finished last.
    [Fact]
    public async Task WhenACanceledReadFinishesAfterANewerLoad_ShouldNotReplaceIt()
    {
        var old = new TaskCompletionSource<IReadOnlyList<RepositoryEntry>>();
        var current = new RepositoryEntry("current", "current", DateTimeOffset.UtcNow);
        var store = new DelayedStore { Read = _ => old.Task };
        var entries = new RepositoryCatalogList(store);
        using var request = new CancellationTokenSource();
        var loading = entries.EnsureLoadedAsync(request.Token);
        request.Cancel();
        store.Read = _ => Task.FromResult<IReadOnlyList<RepositoryEntry>>([current]);
        await entries.EnsureLoadedAsync(CancellationToken.None);
        old.SetResult([new("old", "old", DateTimeOffset.UtcNow)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.True(entries.Loaded);
        Assert.Same(current, Assert.Single(entries.Ordered));
    }

    [Fact]
    public void WhenEntriesAreListed_ShouldOrderThemByUseThenAgeThenName()
    {
        var (list, _, _) = Listed();
        Assert.Equal(["opened", "newer", "a", "b"], list.Ordered.Select(entry => entry.Name));
    }

    [Fact]
    public void WhenAPathEndsWithASeparator_ShouldFindTheSameEntry()
    {
        var (list, root, _) = Listed();
        var separator = Path.DirectorySeparatorChar;
        Assert.Equal(list.IndexOf(Path.Combine(root, "a")), list.IndexOf(Path.Combine(root, "a") + separator));
        Assert.NotNull(list.Find(Path.Combine(root, "a") + separator));
    }

    // The path ends with a separator, which the entry's does not.
    [Fact]
    public void WhenAnEntryIsAddedForAListedPath_ShouldReplaceTheListedOne()
    {
        var (list, root, now) = Listed();
        var separator = Path.DirectorySeparatorChar;
        list.AddOrReplace(new(Path.Combine(root, "a") + separator, "a2", now));
        Assert.Equal(4, list.Ordered.Count());
        Assert.Equal("a2", list.Find(Path.Combine(root, "a"))!.Name);
    }

    [Fact]
    public void WhenAnEntryIsRemoved_ShouldListTheOthers()
    {
        var (list, root, _) = Listed();
        list.Remove(Path.Combine(root, "b"));
        Assert.Equal(3, list.Ordered.Count());
    }

    // Two instances of Trag over the same list, both having read it: what one added or removed stays when the other saves
    // a change of its own.
    [Fact]
    public async Task WhenAnotherInstanceChangedTheList_ShouldKeepItsChangeWhenThisOneSaves()
    {
        var store = new SharedStore { Entries = [Entry("kept"), Entry("gone")] };
        var (first, second) = (new RepositoryCatalogList(store), new RepositoryCatalogList(store));
        await first.EnsureLoadedAsync(CancellationToken.None);
        await second.EnsureLoadedAsync(CancellationToken.None);

        first.AddOrReplace(Entry("added"));
        first.Remove("gone");
        await first.SaveAsync(CancellationToken.None);
        second.AddOrReplace(Entry("other"));
        await second.SaveAsync(CancellationToken.None);

        Assert.Equal(["added", "kept", "other"], store.Entries.Select(entry => entry.Path).Order());
        Assert.Equal(["added", "kept", "other"], second.Ordered.Select(entry => entry.Path).Order());
    }

    private static RepositoryEntry Entry(string path) => new(path, path, DateTimeOffset.UnixEpoch);

    // Four entries out of order: a and b added two hours ago, newer an hour ago, and opened, added a day ago, opened now.
    private static (RepositoryCatalogList List, string Root, DateTimeOffset Now) Listed()
    {
        var list = new RepositoryCatalogList(null);
        var now = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath("catalog-root");
        list.AddOrReplace(new(Path.Combine(root, "b"), "b", now.AddHours(-2)));
        list.AddOrReplace(new(Path.Combine(root, "a"), "a", now.AddHours(-2)));
        list.AddOrReplace(new(Path.Combine(root, "opened"), "opened", now.AddDays(-1), now));
        list.AddOrReplace(new(Path.Combine(root, "newer"), "newer", now.AddHours(-1)));
        return (list, root, now);
    }

    private sealed class DelayedStore : IRepositoryCatalogStore
    {
        public required Func<CancellationToken, Task<IReadOnlyList<RepositoryEntry>>> Read { get; set; }
        public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) => Read(ct);
        public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct) =>
            Task.FromResult(change([]));
    }

    // The file both instances read and write.
    private sealed class SharedStore : IRepositoryCatalogStore
    {
        public required IReadOnlyList<RepositoryEntry> Entries { get; set; }
        public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) => Task.FromResult(Entries);
        public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct)
        {
            Entries = change(Entries).ToArray();
            return Task.FromResult(Entries);
        }
    }
}
