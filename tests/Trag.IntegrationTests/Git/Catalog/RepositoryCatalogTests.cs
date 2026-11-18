using Trag.Application.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Catalog;

public sealed class RepositoryCatalogTests
{
    [Fact]
    public async Task WhenARepositoryOrAFolderInsideItIsProbed_ShouldReportItValidWithItsRootBranchAndRedactedAddress()
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (_, source, ct, _, _, _, catalog) = await PrepareAsync(fixture);

        var probe = await catalog.ProbeAsync(source, ct);
        Assert.Equal(RepositoryLinkStatus.Valid, probe.Status); Assert.Equal("trunk", probe.Branch);
        Assert.Equal(Path.GetFullPath(source), probe.Root);
        Assert.Contains("github.com/acme/source", probe.RemoteUrl); Assert.DoesNotContain("secret", probe.RemoteUrl);
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        var nested = await catalog.ProbeAsync(Path.Combine(source, "sub"), ct);
        Assert.Equal(RepositoryLinkStatus.Valid, nested.Status); Assert.Equal(Path.GetFullPath(source), nested.Root);
    }

    [Fact]
    public async Task WhenAFolderIsNotARepositoryOrIsMissing_ShouldProbeItAsSuch()
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (root, _, ct, plain, _, _, catalog) = await ProbedTheRepositoryAsync(fixture);
        Assert.Equal(RepositoryLinkStatus.NotRepository, (await catalog.ProbeAsync(root, ct)).Status);
        Assert.Equal(RepositoryLinkStatus.NotRepository, (await catalog.ProbeAsync(plain, ct)).Status);
        Assert.Equal(RepositoryLinkStatus.Missing, (await catalog.ProbeAsync(Path.Combine(root, "missing"), ct)).Status);
    }

    [Fact]
    public async Task WhenAFolderIsInitialized_ShouldProbeItAsAValidRepository()
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (_, _, ct, _, fresh, _, catalog) = await ProbedEveryFolderAsync(fixture);
        await catalog.InitializeAsync(fresh, ct);
        Assert.Equal(RepositoryLinkStatus.Valid, (await catalog.ProbeAsync(fresh, ct)).Status);
    }

    [Fact]
    public async Task WhenARepositoryIsCloned_ShouldGiveAValidWorkingCopyAndRefuseASecondCloneIntoIt()
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (_, source, ct, _, _, clone, catalog) = await InitializedAsync(fixture);
        var progress = new List<string>();
        var result = await catalog.CloneAsync(new(source, clone, Progress: new Progress<string>(progress.Add)), ct);
        Assert.True(result.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(RepositoryLinkStatus.Valid, (await catalog.ProbeAsync(clone, ct)).Status);
        Assert.True(File.Exists(Path.Combine(clone, "file.txt")));
        var again = await catalog.CloneAsync(new(source, clone), ct);
        Assert.False(again.Succeeded); Assert.Contains("empty or new folder", again.Message);
    }

    [Fact]
    public async Task WhenARepositoryIsSaved_ShouldLoadItBack()
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (_, source, ct, _, _, _, catalog) = await AfterABrokenCloneAsync(fixture);
        await catalog.UpdateAsync(_ => [new RepositoryEntry(source, "source", DateTimeOffset.UtcNow)], ct);
        Assert.Equal(source, Assert.Single(await catalog.LoadAsync(ct)).Path);
    }

    // A folder named by the corpus of hostile names is initialized and listed under exactly its path, which a fresh read
    // of the list gives back with the folder's name, and it probes as a working copy. The list shows a name without the
    // spaces around it.
    [Theory]
    [MemberData(nameof(HostileNameRows.UsableFolders), MemberType = typeof(HostileNameRows))]
    public async Task WhenARepositoryFolderHasAHostileName_ShouldAddItLoadItBackAndProbeItValid(string label)
    {
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (json, facade, catalog) = LocalCatalog(fixture);
        var folder = Path.Combine(fixture.Container, "repositories", TestNames.Find(label).Path);

        var added = await catalog.InitializeAsync(folder, fixture.Token);

        Assert.Equal(CatalogAdditionOutcome.Added, added.Outcome);
        var entry = Assert.Single(await new RepositoryCatalogStore(json).LoadAsync(fixture.Token));
        Assert.Equal(Path.GetFullPath(folder), entry.Path);
        Assert.Equal(Path.GetFileName(folder).Trim(), entry.Name);
        Assert.Equal(RepositoryLinkStatus.Valid, (await facade.ProbeAsync(entry.Path, fixture.Token)).Status);
    }

    // Git for Windows cannot work in a folder with a reserved device name, nor in one past MAX_PATH.
    [Theory]
    [MemberData(nameof(HostileNameRows.UnusableFolders), MemberType = typeof(HostileNameRows))]
    public async Task WhenGitCannotWorkInTheFolder_ShouldNotInitializeOrListIt(string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (json, _, catalog) = LocalCatalog(fixture);

        var added = await catalog.InitializeAsync(Path.Combine(fixture.Container, "repositories", TestNames.Find(label).Path), fixture.Token);

        Assert.Equal(CatalogAdditionOutcome.Failed, added.Outcome);
        Assert.Empty(await new RepositoryCatalogStore(json).LoadAsync(fixture.Token));
    }

    // Where the file system ignores case, the same repository named in other case is the entry already listed.
    [Fact]
    public async Task WhenARepositoryIsAddedAgainByAPathDifferingOnlyInCase_ShouldKeepOneEntry()
    {
        if (LocalPaths.Comparison == StringComparison.Ordinal) return;
        using var fixture = await GitFixture.CreateAsync("trunk");
        var (json, _, catalog) = LocalCatalog(fixture);

        var first = await catalog.AddPathAsync(fixture.Root, fixture.Token);
        var again = await catalog.AddPathAsync(fixture.Root.ToUpperInvariant(), fixture.Token);

        Assert.Equal((CatalogAdditionOutcome.Added, CatalogAdditionOutcome.AlreadyListed), (first.Outcome, again.Outcome));
        Assert.Single(await new RepositoryCatalogStore(json).LoadAsync(fixture.Token));
    }

    private static (string Json, RepositoryCatalogFacade Facade, LocalRepositoryCatalog Catalog) LocalCatalog(GitFixture fixture)
    {
        var json = Path.Combine(fixture.Container, "repositories.json");
        var facade = new RepositoryCatalogFacade(fixture.Runner, new RepositoryCatalogStore(json));
        return (json, facade, new LocalRepositoryCatalog(new RepositoryCatalogStore(json), facade, facade));
    }

    private sealed record Setup(string Root, string Source, CancellationToken Ct, string Plain, string Fresh, string CloneTarget, RepositoryCatalogFacade Catalog);

    // A committed repository whose remote address carries a password, an empty folder beside it, and the folders the
    // later steps initialize and clone into.
    private static async Task<Setup> PrepareAsync(GitFixture fixture)
    {
        var root = fixture.Container; var source = fixture.Root; var ct = fixture.Token;
        var plain = Directory.CreateDirectory(Path.Combine(root, "plain")).FullName; var fresh = Path.Combine(root, "fresh"); var clone = Path.Combine(root, "clones", "source");
        var catalog = new RepositoryCatalogFacade(fixture.Runner, new RepositoryCatalogStore(Path.Combine(root, "repositories.json")));
        await fixture.WriteAsync("file.txt", "content\n");
        await fixture.CommitAsync("Initial", "file.txt");
        await fixture.GitAsync("remote", "add", "origin", "https://user:secret@github.com/acme/source.git");
        return new(root, source, ct, plain, fresh, clone, catalog);
    }

    // The repository and a new folder inside it were probed.
    private static async Task<Setup> ProbedTheRepositoryAsync(GitFixture fixture)
    {
        var setup = await PrepareAsync(fixture);
        await setup.Catalog.ProbeAsync(setup.Source, setup.Ct);
        Directory.CreateDirectory(Path.Combine(setup.Source, "sub"));
        await setup.Catalog.ProbeAsync(Path.Combine(setup.Source, "sub"), setup.Ct);
        return setup;
    }

    // Then the folders that are no repository, and one that is missing.
    private static async Task<Setup> ProbedEveryFolderAsync(GitFixture fixture)
    {
        var setup = await ProbedTheRepositoryAsync(fixture);
        await setup.Catalog.ProbeAsync(setup.Root, setup.Ct);
        await setup.Catalog.ProbeAsync(setup.Plain, setup.Ct);
        await setup.Catalog.ProbeAsync(Path.Combine(setup.Root, "missing"), setup.Ct);
        return setup;
    }

    private static async Task<Setup> InitializedAsync(GitFixture fixture)
    {
        var setup = await ProbedEveryFolderAsync(fixture);
        await setup.Catalog.InitializeAsync(setup.Fresh, setup.Ct);
        await setup.Catalog.ProbeAsync(setup.Fresh, setup.Ct);
        return setup;
    }

    // The repository was cloned, the clone probed, and a second clone into the same folder refused.
    private static async Task<Setup> ClonedTwiceAsync(GitFixture fixture)
    {
        var setup = await InitializedAsync(fixture);
        var progress = new List<string>();
        await setup.Catalog.CloneAsync(new(setup.Source, setup.CloneTarget, Progress: new Progress<string>(progress.Add)), setup.Ct);
        await setup.Catalog.ProbeAsync(setup.CloneTarget, setup.Ct);
        await setup.Catalog.CloneAsync(new(setup.Source, setup.CloneTarget), setup.Ct);
        return setup;
    }

    private static async Task<Setup> AfterABrokenCloneAsync(GitFixture fixture)
    {
        var setup = await ClonedTwiceAsync(fixture);
        await setup.Catalog.CloneAsync(new(Path.Combine(setup.Root, "nowhere"), Path.Combine(setup.Root, "clones", "nowhere")), setup.Ct);
        return setup;
    }
}
