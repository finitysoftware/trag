using Trag.TestSupport;
using Trag.Domain.Catalog;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.IntegrationTests.Git.Catalog;

// What the picker shows next to a stored link, for every kind of folder a link can end up pointing at.
public sealed class RepositoryProbeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-fixture-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
    private readonly GitProcessRunner runner = new();
    private readonly RepositoryCatalogFacade catalog;

    public RepositoryProbeTests()
    {
        Directory.CreateDirectory(root);
        catalog = new(new GitProcessRunner(), new RepositoryCatalogStore(Path.Combine(root, "repositories.json")));
    }

    [Fact]
    public async Task WhenTheRepositoryHasNoRemote_ShouldShowItsBranchAndNoAddress()
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work č")).FullName;
        await GitAsync(work, "init", "--initial-branch=trunk");
        var probe = await catalog.ProbeAsync(work, timeout.Token);
        Assert.Equal(RepositoryLinkStatus.Valid, probe.Status);
        Assert.Equal("trunk", probe.Branch);
        Assert.Null(probe.RemoteUrl);
        Assert.Equal(Path.GetFullPath(work), probe.Root);
    }

    [Fact]
    public async Task WhenTheBranchSharesItsNameWithATag_ShouldShowTheBranchByItsName()
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work č")).FullName;
        await GitAsync(work, "init", "--initial-branch=trunk");
        await GitAsync(work, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "--allow-empty", "-m", "Initial");
        await GitAsync(work, "tag", "trunk");

        Assert.Equal("trunk", (await catalog.ProbeAsync(work, timeout.Token)).Branch);
    }

    [Fact]
    public async Task WhenTheFolderIsABareRepository_ShouldSayItIsBare()
    {
        var bare = Directory.CreateDirectory(Path.Combine(root, "bare.git")).FullName;
        await GitAsync(bare, "init", "--bare");
        Assert.Equal(RepositoryLinkStatus.Bare, (await catalog.ProbeAsync(bare, timeout.Token)).Status);
    }

    // A working copy's .git folder and the folders in it hold the repository's Git data; git init there would nest a new
    // repository inside them.
    [Fact]
    public async Task WhenTheFolderIsAWorkingCopysGitFolder_ShouldSayItHoldsGitData()
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        await GitAsync(work, "init");

        Assert.Equal(RepositoryLinkStatus.GitDirectory, (await catalog.ProbeAsync(Path.Combine(work, ".git"), timeout.Token)).Status);
        Assert.Equal(RepositoryLinkStatus.GitDirectory, (await catalog.ProbeAsync(Path.Combine(work, ".git", "refs"), timeout.Token)).Status);
    }

    [Fact]
    public async Task WhenTheFolderIsGoneOrWasNeverAFolder_ShouldReportItMissing()
    {
        var removed = Directory.CreateDirectory(Path.Combine(root, "removed")).FullName;
        await GitAsync(removed, "init");
        Assert.Equal(RepositoryLinkStatus.Valid, (await catalog.ProbeAsync(removed, timeout.Token)).Status);
        Directory.Delete(removed, recursive: true);
        Assert.Equal(RepositoryLinkStatus.Missing, (await catalog.ProbeAsync(removed, timeout.Token)).Status);

        var file = Path.Combine(root, "link.txt");
        await File.WriteAllTextAsync(file, "not a folder\n", timeout.Token);
        Assert.Equal(RepositoryLinkStatus.Missing, (await catalog.ProbeAsync(file, timeout.Token)).Status);
        Assert.Equal(RepositoryLinkStatus.Missing, (await catalog.ProbeAsync("   ", timeout.Token)).Status);
    }

    [Fact]
    public async Task WhenTheFolderIsNoRepository_ShouldNotTakeItForAWorkingCopy()
    {
        var plain = Directory.CreateDirectory(Path.Combine(root, "plain")).FullName;
        Assert.Equal(RepositoryLinkStatus.NotRepository, (await catalog.ProbeAsync(plain, timeout.Token)).Status);
    }

    // A link into a subfolder names the repository it is in; where that is, and that it has a working tree, is one
    // question to Git, and the branch and the origin one each.
    [Fact]
    public async Task WhenTheLinkIsASubfolder_ShouldNameTheRepositoryRootAfterThreeGitCalls()
    {
        var work = Directory.CreateDirectory(Path.Combine(root, "work")).FullName;
        await GitAsync(work, "init", "--initial-branch=trunk");
        var nested = Directory.CreateDirectory(Path.Combine(work, "nested")).FullName;
        var calls = 0;
        var counted = new RepositoryCatalogFacade(new GitProcessRunner(exited: _ => Interlocked.Increment(ref calls)), new MemoryRepositoryCatalogStore());
        var probe = await counted.ProbeAsync(nested, timeout.Token);
        Assert.Equal((RepositoryLinkStatus.Valid, Path.GetFullPath(work)), (probe.Status, probe.Root));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task WhenTheRepositoryIsUnborn_ShouldStillReportTheBranchGitWouldCreate()
    {
        var unborn = Directory.CreateDirectory(Path.Combine(root, "unborn")).FullName;
        await GitAsync(unborn, "init", "--initial-branch=fixture");
        var probe = await catalog.ProbeAsync(unborn, timeout.Token);
        Assert.Equal(RepositoryLinkStatus.Valid, probe.Status);
        Assert.Equal("fixture", probe.Branch);
    }

    private async Task GitAsync(string folder, params string[] arguments)
    {
        var result = await runner.RunAsync(folder, arguments, timeout.Token);
        Assert.True(result.ExitCode == 0, System.Text.Encoding.UTF8.GetString(result.StandardError));
    }

    public void Dispose()
    {
        timeout.Cancel();
        timeout.Dispose();
        FixtureDirectory.Delete(root, "trag-fixture-");
    }
}
