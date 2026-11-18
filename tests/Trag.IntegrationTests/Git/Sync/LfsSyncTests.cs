using System.Security.Cryptography;
using System.Text;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Sync;

// Commits with Git LFS files and the remote: a pull downloads their content right after the fetch, where the transfer's
// credentials and limits are, and the merge then checks the files out from the repository's own LFS storage; a push and
// a publish upload their content before the commits.
public sealed class LfsSyncTests
{
    [Fact]
    public async Task WhenIncomingCommitsHoldLfsFiles_ShouldDownloadTheirContentWithTheFetchAndPullThem()
    {
        using var f = await SyncFixture.Create();
        await f.PushLfsFileAsync();
        await f.Lfs(f.One, installed: true);
        var progress = new ProgressLog();

        var result = await f.Execute(SyncAction.Pull, progress: progress);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("fixture\n", await File.ReadAllTextAsync(Path.Combine(f.One, "asset.bin"), f.Token));
        Assert.Contains(SyncOutcomeText.FetchingLfs, progress.Reports);
    }

    // Git LFS's download goes through the transport like Git's own, so a server that says nothing for the quiet limit
    // stops it, and the pull says so before anything is merged.
    [Fact]
    public async Task WhenLfsFetchExceedsTheQuietLimit_ShouldStopThePullBeforeTheMergeAndSaySo()
    {
        using var silent = new SilentServer();
        using var f = await SyncFixture.Create(quietLimit: TimeSpan.FromSeconds(3)); var before = await f.Head(f.One);
        await f.PushLfsFileAsync(); await f.Lfs(f.One, installed: true);
        await f.Git(f.One, "config", "lfs.url", silent.Url("lfs"));

        var result = await f.Execute(SyncAction.Pull);

        Assert.Equal((GitWriteOutcome.Cancelled, SyncOutcomeText.TimedOut), (result.Outcome, result.Message));
        Assert.True(silent.Connected.IsCompleted);
        Assert.Equal(before, await f.Head(f.One));
    }

    // Smudge turned off as git lfs install --skip-smudge turns it off: the pull still downloads the content with the fetch,
    // and the merge checks the file out through the user's filter, which leaves its pointer.
    [Fact]
    public async Task WhenSmudgeIsDisabled_ShouldStillDownloadTheContentAndCheckOutThePointer()
    {
        using var f = await SyncFixture.Create();
        await f.PushLfsFileAsync(); await f.Lfs(f.One, installed: true);
        await f.Git(f.One, "config", "filter.lfs.process", "git-lfs filter-process --skip");
        await f.Git(f.One, "config", "filter.lfs.smudge", "git-lfs smudge --skip -- %f");

        var result = await f.Execute(SyncAction.Pull);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.True(File.Exists(Stored(Path.Combine(f.One, ".git"), "fixture\n")));
        Assert.StartsWith("version https://git-lfs.github.com/spec/v1", await File.ReadAllTextAsync(Path.Combine(f.One, "asset.bin"), f.Token));
        Assert.Equal("", await f.Git(f.One, "status", "--porcelain"));
    }

    // Content the remote does not have stops the pull at the download, before anything is merged.
    [Fact]
    public async Task WhenTheRemoteLacksTheIncomingLfsContent_ShouldStopThePullBeforeTheMerge()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        await f.PushLfsFileAsync(content: false);
        await f.Lfs(f.One, installed: true);

