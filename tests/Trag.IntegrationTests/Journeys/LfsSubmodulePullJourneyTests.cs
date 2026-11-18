using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Journeys;

// The other machine pushes a new version of a Git LFS file and moves a submodule; the pull here downloads the LFS
// content before it merges, the merge commit records the incoming submodule commit, and Update submodules checks it out;
// over real Git and Git LFS.
public sealed class LfsSubmodulePullJourneyTests
{
    [Fact]
    public async Task WhenAPullBringsANewLfsVersionAndAMovedSubmodule_ShouldDownloadBeforeTheMergeAndOfferTheSubmoduleUpdate()
    {
        await using var journey = await JourneyShell.StartAsync();
        var f = journey.Fixture;
        var (container, seed, token) = (f.Container, f.Root, f.Token);
        var (mine, other, hub, sub) = (Path.Combine(container, "this machine"), Path.Combine(container, "other machine"),
            Path.Combine(container, "hub.git"), Path.Combine(container, "sub origin"));

        // The hub's main: an LFS file and a submodule, its content pushed before the commits.
        Directory.CreateDirectory(sub);
        await journey.Git(sub, "init", "-q", "--initial-branch=main");
        await SubmoduleFixture.IdentifyAsync(f, sub);
        await journey.Git(sub, "commit", "-q", "--allow-empty", "-m", "Sub one");
        await journey.Git(container, "init", "-q", "--bare", "--initial-branch=main", "hub.git");
        await LfsConfig.SetAsync(f, seed, installed: true);
        await f.WriteAsync(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        await f.WriteAsync("asset.bin", "version one\n");
        await journey.Git(seed, "-c", "protocol.file.allow=always", "submodule", "add", "-q", "--", new Uri(sub).AbsoluteUri, "sub");
        await f.CommitAsync("Start with an asset and a submodule", ".gitattributes", "asset.bin", "sub", ".gitmodules");
        await journey.Git(seed, "remote", "add", "origin", hub);
        await journey.Git(seed, "lfs", "push", "origin", "HEAD");
        await journey.Git(seed, "push", "-q", "--no-verify", "origin", "HEAD:refs/heads/main");
        await journey.Git(container, "-c", "protocol.file.allow=always", "clone", "-q", "--recurse-submodules", hub, mine);
        await SubmoduleFixture.IdentifyAsync(f, mine);
        await journey.Git(mine, "config", "protocol.file.allow", "always");
        await journey.Git(container, "clone", "-q", hub, other);
        await SubmoduleFixture.IdentifyAsync(f, other);
        journey.Host.Folder = mine;
        var shell = journey.Shell;
        await shell.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await shell.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal("version one\n", await File.ReadAllTextAsync(Path.Combine(mine, "asset.bin"), token));

        // Over there: a new version of the asset and the submodule moved to a new commit, content first again.
        await journey.Git(sub, "commit", "-q", "--allow-empty", "-m", "Sub two");
        var subTwo = await journey.Git(sub, "rev-parse", "HEAD");
        await journey.Git(other, "update-index", "--cacheinfo", "160000," + subTwo + ",sub");
        await File.WriteAllTextAsync(Path.Combine(other, "asset.bin"), "version two\n", token);
        await journey.Git(other, "add", "asset.bin");
        await journey.Git(other, "commit", "-q", "-m", "New asset, newer submodule");
        await journey.Git(other, "lfs", "push", "origin", "main");
        await journey.Git(other, "push", "-q", "--no-verify", "origin", "main");
        var theirs = await journey.Git(other, "rev-parse", "HEAD");

        // Here: a commit of this machine's own, so the pull makes a merge commit.
        await File.WriteAllTextAsync(Path.Combine(mine, "notes.txt"), "mine\n", token);
        await journey.Git(mine, "add", "notes.txt");
        await journey.Git(mine, "commit", "-q", "-m", "Notes here");
        var ours = await journey.Git(mine, "rev-parse", "HEAD");
        await ShellHarness.Until(() => shell.Repository.Snapshot!.Head == ours && !shell.Repository.IsBusy, "the watcher to show the commit");
        await shell.Sync.SyncCompletion;

        // Pull: the LFS content comes down before the merge, which records the submodule's new commit.
        var steps = new List<string>();
        shell.StatusBar.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(shell.StatusBar.Status)) lock (steps) steps.Add(shell.StatusBar.Status); };
        await shell.Sync.PullCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(ours + " " + theirs, await journey.Git(mine, "log", "-1", "--format=%P"));
        lock (steps)
        {
            Assert.Contains(SyncOutcomeText.FetchingLfs, steps);
            Assert.True(steps.IndexOf(SyncOutcomeText.FetchingLfs) < steps.IndexOf(SyncOutcomeText.MergingFetched), string.Join(" | ", steps));
        }
        Assert.Equal("version two\n", await File.ReadAllTextAsync(Path.Combine(mine, "asset.bin"), token));
        Assert.Equal(subTwo, await journey.Git(mine, "rev-parse", "HEAD:sub"));
        Assert.NotEqual(subTwo, await journey.Git(Path.Combine(mine, "sub"), "rev-parse", "HEAD"));
        Assert.Equal(("sub", ""), (await journey.Git(mine, "diff", "--name-only"), await journey.Git(mine, "diff", "--cached", "--name-only")));

        // The submodule is offered for its update, which checks the recorded commit out and leaves nothing to commit.
        Assert.Equal(SubmoduleText.NotAtRecorded(["sub"]), shell.FileStatus.SubmoduleNotice);
        Assert.True(shell.FileStatus.UpdateSubmodulesCommand.CanExecute(null));
        await shell.FileStatus.UpdateSubmodulesCommand.ExecuteAsync(null);
        await shell.Sync.SyncCompletion;
        Assert.Equal(subTwo, await journey.Git(Path.Combine(mine, "sub"), "rev-parse", "HEAD"));
        Assert.Equal("", await journey.Git(mine, "--no-optional-locks", "status", "--porcelain"));
        Assert.Equal("", shell.FileStatus.SubmoduleNotice);
        Assert.Empty(shell.FileStatus.UnstagedFiles);
        Assert.Empty(shell.FileStatus.StagedFiles);
        await journey.Git(mine, "fsck", "--no-progress");
        await journey.Git(mine, "lfs", "fsck");
    }
}
