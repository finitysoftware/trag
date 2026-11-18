using Trag.Application.Catalog;
using Trag.Domain.Catalog;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Catalog;

public sealed class LocalRepositoryCatalogTests
{
    private static readonly string Root = Path.GetFullPath("catalog-fixture");
    private static string At(string name) => Path.Combine(Root, name);

    [Fact]
    public async Task WhenAFolderInsideARepositoryIsAdded_ShouldRecordTheRepositorysRoot()
    {
        var (store, catalog) = WithRepository();

        var added = await catalog.AddPathAsync(At(Path.Combine("repo", "src")), CancellationToken.None);

        Assert.Equal(CatalogAdditionOutcome.Added, added.Outcome);
        Assert.Equal(At("repo"), added.Entry!.Path); Assert.Equal("repo", added.Entry.Name); Assert.Equal("main", added.Entry.Branch); Assert.Null(added.Entry.LastOpenedAt);
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task WhenARepositoryIsAddedAndLaterOpened_ShouldDateBothByTheCatalogsClock()
    {
        var (_, catalog) = WithRepository();
        var clock = new ManualTime(); catalog.Time = clock;
        var added = (await catalog.AddPathAsync(At(Path.Combine("repo", "src")), CancellationToken.None)).Entry!;
        clock.Now = clock.Now.AddHours(1);
        var opened = (await catalog.RecordOpenedAsync(Snapshots.Create(root: At("repo"), remotes: []), CancellationToken.None)).Entry!;
        Assert.Equal((clock.Now.AddHours(-1), clock.Now.AddHours(-1), clock.Now), (added.AddedAt, opened.AddedAt, opened.LastOpenedAt!.Value));
    }

    [Fact]
    public async Task WhenAListedRepositoryIsAddedAgain_ShouldListItOnceAndKeepItsDate()
    {
        var (store, catalog) = WithRepository();
        var added = await catalog.AddPathAsync(At(Path.Combine("repo", "src")), CancellationToken.None);
        Assert.Equal(At("repo"), added.Entry!.Path);

        var again = await catalog.AddPathAsync(At(Path.Combine("repo", "src")), CancellationToken.None);

        Assert.Equal(CatalogAdditionOutcome.AlreadyListed, again.Outcome);
        Assert.Equal(added.Entry.AddedAt, again.Entry!.AddedAt);
        Assert.Single(catalog.Entries.Ordered); Assert.Equal(2, store.Saves);
    }

    // The list holds as many repositories as the settings keep: another one is refused here, added or opened, rather than
    // shown for the session and lost at the next start.
    [Fact]
    public async Task WhenTheListIsFull_ShouldRefuseAnotherRepositoryAndSaveNothing()
    {
        var (store, catalog) = WithRepository();
        var earlier = DateTimeOffset.UtcNow.AddDays(-1);
        store.Entries.AddRange(Enumerable.Range(0, RepositoryEntry.MaximumListed).Select(i => new RepositoryEntry(At("listed-" + i), "listed-" + i, earlier)));

        var added = await catalog.AddPathAsync(At(Path.Combine("repo", "src")), CancellationToken.None);
        var opened = await catalog.RecordOpenedAsync(Snapshots.Create(root: At("elsewhere"), remotes: []), CancellationToken.None);

        Assert.Equal((CatalogAdditionOutcome.Full, CatalogAdditionOutcome.Full), (added.Outcome, opened.Outcome));
        Assert.Equal(RepositoryEntry.MaximumListed, catalog.Entries.Ordered.Count()); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task WhenTheFolderIsMissing_ShouldNotAddIt()
    {
        var (store, catalog) = WithMissingAndPlainFolders();
        Assert.Equal(CatalogAdditionOutcome.Missing, (await catalog.AddPathAsync(At("gone"), CancellationToken.None)).Outcome);
        Assert.Empty(catalog.Entries.Ordered); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task WhenTheFolderIsNoRepository_ShouldNotAddIt()
    {
        var (store, catalog) = WithMissingAndPlainFolders();
        Assert.Equal(CatalogAdditionOutcome.NotRepository, (await catalog.AddPathAsync(At("plain"), CancellationToken.None)).Outcome);
        Assert.Empty(catalog.Entries.Ordered); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task WhenAFolderThatIsNoRepositoryIsInitialized_ShouldAddIt()
    {
        var (store, catalog) = WithMissingAndPlainFolders();

        var initialized = await catalog.InitializeAsync(At("plain"), CancellationToken.None);

        Assert.Equal(CatalogAdditionOutcome.Added, initialized.Outcome);
        Assert.Equal(At("plain"), Assert.Single(store.Initialized));
        Assert.Single(catalog.Entries.Ordered);
    }

    [Fact]
    public async Task WhenAListedRepositoryOpens_ShouldMoveItToTheTopWithItsBranchAndOrigin()
    {
        var (_, catalog, earlier) = TwoListed();

        var snapshot = Snapshots.Create(root: At("repo"), branch: "topic", remotes: [new("origin", "https://example.invalid/repo.git", "push")]);
        var entry = (await catalog.RecordOpenedAsync(snapshot, CancellationToken.None)).Entry!;

        Assert.Equal("topic", entry.Branch); Assert.Equal("https://example.invalid/repo.git", entry.RemoteUrl);
        Assert.NotNull(entry.LastOpenedAt); Assert.Equal(earlier, entry.AddedAt);
        Assert.Equal(At("repo"), catalog.Entries.Ordered.First().Path);
    }

    // The listed repository opened before, which saved once.
    [Fact]
    public async Task WhenARepositoryOutsideTheListOpens_ShouldAddItNamedAfterItsFolder()
    {
        var (store, catalog, _) = TwoListed();
        var snapshot = Snapshots.Create(root: At("repo"), branch: "topic", remotes: [new("origin", "https://example.invalid/repo.git", "push")]);
        await catalog.RecordOpenedAsync(snapshot, CancellationToken.None);

        var unknown = (await catalog.RecordOpenedAsync(Snapshots.Create(root: At("new"), remotes: []), CancellationToken.None)).Entry!;

        Assert.Equal("new", unknown.Name); Assert.Null(unknown.RemoteUrl); Assert.NotNull(unknown.LastOpenedAt);
        Assert.Equal(3, catalog.Entries.Ordered.Count()); Assert.Equal(2, store.Saves);
    }

    [Fact]
    public async Task WhenEveryEntryIsProbed_ShouldTellEachItemWriteBackOnlyTheChangedOnesAndSaveOnce()
    {
        var store = new FakeRepositoryCatalog();
        var now = DateTimeOffset.UtcNow;
        store.Entries.Add(new(At("a"), "a", now, null, null, "main"));
        store.Entries.Add(new(At("b"), "b", now, null, null, "main"));
        store.Entries.Add(new(At("c"), "c", now, null, null, "main"));
        store.Probes[At("b")] = new(RepositoryLinkStatus.Valid, "feature", "url", At("b"));
        store.Probes[At("c")] = new(RepositoryLinkStatus.Missing);
        var catalog = new LocalRepositoryCatalog(store, store, store);
        await catalog.EnsureLoadedAsync(CancellationToken.None);

        var checking = new List<string>();
        var probes = new Dictionary<string, RepositoryProbe>();
        await catalog.ProbeAllAsync(catalog.Entries.Ordered.ToArray(), entry => entry.Path,
            (entry, on) => { if (on) checking.Add(entry.Name); },
            (entry, probe) =>
            {
                probes[entry.Name] = probe;
                return probe.Status == RepositoryLinkStatus.Valid && (entry.Branch != probe.Branch || entry.RemoteUrl != probe.RemoteUrl)
                    ? entry with { Branch = probe.Branch, RemoteUrl = probe.RemoteUrl } : null;
            }, () => true, CancellationToken.None);

        Assert.Equal(["a", "b", "c"], checking.Order());
        Assert.Equal(RepositoryLinkStatus.Missing, probes["c"].Status);
        Assert.Equal("feature", catalog.Entries.Find(At("b"))!.Branch);
        Assert.Equal("main", catalog.Entries.Find(At("a"))!.Branch);
        Assert.Equal(1, store.Saves);
    }

    // A repository on main whose subfolder src the test adds.
    private static (FakeRepositoryCatalog Store, LocalRepositoryCatalog Catalog) WithRepository()
    {
        var store = new FakeRepositoryCatalog();
        store.Probes[At(Path.Combine("repo", "src"))] = new(RepositoryLinkStatus.Valid, "main", "https://example.invalid/repo.git", At("repo"));
        return (store, new LocalRepositoryCatalog(store, store, store));
    }

    // A folder that is gone and a plain folder that is no repository.
    private static (FakeRepositoryCatalog Store, LocalRepositoryCatalog Catalog) WithMissingAndPlainFolders()
    {
        var store = new FakeRepositoryCatalog();
        store.Probes[At("gone")] = new(RepositoryLinkStatus.Missing);
        store.Probes[At("plain")] = new(RepositoryLinkStatus.NotRepository);
        return (store, new LocalRepositoryCatalog(store, store, store));
    }

    // Two repositories listed a day ago; repo is not opened yet and remembers an old origin and branch.
    private static (FakeRepositoryCatalog Store, LocalRepositoryCatalog Catalog, DateTimeOffset Earlier) TwoListed()
    {
        var store = new FakeRepositoryCatalog();
        var earlier = DateTimeOffset.UtcNow.AddDays(-1);
        store.Entries.Add(new(At("other"), "other", earlier, earlier));
        store.Entries.Add(new(At("repo"), "repo", earlier, null, "old-url", "old"));
        return (store, new LocalRepositoryCatalog(store, store, store), earlier);
    }
}
