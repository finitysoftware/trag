using Trag.Infrastructure.Platform.Storage;

namespace Trag.Infrastructure.UnitTests.Platform.Storage;

// The catalog of a launch with nowhere to write: the list lives as long as the process, and nothing reaches disk.
public sealed class MemoryRepositoryCatalogStoreTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset When = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WhenTheStoreIsNew_ShouldHaveNothingInIt() => Assert.Empty(await new MemoryRepositoryCatalogStore().LoadAsync(Ct));

    [Fact]
    public async Task WhenEntriesAreSaved_ShouldComeBackSanitized()
    {
        var store = new MemoryRepositoryCatalogStore();
        await store.UpdateAsync(_ => [new(Path.Combine("x", "repo"), "", When), new("   ", "blank", When)], Ct);
        var entry = Assert.Single(await store.LoadAsync(Ct));
        Assert.Equal("repo", entry.Name);
    }

    [Fact]
    public async Task WhenAListIsSaved_ShouldLoadEveryEntryOfIt()
    {
        var store = new MemoryRepositoryCatalogStore();
        await store.UpdateAsync(_ => [new("first", "First", When), new("second", "Second", When)], Ct);
        Assert.Equal(2, (await store.LoadAsync(Ct)).Count);
    }

    [Fact]
    public async Task WhenAnotherListIsSaved_ShouldReplaceTheListRatherThanAddToIt()
    {
        var store = new MemoryRepositoryCatalogStore();
        await store.UpdateAsync(_ => [new("first", "First", When), new("second", "Second", When)], Ct);
        await store.LoadAsync(Ct);
        await store.UpdateAsync(_ => [new("third", "Third", When)], Ct);
        Assert.Equal("Third", Assert.Single(await store.LoadAsync(Ct)).Name);
    }
}