        var result = await f.Execute(SyncAction.Pull);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.LfsNotFetched), (result.Outcome, result.Message));
        Assert.NotEqual("", result.Details);
        Assert.Equal(before, await f.Head(f.One));
    }

    [Fact]
    public async Task WhenPushedCommitsHoldLfsFiles_ShouldUploadTheirContentBeforeTheCommits()
    {
        using var f = await SyncFixture.Create();
        await f.CommitLfsFileAsync();
        var progress = new ProgressLog();

        var result = await f.Execute(SyncAction.Push, progress: progress);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Contains(SyncOutcomeText.PushingLfs, progress.Reports);
        Assert.True(File.Exists(StoredInOrigin(f, "fixture\n")));
    }

    // The commit that adds the LFS file goes to the remote too, though the last commit deleted the file again.
    [Fact]
    public async Task WhenAnLfsObjectIsOnlyInAnIntermediateCommit_ShouldStillPushIt()
    {
        using var f = await SyncFixture.Create();
        await f.CommitLfsFileAsync();
        await f.Git(f.One, "rm", "-q", "asset.bin"); await f.Git(f.One, "commit", "-m", "No asset");

        var result = await f.Execute(SyncAction.Push);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.True(File.Exists(StoredInOrigin(f, "fixture\n")));
    }

    // A remote with a push address of its own takes the LFS content where the commits go.
    [Fact]
    public async Task WhenTheRemoteHasAPushAddressOfItsOwn_ShouldUploadTheLfsContentThere()
    {
        using var f = await SyncFixture.Create(); var other = Path.Combine(f.Root, "publish.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, other);
        await f.Git(f.One, "remote", "set-url", "--push", "origin", other);
        await f.CommitLfsFileAsync();

        var result = await f.Execute(SyncAction.Push);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal((true, false), (File.Exists(Stored(other, "fixture\n")), File.Exists(StoredInOrigin(f, "fixture\n"))));
    }

    // The remote's own LFS address still counts when the transfer runs against Trag's verified copy of the remote, as it
    // does for a push address of its own or a Trag account.
    [Fact]
    public async Task WhenTheRemoteNamesItsOwnLfsAddress_ShouldUploadThereThroughTheVerifiedRemote()
    {
        using var f = await SyncFixture.Create(); var other = Path.Combine(f.Root, "publish.git"); var store = Path.Combine(f.Root, "store.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, other);
        await f.Git(f.Root, "init", "--bare", store);
        await f.Git(f.One, "remote", "set-url", "--push", "origin", other);
        await f.Git(f.One, "config", "remote.origin.lfsurl", new Uri(store).AbsoluteUri);
        await f.CommitLfsFileAsync();

        var result = await f.Execute(SyncAction.Push);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal((true, false), (File.Exists(Stored(store, "fixture\n")), File.Exists(Stored(other, "fixture\n"))));
    }

    private static string StoredInOrigin(SyncFixture f, string content) => Stored(f.Bare, content);

    private static string Stored(string bare, string content)
    {
        var oid = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        return Path.Combine(bare, "lfs", "objects", oid[..2], oid[2..4], oid);
    }

    // A branch One publishes to the remote it already has.
    private static async Task<GitWriteResult> PublishTopicAsync(SyncFixture f) => await f.Sync.ExecuteAsync(
        new PublishRequest(await f.Reader.ReadAsync(f.One, f.Token), new(PublishKind.ExistingRemote, "origin", "topic", true)), f.Token);

    [Fact]
    public async Task WhenAPublishedBranchHoldsLfsFiles_ShouldUploadTheirContentBeforeTheCommits()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "topic");
        await f.CommitLfsFileAsync();

        var result = await PublishTopicAsync(f);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.True(File.Exists(StoredInOrigin(f, "fixture\n")));
    }

    [Fact]
    public async Task WhenAPublishedBranchsLfsContentCannotBeUploaded_ShouldPublishNoCommits()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "topic");
        await f.CommitLfsFileAsync();
        await BlockLfsStorageAsync(f);

        var result = await PublishTopicAsync(f);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.EndsWith(SyncOutcomeText.LfsNotPushed, result.Message);
        Assert.Equal("", await f.Git(f.Bare, "branch", "--list", "topic"));
    }

    // The LFS storage One sends to is a path under a file, where nothing can be written.
    private static async Task BlockLfsStorageAsync(SyncFixture f)
    {
        var blocker = Path.Combine(f.Root, "blocker");
        await File.WriteAllTextAsync(blocker, "a file where the LFS storage would be\n", f.Token);
        await f.Git(f.One, "config", "lfs.url", new Uri(Path.Combine(blocker, "store")).AbsoluteUri);
    }

    [Fact]
    public async Task WhenTheLfsContentCannotBeUploaded_ShouldPushNothing()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        await f.CommitLfsFileAsync();
        await BlockLfsStorageAsync(f);

        var result = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.LfsNotPushed), (result.Outcome, result.Message));
        Assert.NotEqual("", result.Details);
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // Pushed commits without LFS files have nothing for Git LFS to upload.
    [Fact]
    public async Task WhenPushedCommitsHoldNoLfsFiles_ShouldPushWithoutGitLfs()
    {
        using var f = await SyncFixture.Create();
        await f.Lfs(f.One, installed: true);
        await f.Commit(f.One, "plain");
        var progress = new ProgressLog();

        var result = await f.Execute(SyncAction.Push, progress: progress);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain(SyncOutcomeText.PushingLfs, progress.Reports);
    }

    // One pulls Two's LFS file on trunk, then Two pushes a branch with another LFS file, its content too or not, and One
    // fetches the branch without its LFS content. Returns the branch's commit.
    private static async Task<string> FeatureWithLfsFileAsync(SyncFixture f, bool content)
    {
        await f.PushLfsFileAsync();
        await f.Lfs(f.One, installed: true);
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Pull)).Outcome);
        await f.Git(f.Two, "switch", "-c", "feature");
        await File.WriteAllTextAsync(Path.Combine(f.Two, "feature.bin"), "feature\n", f.Token);
        await f.Git(f.Two, "add", "feature.bin"); await f.Git(f.Two, "commit", "-m", "Feature asset");
        if (content) await f.Git(f.Two, "lfs", "push", "origin", "feature");
        await f.Git(f.Two, "push", "--no-verify", "origin", "feature");
        await f.Git(f.One, "fetch", "origin");
        return await f.Head(f.Two);
    }

    private static async Task<GitWriteResult> DownloadAsync(SyncFixture f, string commit) =>
        await new GitWriteRouter(new GitProcessRunner(), f.Sync).ExecuteAsync(new LfsDownloadRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", commit), f.Token);

    [Fact]
    public async Task WhenARemoteBranchsLfsContentIsDownloaded_ShouldStoreItBeforeAnyMerge()
    {
        using var f = await SyncFixture.Create();
        var feature = await FeatureWithLfsFileAsync(f, content: true);

        var result = await DownloadAsync(f, feature);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.True(File.Exists(Stored(Path.Combine(f.One, ".git"), "feature\n")));
    }

    [Fact]
    public async Task WhenTheRemoteLacksTheBranchsLfsContent_ShouldRejectTheDownload()
    {
        using var f = await SyncFixture.Create();
        var feature = await FeatureWithLfsFileAsync(f, content: false);

        var result = await DownloadAsync(f, feature);

        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.LfsNotDownloaded), (result.Outcome, result.Message));
        Assert.NotEqual("", result.Details);
    }

    // A branch that brings the repository's first LFS files has its content downloaded too.
    [Fact]
    public async Task WhenARemoteBranchBringsTheFirstLfsFiles_ShouldDownloadTheirContent()
    {
        using var f = await SyncFixture.Create();
        await f.Lfs(f.One, installed: true);
        await f.Git(f.Two, "switch", "-c", "feature");
        await f.PushLfsFileAsync(branch: "feature");
        await f.Git(f.One, "fetch", "origin");

        var result = await DownloadAsync(f, await f.Head(f.Two));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.True(File.Exists(Stored(Path.Combine(f.One, ".git"), "fixture\n")));
    }

    // Without Git LFS there is nothing to download with, and Git is not asked.
    [Fact]
    public async Task WhenGitLfsIsNotInstalled_ShouldDownloadNothingWithoutRunningGit()
    {
        using var f = await SyncFixture.Create();
        await f.Lfs(f.One, installed: false);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var processes = 0;
        var sync = new GitSyncFacade(new GitProcessRunner(exited: _ => Interlocked.Increment(ref processes)), f.Reader);

        var result = await sync.ExecuteAsync(new LfsDownloadRequest(snapshot, "origin", snapshot.Head!), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, 0), (result.Outcome, processes));
    }

    // A large repository's tree is more than the runner holds at once, yet a push and a pull still ask it whether the
    // commit has LFS files. The transfer itself brings one file, so nothing else Git prints comes near the limit.
    [Fact]
    public async Task WhenTheTreeIsLargerThanTheRunnerHolds_ShouldStillPushAndPull()
    {
        using var f = await SyncFixture.Create();
        await f.Lfs(f.One, installed: true); await f.Lfs(f.Two, installed: true);
        string[] names = [.. Enumerable.Range(0, 150).Select(i => $"file-{i:000}.txt")];
        foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(f.One, name), "content\n", f.Token);
        await f.Git(f.One, ["add", .. names]); await f.Git(f.One, "commit", "-m", "Files");
        await f.Git(f.One, "push", "origin", "trunk"); await f.Git(f.Two, "pull", "--ff-only");
        await f.Commit(f.One, "one more");
        var runner = new GitProcessRunner(outputLimit: 4096);
        var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader);
        async Task<GitWriteResult> Run(string root, SyncAction action)
        {
            var snapshot = await reader.ReadAsync(root, f.Token);
            return await sync.ExecuteAsync(new SyncRequest(snapshot, await sync.ReadAsync(snapshot, "origin", null, f.Token), action), f.Token);
        }

        var pushed = await Run(f.One, SyncAction.Push);
        var pulled = await Run(f.Two, SyncAction.Pull);

        Assert.True(pushed.Outcome == GitWriteOutcome.Succeeded, pushed.Message + pushed.Details);
        Assert.True(pulled.Outcome == GitWriteOutcome.Succeeded, pulled.Message + pulled.Details);
    }

    // Incoming commits without LFS files have nothing for Git LFS to download.
    [Fact]
    public async Task WhenIncomingCommitsHoldNoLfsFiles_ShouldPullWithoutGitLfs()
    {
        using var f = await SyncFixture.Create();
        await f.Lfs(f.One, installed: true);
        await f.Commit(f.Two, "plain"); await f.Git(f.Two, "push", "origin", "trunk");
        var progress = new ProgressLog();

        var result = await f.Execute(SyncAction.Pull, progress: progress);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.DoesNotContain(SyncOutcomeText.FetchingLfs, progress.Reports);
    }
}
