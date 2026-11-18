using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.IntegrationTests.Git.Sync;

// Fetch, pull and push between two clones of one origin: the counts, the verified destination, and the choice of
// remote and branch when the repository does not make it for the user.
public sealed class SyncExchangeTests
{
    [Fact]
    public async Task WhenTwoClonesExchangeCommits_ShouldCountAfterTheFetchFastForwardThePullAndVerifyThePush()
    {
        using var f = await SyncFixture.Create();
        var initial = await f.Head(f.One);
        var state = await f.State(); Assert.Equal(0, state.Ahead); Assert.Equal(0, state.Behind);
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        Assert.Equal(0, (await f.State()).Behind); // Local information remains explicitly stale until fetch.
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Fetch)).Outcome);
        state = await f.State(); Assert.Equal(1, state.Behind); Assert.Equal(initial, await f.Head(f.One));
        var result = await f.Execute(SyncAction.Pull);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.Two), await f.Head(f.One)); Assert.Empty((await f.Reader.ReadAsync(f.One, f.Token)).Files);
        await f.Commit(f.One, "from Trag");
        await f.Hook(f.One, "pre-push", "test \"$1\" = origin || exit 1\n");
        Assert.Equal(1, (await f.State()).Ahead);
        result = await f.Execute(SyncAction.Push);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.One), await f.Head(f.Bare)); Assert.Equal(0, (await f.State()).Ahead);
        Assert.Equal("origin", await f.Git(f.One, "remote"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.One, ".git"), "trag-write-*"));
    }

    // A submodule in the incoming commits is merged like any other change; checking it out is left to Update submodules.
    [Fact]
    public async Task WhenIncomingCommitsAddASubmodule_ShouldPullThemAndListTheSubmoduleForUpdate()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.Two, "-c", "protocol.file.allow=always", "submodule", "add", "--", new Uri(f.Bare).AbsoluteUri, "nested");
        await f.Git(f.Two, "commit", "-m", "Add nested"); await f.Git(f.Two, "push", "--no-verify", "origin", "trunk");

        var result = await f.Execute(SyncAction.Pull);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.Two), await f.Head(f.One));
        Assert.Equal(["nested"], (await f.Reader.ReadAsync(f.One, f.Token)).SubmodulesToUpdate);
    }

    // A push that finds the commit already there, sent by another tool, records it as an arrival does, so the branch shows
    // up to date without a fetch and the next Push does not say the same again. The remote takes the commit itself, since
    // from Git 2.56 a push to the remote's address moves its tracking ref too.
    [Fact]
    public async Task WhenPushFindsTheCommitAlreadyThere_ShouldMoveTheTrackingRefAndShowUpToDate()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "sent elsewhere");
        await f.Git(f.Bare, "fetch", f.One, "HEAD:refs/heads/trunk");
        Assert.Equal(1, (await f.State()).Ahead);

        var result = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Succeeded, SyncOutcomeText.AlreadyThere), (result.Outcome, result.Message));
        Assert.Equal(0, (await f.State()).Ahead);
    }

    // A pull whose fetch brought nothing the branch lacks stops after the fetch, without a merge.
    [Fact]
    public async Task WhenThePullsFetchBringsNothingNew_ShouldSayTheBranchHasTheUpstreamAndNotMerge()
    {
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        var pull = await f.Execute(SyncAction.Pull);
        Assert.Equal((GitWriteOutcome.Succeeded, "Fetch completed. Your branch already contains the upstream commits."), (pull.Outcome, pull.Message));
        Assert.Equal(before, await f.Head(f.One)); Assert.False(File.Exists(Path.Combine(f.One, ".git", "ORIG_HEAD")));
    }

    [Fact]
    public async Task WhenTheBranchHasNoUpstream_ShouldPushOnlyToAnExplicitlySelectedBranchWithoutRecordingIt()
    {
        using var f = await SyncFixture.Create(); await f.Git(f.One, "branch", "--unset-upstream");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var state = await f.Sync.ReadAsync(snapshot, "origin", null, f.Token);
        Assert.Null(state.Branch); Assert.Null(state.Ahead); Assert.Null(state.Behind); Assert.Single(state.Branches);
        var rejected = await f.Sync.ExecuteAsync(new SyncRequest(snapshot, state, SyncAction.Push), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, rejected.Outcome);
        await f.Commit(f.One, "explicit selected target");
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Push, branch: "refs/heads/trunk")).Outcome);
        Assert.Null((await f.Reader.ReadAsync(f.One, f.Token)).TrackingRemote);
    }

    [Fact]
    public async Task WhenAnAccountIsSavedWithoutAnAccountService_ShouldRefuseItAndFetchNothing()
    {
        // Without an account store there is nothing to save; the request must not fall through into a fetch or a pull.
        using var f = await SyncFixture.Create(); var before = await f.Head(f.One);
        await f.Commit(f.Two, "not to be pulled"); await f.Git(f.Two, "push", "origin", "trunk");
        var result = await f.Execute(SyncAction.SelectAccount);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        var state = await f.State(); Assert.Equal(0, state.Behind);
        Assert.Equal(before, await f.Head(f.One));
    }

    [Fact]
    public async Task WhenThereAreSeveralRemotesAndASeparatePushAddress_ShouldNeverPushToTheFetchDestination()
    {
        using var f = await SyncFixture.Create(); var other = Path.Combine(f.Root, "publish.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, other);
        await f.Git(f.One, "remote", "add", "backup", other);
        await f.Git(f.One, "remote", "set-url", "--push", "origin", other);
        var original = await f.Head(f.Bare); await f.Commit(f.One, "publish only");
        var hookMarker = Path.Combine(f.Root, "explicit-push-hook");
        await f.Hook(f.One, "pre-push", "printf '%s\\n' \"$2\" > " + Quote(hookMarker) + "\n");
        var state = await f.State(); Assert.NotEqual(state.Remote.FetchAddress, state.Remote.PushAddress);
        var pushed = await f.Execute(SyncAction.Push);
        Assert.Equal((GitWriteOutcome.Succeeded, SyncOutcomeText.ComparedWithFetch(SyncOutcomeText.Pushed, state.Remote.FetchAddress)), (pushed.Outcome, pushed.Message));
        Assert.Equal(original, await f.Head(f.Bare)); Assert.Equal(await f.Head(f.One), await f.Head(other));
        Assert.Contains("publish.git", await File.ReadAllTextAsync(hookMarker, f.Token));
        Assert.Equal(1, (await f.State()).Ahead); // Counts still compare with the separate fetch endpoint, as the message says.
        var again = await f.Execute(SyncAction.Push);
        Assert.Equal((GitWriteOutcome.Succeeded, SyncOutcomeText.ComparedWithFetch(SyncOutcomeText.AlreadyThere, state.Remote.FetchAddress)), (again.Outcome, again.Message));
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Fetch, remote: "backup")).Outcome);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Null((await f.Sync.ReadAsync(snapshot, "backup", null, f.Token)).Branch);
        Assert.Equal(0, (await f.Sync.ReadAsync(snapshot, "backup", "refs/heads/trunk", f.Token)).Ahead);
    }

    // A teammate pushes after Trag read the destination and before its push arrives: the push is no force, so Git refuses
    // to replace the teammate's commit, and since the destination is neither where it was nor the pushed commit, the
    // result says it changed instead of claiming either.
    [Fact]
    public async Task WhenAPeerPushesBetweenLsRemoteAndPush_ShouldReportUncertainAndKeepThePeersCommit()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "mine"); await f.Commit(f.Two, "theirs");
        var pushed = false;
        var between = new OnReport(text =>
        {
            if (pushed || text != Trag.Domain.Presentation.Text.SyncOutcomeText.Pushing(false)) return;
            f.Git(f.Two, "push", "origin", "trunk").GetAwaiter().GetResult();
            pushed = true;
        });
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var result = await f.Sync.ExecuteAsync(new SyncRequest(snapshot, await f.Sync.ReadAsync(snapshot, "origin", null, f.Token), SyncAction.Push) { Progress = between }, f.Token);

        Assert.True(pushed);
        Assert.Equal((GitWriteOutcome.Uncertain, Trag.Domain.Presentation.Text.SyncOutcomeText.DestinationChanged), (result.Outcome, result.Message));
        Assert.Equal(await f.Head(f.Two), await f.Head(f.Bare));
    }

    // Git rewrites an address once. A push address that a second insteadOf rule would rewrite again names one destination
    // for Git and another for the alias Trag pushes through, so the push is refused before anything is sent, and the
    // repository the second rewrite names receives nothing.
    [Fact]
    public async Task WhenThePushAddressIsRewrittenTwice_ShouldRefuseAndSendNothing()
    {
        using var f = await SyncFixture.Create(); await f.Commit(f.One, "mine");
        var elsewhere = Path.Combine(f.Root, "elsewhere.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, elsewhere);
        var before = await f.Head(elsewhere);
        await f.Git(f.One, "config", "url." + f.Root.Replace('\\', '/') + "/.insteadOf", "first:");
        await f.Git(f.One, "config", "url.first:.insteadOf", "second:");
        await f.Git(f.One, "remote", "set-url", "--push", "origin", "second:elsewhere.git");

        var result = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Rejected, SyncOutcomeText.NotFinished), (result.Outcome, result.Message));
        Assert.Equal(before, await f.Head(elsewhere));
    }

    [Fact]
    public async Task WhenExtraBranchesTagsAndAForceRefspecAreConfigured_ShouldPushOnlyTheSelectedBranchWithoutForce()
    {
        using var f = await SyncFixture.Create(); await f.Commit(f.One, "only selected branch");
        await f.Git(f.One, "branch", "extra");
        await f.Git(f.One, "-c", "tag.gpgsign=false", "tag", "-a", "private-tag", "-m", "local only");
        await f.Git(f.One, "config", "push.followTags", "true");
        await f.Git(f.One, "config", "remote.origin.push", "+refs/heads/*:refs/heads/*");
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal("refs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
        await f.Commit(f.Two, "concurrent remote commit");
        await f.Git(f.Two, "push", "--force", "origin", "trunk"); // Fixture creates a non-fast-forward destination.
        var remote = await f.Head(f.Bare);
        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal(remote, await f.Head(f.Bare));
    }

    private sealed class OnReport(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
