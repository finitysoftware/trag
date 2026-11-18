using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.IntegrationTests.Git.Sync;

// Every transfer Trag refuses or Git rejects, and what has to be untouched afterwards: divergence, a dirty tree, a
// foreign lock, content it cannot check out, a hook on the server, and a remote configuration it cannot trust.
public sealed class SyncRefusalTests
{
    [Fact]
    public async Task WhenADivergedBranchIsPulled_ShouldStopForResolutionAndKeepTheLocalBranch()
    {
        using var f = await SyncFixture.Create();
        var (local, _, pull) = await PullADivergence(f);
        Assert.Equal(GitWriteOutcome.NeedsResolution, pull.Outcome);
        Assert.Equal(local, await f.Head(f.One)); Assert.True(File.Exists(Path.Combine(f.One, ".git", "MERGE_HEAD")));
    }

    [Fact]
    public async Task WhenADivergedBranchIsPushedAfterItsPull_ShouldRejectThePushAndKeepTheRemoteBranch()
    {
        using var f = await SyncFixture.Create();
        var (_, remote, _) = await PullADivergence(f);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal(remote, await f.Head(f.Bare));
        var state = await f.State(); Assert.Equal(1, state.Ahead); Assert.Equal(1, state.Behind);
    }

    [Fact]
    public async Task WhenThePushTargetChangedSinceItWasRead_ShouldRejectThePush()
    {
        using var f = await SyncFixture.Create();
        var (snapshot, state) = await ChangePushTargetAfterRead(f);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Push), f.Token)).Outcome);
    }

    [Fact]
    public async Task WhenTheWorkingTreeIsDirty_ShouldRejectThePull()
    {
        using var f = await SyncFixture.Create();
        await DirtyTreeAfterARefusedPush(f);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Pull)).Outcome);
    }

    // An amend after the push leaves the upstream's tip in the branch's own history: its extra commit is the branch's
    // old one, and a pull would merge it back in.
    [Fact]
    public async Task WhenTheBranchWasRewrittenAfterItsPush_ShouldRejectThePullAndKeepTheBranch()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "pushed"); await f.Git(f.One, "push", "origin", "trunk");
        await f.Git(f.One, "commit", "--amend", "-m", "rewritten");
        var rewritten = await f.Head(f.One);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var state = await f.Sync.ReadAsync(snapshot, "origin", null, f.Token);
        Assert.True(state.IsRewritten);
        var pull = await f.Sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Pull), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, pull.Outcome);
        Assert.Equal(rewritten, await f.Head(f.One));
    }

    // The other way round: someone rewrote the upstream (amended a pushed commit, here only its message) and forced it
    // over the remote. The local branch still holds the old commit, so it looks diverged, and a pull would merge the old
    // commit back in beside its rewritten copy; the next push hands it back to the remote. The automatic fetch usually
    // brings the rewritten upstream in before the user pulls, and the branch may carry commits of its own on top.
    [Theory]
    [InlineData("pull")]
    [InlineData("fetched first")]
    [InlineData("own commit on top")]
    public async Task WhenTheUpstreamWasRewrittenByForcePush_ShouldNotMergeItsOldCommitsBackIn(string story)
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "shared work"); await f.Git(f.One, "push", "origin", "trunk");
        await f.Git(f.Two, "pull", "--ff-only", "origin", "trunk");
        await f.Git(f.Two, "commit", "--amend", "-m", "shared work, reworded");
        await f.Git(f.Two, "push", "--force", "origin", "trunk");
        if (story == "fetched first") Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Fetch)).Outcome);
        if (story == "own commit on top") await f.Commit(f.One, "my own work");
        var before = await f.Head(f.One);

        var pull = await f.Execute(SyncAction.Pull);

        Assert.True(pull.Outcome == GitWriteOutcome.Rejected, pull.Outcome + ": " + pull.Message);
        Assert.Equal(SyncOutcomeText.FetchedUpstreamRewritten, pull.Message);
        Assert.Equal(before, await f.Head(f.One));
    }

    // The upstream branch was deleted on the remote; its tracking branch stays, as nothing here prunes. The pull asks the
    // remote for the branch itself, as Git's own pull does, so it says the branch is gone instead of that the branch
    // already contains it, and fetches and merges nothing.
    [Fact]
    public async Task WhenTheRemoteDeletedTheUpstreamBranch_ShouldSayItInsteadOfAlreadyContains()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "feature"); await f.Git(f.One, "push", "-u", "origin", "feature");
        await f.Git(f.Bare, "branch", "-D", "feature");
        var (before, tracked) = (await f.Head(f.One), await f.Git(f.One, "rev-parse", "refs/remotes/origin/feature"));

        var pull = await f.Execute(SyncAction.Pull);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.UpstreamDeleted), (pull.Outcome, pull.Message));
        Assert.Equal((before, tracked), (await f.Head(f.One), await f.Git(f.One, "rev-parse", "refs/remotes/origin/feature")));
    }

    // The fetch prunes as the user's configuration says, as it does in a terminal, so
    // fetch.prune=true takes away the tracking branch of a branch deleted on the remote, and the fetch still brings what
    // the peer pushed.
    [Fact]
    public async Task WhenTheUserConfiguredFetchPrune_ShouldRemoveTheTrackingBranchOfABranchDeletedOnTheRemote()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.Two, "push", "origin", "trunk:refs/heads/gone"); await f.Git(f.One, "fetch", "origin");
        await f.Git(f.Bare, "branch", "-D", "gone");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        await f.Git(f.One, "config", "fetch.prune", "true");

        var fetch = await f.Execute(SyncAction.Fetch);

        Assert.Equal(GitWriteOutcome.Succeeded, fetch.Outcome);
        Assert.Equal(("", await f.Head(f.Two)),
            (await f.Git(f.One, "for-each-ref", "refs/remotes/origin/gone"), await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk")));
    }

    // Every transfer runs with -c submodule.recurse=false, which Git reads after every other setting, so the user's
    // fetch.recurseSubmodules=yes fetches nothing into a submodule behind the fetch Trag shows: the submodule's tracking
    // branch stays where it was, though its remote moved on.
    [Fact]
    public async Task WhenTheUserConfiguredFetchRecurseSubmodules_ShouldFetchOnlyTheRepositoryItself()
    {
        using var f = await SyncFixture.Create(); var library = Path.Combine(f.Root, "library.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, library);
        await f.Git(f.One, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(library).AbsoluteUri, "sub");
        await f.Git(f.One, "commit", "-m", "Add sub");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk"); await f.Git(f.Two, "push", library, "trunk");
        await f.Git(f.One, "config", "fetch.recurseSubmodules", "yes");
        var sub = Path.Combine(f.One, "sub"); var tracked = await f.Git(sub, "rev-parse", "refs/remotes/origin/trunk");

        var fetch = await f.Execute(SyncAction.Fetch);

        Assert.True(fetch.Outcome == GitWriteOutcome.Succeeded, fetch.Message + fetch.Details);
        Assert.Equal((await f.Head(f.Two), tracked),
            (await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"), await f.Git(sub, "rev-parse", "refs/remotes/origin/trunk")));
    }

    // The upstream was merged or rebased onto and that was undone: its tip is reachable from the branch's reflog only
    // through the undone merge or rebase, never as the branch's own tip, so the branch was not rewritten. The upstream's
    // commits are someone else's, and the pull is offered again as before the first one.
    [Theory]
    [InlineData("merge")]
    [InlineData("rebase")]
    public async Task WhenAnIntegrationOfTheUpstreamWasUndone_ShouldOfferThePullAgain(string integration)
    {
        using var f = await SyncFixture.Create();
        await UndoAnIntegration(f, integration);
        var state = await f.State();
        Assert.Equal((1, 1, false), (state.Ahead, state.Behind, state.Rewritten));
        Assert.True(state.CanPull(await f.Reader.ReadAsync(f.One, f.Token)));
        var pull = await f.Execute(SyncAction.Pull);
        Assert.True(pull.Outcome == GitWriteOutcome.Succeeded, pull.Message + pull.Details);
        Assert.Equal(0, (await f.State()).Behind);
    }

    // A working tree that changed while the pull fetched (here the fetch's own ref update writes to it) stops the merge.
    [Fact]
    public async Task WhenTheWorkingTreeChangesDuringThePullsFetch_ShouldRejectTheMergeAndKeepTheBranch()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        await f.Commit(f.Two, "remote change"); await f.Git(f.Two, "push", "origin", "trunk");
        await f.Hook(f.One, "reference-transaction", "test \"$1\" = committed && echo changed >> shared.txt\nexit 0\n");
        var pull = await f.Execute(SyncAction.Pull);
        Assert.Equal(GitWriteOutcome.Rejected, pull.Outcome);
        Assert.StartsWith("Fetch completed, but", pull.Message);
        Assert.Equal(before, await f.Head(f.One));
    }

    [Fact]
    public async Task WhenAForeignProcessHoldsTheIndexLock_ShouldRejectThePullWithoutDeletingTheLock()
    {
        using var f = await SyncFixture.Create();
        await DirtyTreeAfterARefusedPush(f);
        await f.Execute(SyncAction.Pull);
        await f.Git(f.One, "add", "shared.txt"); await f.Git(f.One, "commit", "-m", "keep local content");
        await f.Git(f.One, "push", "origin", "trunk"); await f.Git(f.Two, "pull", "--ff-only");
        await f.Commit(f.Two, "new remote"); await f.Git(f.Two, "push", "origin", "trunk");
        var path = Path.Combine(f.One, ".git", "index.lock"); await File.WriteAllTextAsync(path, "foreign lock", f.Token);
        var result = await f.Execute(SyncAction.Pull);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Contains("locked", result.Message);
        Assert.Equal("foreign lock", await File.ReadAllTextAsync(path, f.Token));
    }

    [Fact]
    public async Task WhenIncomingCommitsHoldLfsContentWithoutGitLfs_ShouldStopThePullBeforeCheckout()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        await f.Lfs(f.One, installed: false);
        await File.WriteAllTextAsync(Path.Combine(f.Two, ".gitattributes"), "*.bin filter=lfs\n", f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.Two, "content.bin"), "pointer fixture\n", f.Token);
        await f.Git(f.Two, "-c", "filter.lfs.clean=cat", "-c", "filter.lfs.required=false", "add", ".gitattributes", "content.bin");
        await f.Git(f.Two, "commit", "-m", "unsupported incoming content"); await f.Git(f.Two, "push", "--no-verify", "origin", "trunk");
        var progress = new ProgressLog();
        var result = await f.Execute(SyncAction.Pull, progress: progress);
        Assert.Equal((GitWriteOutcome.Rejected, MergeOutcomeText.IncomingLfs("pull")), (result.Outcome, result.Message));
        Assert.DoesNotContain(SyncOutcomeText.FetchingLfs, progress.Reports);
        Assert.Equal(before, await f.Head(f.One)); Assert.False(File.Exists(Path.Combine(f.One, "content.bin")));
    }

    [Fact]
    public async Task WhenAHookOnTheServerRejectsThePush_ShouldReturnItsDetailsAndKeepTheDestination()
    {
        using var f = await SyncFixture.Create(); await f.Hook(f.Bare, "pre-receive", "echo 'Fixture rejects push' >&2\nexit 1\n");
        var before = await f.Head(f.Bare); await f.Commit(f.One, "rejected commit");
        var result = await f.Execute(SyncAction.Push);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome); Assert.Contains("Fixture rejects push", result.Details);
        Assert.Equal(before, await f.Head(f.Bare));
    }

    // An address with a user and a password in it never shows them: not in the sync panel's address, and not in the
    // message or the details of a fetch that cannot reach it.
    [Fact]
    public async Task WhenTheRemoteUrlHoldsCredentials_ShouldShowNoUserInfoInThePanelOrTheDetails()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "config", "remote.origin.url", "https://fixture-user:fixture-secret@127.0.0.1:9/team/repo.git");

        var state = await f.State();
        var fetch = await f.Execute(SyncAction.Fetch);

        Assert.Equal(GitWriteOutcome.Rejected, fetch.Outcome);
        Assert.All(new[] { state.Remote.FetchAddress, state.Remote.PushAddress, fetch.Message, fetch.Details ?? "" }, text =>
        {
            Assert.DoesNotContain("fixture-user", text);
            Assert.DoesNotContain("fixture-secret", text);
        });
        Assert.Contains("127.0.0.1", state.Remote.FetchAddress);
    }

    [Theory]
    [InlineData("remote.origin.mirror", "TRUE", false)]
    [InlineData("remote.origin.mirror", "YeS", false)]
    [InlineData("remote.origin.url", "second.git", true)]
    [InlineData("remote.origin.pushurl", "second.git", true)]
    [InlineData("remote.origin.fetch", "+refs/heads/*:refs/heads/*", false)]
    public async Task WhenTheRemoteConfigurationIsAmbiguousOrUnsafe_ShouldRefuseToFetchOrPush(string key, string value, bool append)
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.Bare);
        if (key.EndsWith("pushurl", StringComparison.Ordinal)) await f.Git(f.One, "config", key, f.Bare);
        await f.Git(f.One, "config", append ? "--add" : "--replace-all", key, value);
        var state = await f.State(); Assert.NotEmpty(state.Remote.Limitation);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Fetch)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal(before, await f.Head(f.Bare)); Assert.Equal(before, await f.Head(f.One));
    }

    // Both clones commit on top of the same trunk, Two pushes first, and One pulls the divergence.
    // One commits a file of its own while Two pushes, One fetches and merges or rebases onto the upstream, then goes back to
    // its own commit and keeps what the integration staged in a stash.
    internal static async Task UndoAnIntegration(SyncFixture f, string integration)
    {
        await File.WriteAllTextAsync(Path.Combine(f.One, "mine.txt"), "mine\n", f.Token);
        await f.Git(f.One, "add", "mine.txt"); await f.Git(f.One, "commit", "-m", "mine");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        await f.Git(f.One, "fetch", "origin");
        if (integration == "merge")
        {
            await f.Git(f.One, "merge", "--no-ff", "--no-edit", "origin/trunk");
            await f.Git(f.One, "reset", "--soft", "HEAD~1");
            await f.Git(f.One, "stash");
        }
        else
        {
            await f.Git(f.One, "rebase", "origin/trunk");
            await f.Git(f.One, "reset", "--hard", "trunk@{1}");
        }
    }

    private static async Task<(string Local, string Remote, GitWriteResult Pull)> PullADivergence(SyncFixture f)
    {
        await f.Commit(f.Two, "remote divergence"); await f.Git(f.Two, "push", "origin", "trunk");
        await f.Commit(f.One, "local divergence"); var local = await f.Head(f.One); var remote = await f.Head(f.Bare);
        return (local, remote, await f.Execute(SyncAction.Pull));
    }

    // The status and the sync state are read, then the push address changes behind them.
    private static async Task<(RepositorySnapshot Snapshot, RepositorySyncState State)> ChangePushTargetAfterRead(SyncFixture f)
    {
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token); var state = await f.State();
        await f.Git(f.One, "remote", "set-url", "--push", "origin", Path.Combine(f.Root, "new destination.git"));
        return (snapshot, state);
    }

    // After the push to the changed target was refused, the push address is restored and shared.txt gets an unstaged edit.
    private static async Task DirtyTreeAfterARefusedPush(SyncFixture f)
    {
        var (snapshot, state) = await ChangePushTargetAfterRead(f);
        await f.Sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Push), f.Token);
        await f.Git(f.One, "config", "--unset-all", "remote.origin.pushurl");
        await File.AppendAllTextAsync(Path.Combine(f.One, "shared.txt"), "unstaged\n", f.Token);
    }
}
