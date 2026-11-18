using static Trag.IntegrationTests.Git.Sync.SyncFixture;
using static Trag.IntegrationTests.Git.Write.MergeScenario;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.IntegrationTests.Git.Sync;

namespace Trag.IntegrationTests.Git.Write;

// A pull that merges: the commit it creates, the merge it leaves pending when a hook says no, and what an
// interruption around the merge commit leaves behind for the verification to find.
public sealed class MergePullTests
{
    [Fact]
    public async Task WhenADivergedBranchIsPulledCleanly_ShouldCreateACommitWithBothParentsAndPushIt()
    {
        using var f = await SyncFixture.Create();
        await DivergeCleanly(f);
        var parents = await f.Head(f.One) + " " + await f.Head(f.Two);
        await f.Git(f.One, "config", "branch.trunk.mergeOptions", "--squash --no-commit");
        var pull = await f.Execute(SyncAction.Pull);
        Assert.True(pull.Outcome == GitWriteOutcome.Succeeded, pull.Message + pull.Details);
        Assert.Equal(parents, await f.Git(f.One, "show", "-s", "--format=%P", "HEAD"));
        Assert.Equal(await f.Head(f.One), pull.CommitId);
        Assert.Empty((await f.Reader.ReadAsync(f.One, f.Token)).Files);
        Assert.Equal(GitWriteOutcome.Succeeded, (await f.Execute(SyncAction.Push)).Outcome);
        Assert.Equal(await f.Head(f.One), await f.Head(f.Bare));
    }

    // A branch named other than its upstream: the pull merges what trunk brings into the branch checked out, as for a
    // branch named after its upstream.
    [Fact]
    public async Task WhenPullNeedsAMergeCommitOnABranchWithAnotherUpstreamName_ShouldMergeTheUpstreamIntoTheBranch()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "branch", "--move", "trunk", "local");
        await DivergeCleanly(f);
        var parents = await f.Head(f.One) + " " + await f.Head(f.Two);

        var pull = await f.Execute(SyncAction.Pull);

        Assert.True(pull.Outcome == GitWriteOutcome.Succeeded, pull.Message + pull.Details);
        Assert.Equal((parents, "local"), (await f.Git(f.One, "show", "-s", "--format=%P", "HEAD"), await f.Git(f.One, "branch", "--show-current")));
    }

    // The pull merges the tip its fetch read by id; the merge commit still names the upstream it came from, as Git's merge
    // of that branch would, and not the id.
    [Fact]
    public async Task WhenAPullMakesAMergeCommit_ShouldNameTheUpstreamInItsMessage()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "branch", "--move", "trunk", "local");
        await DivergeCleanly(f);

        var pull = await f.Execute(SyncAction.Pull);

        Assert.True(pull.Outcome == GitWriteOutcome.Succeeded, pull.Message + pull.Details);
        Assert.Equal("Merge remote-tracking branch 'origin/trunk' into local", await f.Git(f.One, "show", "-s", "--format=%B", "HEAD"));
    }

    [Fact]
    public async Task WhenAHookRefusesTheMergeCommitOfAPull_ShouldLeaveTheCleanMergePendingForAnExplicitFinish()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "commit", "--allow-empty", "-m", "local");
        await f.Commit(f.Two, "remote"); await f.Git(f.Two, "push", "origin", "trunk");
        await f.Hook(f.One, "pre-merge-commit", "echo 'fixture rejected merge' >&2\nexit 1\n");
        var result = await f.Execute(SyncAction.Pull);
        Assert.True(result.Outcome == GitWriteOutcome.NeedsResolution, result.Message + result.Details);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.True(snapshot.HasSupportedMerge); Assert.DoesNotContain(snapshot.Files, file => file.IsConflict);
        Assert.Equal(GitWriteOutcome.Succeeded, (await FinishMerge(f, "Finish reviewed merge")).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenFinishingAMergeIsCancelledAroundItsCommit_ShouldVerifyWhetherTheCommitWasCreated(bool afterCommit)
    {
        using var f = await Conflict();
        await File.WriteAllTextAsync(Path.Combine(f.One, "shared.txt"), "resolved\n", f.Token);
        await StageResolution(f, "shared.txt");
        var marker = Path.Combine(f.Root, "commit-started");
        await f.Hook(f.One, afterCommit ? "post-commit" : "pre-commit", "sleep 30 & echo ready > " + Quote(marker) + "\nwait\n");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var task = FinishMerge(f, "Interrupted merge", cancel.Token);
        await WaitFor(marker, f.Token); cancel.Cancel(); var result = await task;
        Assert.True(result.Outcome == (afterCommit ? GitWriteOutcome.Succeeded : GitWriteOutcome.Cancelled), result.Message + result.Details);
        Assert.Equal(afterCommit ? RepositoryOperation.None : RepositoryOperation.Merge, result.Snapshot!.Operation);
        Assert.False(File.Exists(Path.Combine(f.One, ".git", "index.lock")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenADivergedPullIsInterruptedAroundItsMergeCommit_ShouldVerifyTheCommitOrBlockWritesForRecovery(bool afterCommit)
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "commit", "--allow-empty", "-m", "local");
        await f.Commit(f.Two, "remote"); await f.Git(f.Two, "push", "origin", "trunk");
        var local = await f.Head(f.One); var incoming = await f.Head(f.Two); var marker = Path.Combine(f.Root, "merge-started");
        await f.Hook(f.One, afterCommit ? "post-merge" : "pre-merge-commit", "sleep 30 & echo ready > " + Quote(marker) + "\nwait\n");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var pending = f.Execute(SyncAction.Pull, token: cancel.Token);
        await WaitFor(marker, f.Token); cancel.Cancel(); var result = await pending;
        Assert.True(result.Outcome == (afterCommit ? GitWriteOutcome.Succeeded : GitWriteOutcome.Uncertain), result.Message + result.Details);
        var actual = await f.Reader.ReadAsync(f.One, f.Token);
        if (afterCommit)
        {
            Assert.Equal(local + " " + incoming, await f.Git(f.One, "show", "-s", "--format=%P", "HEAD")); Assert.Empty(actual.Files);
        }
        else
        {
            Assert.Equal(local, actual.Head); Assert.Contains(actual.Limitations, text => text.Contains("recovery index", StringComparison.Ordinal));
            Assert.Equal(GitWriteOutcome.Rejected, (await FinishMerge(f, "must not continue uncertain index")).Outcome);
        }
        Assert.False(File.Exists(Path.Combine(f.One, ".git", "index.lock")));
    }
}
