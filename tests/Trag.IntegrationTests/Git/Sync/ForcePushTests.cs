using Trag.Domain.Writes;

namespace Trag.IntegrationTests.Git.Sync;

// Replacing the remote branch with a rewritten one, with real Git between two clones of one origin: a push refuses the
// rewrite, the force push replaces the branch the user was shown, and nothing is sent when the remote moved since the
// last fetch or holds commits the branch never had.
public sealed class ForcePushTests
{
    [Fact]
    public async Task WhenThePushedBranchWasRewritten_ShouldReplaceItsUpstreamOnlyWithAForcePush()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "mine"); await f.Git(f.One, "push", "origin", "trunk");
        var pushed = await f.Head(f.Bare);
        await f.Git(f.One, "commit", "--amend", "-m", "mine, reworded");
        var state = await f.State();
        Assert.Equal((1, 1, true), (state.Ahead, state.Behind, state.Rewritten));
        Assert.True(state.CanForcePush(await f.Reader.ReadAsync(f.One, f.Token)));

        Assert.Equal(GitWriteOutcome.Rejected, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal(pushed, await f.Head(f.Bare));

        var result = await f.Execute(SyncAction.ForcePush);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.One), await f.Head(f.Bare));
        state = await f.State();
        Assert.Equal((0, 0, false), (state.Ahead, state.Behind, state.Rewritten));
    }

    [Fact]
    public async Task WhenTheRemoteMovedSinceTheLastFetch_ShouldRefuseTheForcePushAndSendNothing()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "mine"); await f.Git(f.One, "push", "origin", "trunk");
        await f.Git(f.One, "commit", "--amend", "-m", "mine, reworded");
        await f.Git(f.Two, "pull", "--ff-only", "origin", "trunk");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        var peer = await f.Head(f.Two);
        // One has not fetched: it still sees its own old commit on the remote, which it once contained.
        Assert.True((await f.State()).Rewritten);

        var result = await f.Execute(SyncAction.ForcePush);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.StartsWith("The remote branch moved since your last fetch.", result.Message);
        Assert.Equal(peer, await f.Head(f.Bare));
    }

    // With a separate push address the tip the force push may replace is read where the push goes: a peer's commit there
    // is kept, though the fetch address still holds the tip the user was shown.
    [Fact]
    public async Task WhenThePushUrlDiffersFromTheFetchUrl_ShouldCompareTheLeaseOnThePushSide()
    {
        using var f = await SyncFixture.Create();
        var mirror = Path.Combine(f.Root, "publish.git");
        await f.Commit(f.One, "mine"); await f.Git(f.One, "push", "origin", "trunk");
        await f.Git(f.Root, "clone", "--bare", f.Bare, mirror);
        await f.Git(f.One, "remote", "set-url", "--push", "origin", mirror);
        await f.Git(f.One, "commit", "--amend", "-m", "mine, reworded");
        await f.Git(f.Two, "pull", "--ff-only", "origin", "trunk");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", mirror, "trunk");
        var peer = await f.Head(f.Two);
        Assert.True((await f.State()).Rewritten);

        var result = await f.Execute(SyncAction.ForcePush);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.StartsWith("The remote branch moved since your last fetch.", result.Message);
        Assert.Equal(peer, await f.Head(mirror));
    }

    // An undone merge or rebase onto the upstream leaves its tip in the reflog, but never as the branch's own tip: the
    // remote's commits are someone else's, and replacing them would delete them.
    [Theory]
    [InlineData("merge")]
    [InlineData("rebase")]
    public async Task WhenAnIntegrationOfTheUpstreamWasUndone_ShouldRefuseTheForcePushAndKeepTheRemoteCommits(string integration)
    {
        using var f = await SyncFixture.Create();
        await SyncRefusalTests.UndoAnIntegration(f, integration);
        var peer = await f.Head(f.Bare);
        Assert.False((await f.State()).CanForcePush(await f.Reader.ReadAsync(f.One, f.Token)));

        var result = await f.Execute(SyncAction.ForcePush);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.StartsWith("The remote branch has commits this branch never had.", result.Message);
        Assert.Equal(peer, await f.Head(f.Bare));
    }

    [Fact]
    public async Task WhenFetchedCommitsWereNeverIntegrated_ShouldRefuseTheForcePushAndKeepThem()
    {
        using var f = await SyncFixture.Create();
        await f.Commit(f.One, "mine"); await f.Git(f.One, "push", "origin", "trunk");
        await f.Git(f.Two, "pull", "--ff-only", "origin", "trunk");
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        var peer = await f.Head(f.Two);
        await f.Git(f.One, "commit", "--amend", "-m", "mine, reworded");
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Fetch)).Outcome);
        var state = await f.State();
        Assert.Equal((1, 2, false), (state.Ahead, state.Behind, state.Rewritten));
        Assert.False(state.CanForcePush(await f.Reader.ReadAsync(f.One, f.Token)));

        var result = await f.Execute(SyncAction.ForcePush);
        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.StartsWith("The remote branch has commits this branch never had.", result.Message);
        Assert.Equal(peer, await f.Head(f.Bare));
    }
}
