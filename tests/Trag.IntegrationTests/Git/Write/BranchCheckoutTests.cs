using Trag.IntegrationTests.Git.Sync;
using Trag.Domain.Paths;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;

namespace Trag.IntegrationTests.Git.Write;

public sealed class BranchCheckoutTests
{
    // A commit only a remote branch has at its tip is a commit to look at: the checkout detaches there, and makes no
    // local branch that tracks the remote one.
    [Fact]
    public async Task WhenTheCommitIsTheTipOfARemoteBranchOnly_ShouldDetachThereAndMakeNoBranch()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.Two, "remote only"); await f.Git(f.Two, "push", "origin", "trunk:topic");
        await f.Git(f.One, "fetch", "origin");
        var tip = await f.Git(f.One, "rev-parse", "origin/topic");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(new BranchRequest(snapshot, new(tip), BranchAction.Checkout), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal((tip, "HEAD"), (await f.Head(f.One), await f.Git(f.One, "rev-parse", "--abbrev-ref", "HEAD")));
        Assert.Equal("trunk", await f.Git(f.One, "for-each-ref", "--format=%(refname:short)", "refs/heads"));
    }

    [Fact]
    public async Task WhenAnExistingCommitIsCheckedOut_ShouldDetachHeadAndCountTheCommitsMadeThereUntilABranchAdoptsThem()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var first = await f.Head(f.One);
        await f.Commit(f.One, "second"); var second = await f.Head(f.One);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal(0, snapshot.DetachedCommits);

        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("not-a-commit"), BranchAction.Checkout), f.Token)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new(new string('0', 40)), BranchAction.Checkout), f.Token)).Outcome);
        var detach = await writer.ExecuteAsync(new BranchRequest(snapshot, new(first), BranchAction.Checkout), f.Token);
        Assert.True(detach.Outcome == GitWriteOutcome.Succeeded, detach.Message + detach.Details); Assert.Contains("not on a branch", detach.Message);
        Assert.Equal("HEAD", await f.Git(f.One, "rev-parse", "--abbrev-ref", "HEAD"));
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Null(snapshot.Branch); Assert.Equal(first, snapshot.Head); Assert.Equal(0, snapshot.DetachedCommits); Assert.False(snapshot.HasUnsupportedWriteState);

        // Staging and committing work on the detached HEAD; the new commit is on no branch, and the reader counts it.
        await File.WriteAllTextAsync(Path.Combine(f.One, "detached.txt"), "look around\n", f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var staged = await writer.ExecuteAsync(new StageRequest(snapshot, [new GitPath("detached.txt"u8.ToArray())]), f.Token);
        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var committed = await writer.ExecuteAsync(new CommitRequest(snapshot, "Detached work"), f.Token);
        Assert.True(committed.Outcome == GitWriteOutcome.Succeeded, committed.Message + committed.Details);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Null(snapshot.Branch); Assert.Equal(1, snapshot.DetachedCommits);
        var kept = await f.Head(f.One);

        // A branch at HEAD adopts the commit; switching back to trunk then leaves nothing behind.
        var branch = await writer.ExecuteAsync(new BranchRequest(snapshot, new("rescued", null, false), BranchAction.Create), f.Token);
        Assert.True(branch.Outcome == GitWriteOutcome.Succeeded, branch.Message + branch.Details);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Null(snapshot.Branch); Assert.Equal(0, snapshot.DetachedCommits); Assert.Equal(kept, await f.Git(f.One, "rev-parse", "refs/heads/rescued"));
        var back = await writer.ExecuteAsync(new BranchRequest(snapshot, new("trunk"), BranchAction.Switch), f.Token);
        Assert.True(back.Outcome == GitWriteOutcome.Succeeded, back.Message + back.Details);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal("trunk", snapshot.Branch); Assert.Equal(second, snapshot.Head); Assert.Equal(0, snapshot.DetachedCommits);
    }

    // Git refuses this checkout, not Trag's validation. The detached work of the test above comes first, as it did before
    // the split.
    [Fact]
    public async Task WhenACheckoutWouldOverwriteLocalChanges_ShouldRefuseItAndLeaveTheFilesAlone()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var first = await DetachCommitAndSwitchBack(f, writer);

        await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "conflict\n", f.Token);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var refused = await writer.ExecuteAsync(new BranchRequest(snapshot, new(first), BranchAction.Checkout), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, refused.Outcome); Assert.Contains("would be overwritten", refused.Message);
        Assert.Equal("trunk", await f.Git(f.One, "rev-parse", "--abbrev-ref", "HEAD")); Assert.Equal("conflict\n", await File.ReadAllTextAsync(Path.Combine(f.One, "shared.txt"), f.Token));
    }

    // The writes of the detached HEAD test without its checks: two refused checkouts, the detach to the first commit, a
    // commit there, the branch that adopts it and the switch back to trunk. Answers the first commit.
    private static async Task<string> DetachCommitAndSwitchBack(SyncFixture f, GitWriteRouter writer)
    {
        var first = await f.Head(f.One);
        await f.Commit(f.One, "second");
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("not-a-commit"), BranchAction.Checkout), f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new(new string('0', 40)), BranchAction.Checkout), f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new(first), BranchAction.Checkout), f.Token);
        await File.WriteAllTextAsync(Path.Combine(f.One, "detached.txt"), "look around\n", f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new StageRequest(snapshot, [new GitPath("detached.txt"u8.ToArray())]), f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new CommitRequest(snapshot, "Detached work"), f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("rescued", null, false), BranchAction.Create), f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("trunk"), BranchAction.Switch), f.Token);
        return first;
    }
}
