using Trag.IntegrationTests.Git.Sync;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class BranchDeleteTests
{
    [Fact]
    public async Task WhenLocalBranchesAreDeleted_ShouldDeleteMergedOnesAtOnceAndAskBeforeForcingUnmergedOnes()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        var lost = await Branches(f);
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);

        var current = await writer.ExecuteAsync(new BranchRequest(snapshot, new("trunk"), BranchAction.Delete), f.Token);
        Assert.Equal(GitWriteOutcome.Rejected, current.Outcome); Assert.Contains("Switch to another branch", current.Message);
        var merged = await writer.ExecuteAsync(new BranchRequest(snapshot, new("merged"), BranchAction.Delete), f.Token);
        Assert.True(merged.Outcome == GitWriteOutcome.Succeeded, merged.Message + merged.Details);
        var names = (await f.Git(f.One, "branch", "--no-column", "--format=%(refname:short)")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain("merged", names); Assert.Contains("unmerged", names);

        // Git decides what is unmerged; the writer reports it as a decision instead of deleting.
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var refused = await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged"), BranchAction.Delete), f.Token);
        Assert.Equal(GitWriteOutcome.NeedsResolution, refused.Outcome); Assert.Contains("not fully merged", refused.Message);
        Assert.Equal(lost, await f.Git(f.One, "rev-parse", "refs/heads/unmerged"));
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var forced = await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged", ForceAt: lost), BranchAction.Delete), f.Token);
        Assert.True(forced.Outcome == GitWriteOutcome.Succeeded, forced.Message + forced.Details); Assert.Contains(lost[..8], forced.Message);
        Assert.Equal("trunk", await f.Git(f.One, "branch", "--no-column", "--format=%(refname:short)"));
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged"), BranchAction.Delete), f.Token)).Outcome);
    }

    // The deletion goes through the transport. The local deletions of the test above come first, as they did before the
    // split.
    [Fact]
    public async Task WhenARemoteBranchIsDeleted_ShouldVerifyItIsGoneAndDropItsTrackingBranch()
    {
        using var f = await SyncFixture.Create();
        var writer = new GitWriteRouter(new(), f.Sync);
        await Branches(f);
        await DeleteLocalBranches(f, writer);

        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Equal(await f.Head(f.One), await f.Git(f.Bare, "rev-parse", "refs/heads/topic"));
        var remote = await writer.ExecuteAsync(new RemoteBranchDeleteRequest(snapshot, "origin", "topic"), f.Token);
        Assert.True(remote.Outcome == GitWriteOutcome.Succeeded, remote.Message + remote.Details); Assert.Contains("origin/topic was deleted", remote.Message);
        Assert.Equal("refs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
        Assert.DoesNotContain("origin/topic", await f.Git(f.One, "branch", "-r"));
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        var missing = await writer.ExecuteAsync(new RemoteBranchDeleteRequest(snapshot, "origin", "topic"), f.Token);
        Assert.True(missing.Outcome == GitWriteOutcome.Succeeded, missing.Message); Assert.Contains("no longer exists", missing.Message);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new RemoteBranchDeleteRequest(snapshot, "nowhere", "trunk"), f.Token)).Outcome);
        Assert.Equal(GitWriteOutcome.Rejected, (await writer.ExecuteAsync(new RemoteBranchDeleteRequest(snapshot, "origin", "bad name"), f.Token)).Outcome);
        Assert.Equal("refs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)"));
    }

    // A teammate pushes to the branch after Trag read where it was and before the deletion arrives: the deletion goes only
    // while the branch is still where Trag read it, so the teammate's commit stays on the remote.
    [Fact]
    public async Task WhenTheRemoteBranchMovesBeforeTheDeletionArrives_ShouldRefuseAndKeepTheNewCommit()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "push", "origin", "trunk:topic"); await f.Git(f.One, "fetch", "origin");
        await f.Git(f.Two, "fetch", "origin"); await f.Git(f.Two, "switch", "-c", "topic", "origin/topic"); await f.Commit(f.Two, "teammate");
        var teammate = await f.Head(f.Two);
        var pushed = false;
        var between = new OnReport(text =>
        {
            if (pushed || text != RemoteBranchDeleteText.Deleting("topic", "origin")) return;
            f.Git(f.Two, "push", "origin", "topic").GetAwaiter().GetResult();
            pushed = true;
        });

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "topic") { Progress = between }, f.Token);

        Assert.True(pushed);
        Assert.NotEqual(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(teammate, await f.Git(f.Bare, "rev-parse", "refs/heads/topic"));
    }

    // The remote pushes somewhere else than it fetches from, as a fork set up with pushurl does: the branch goes where the
    // push goes, and the remote-tracking branch, which follows the fetch side where the branch is still there, stays.
    [Fact]
    public async Task WhenTheRemotePushesElsewhereThanItFetches_ShouldDeleteThereAndKeepTheTrackingBranch()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "push", "origin", "trunk:topic"); await f.Git(f.One, "fetch", "origin");
        var pushSide = Path.Combine(f.Root, "push-side.git");
        await f.Git(f.Root, "clone", "--bare", f.Bare, pushSide);
        await f.Git(f.One, "config", "remote.origin.pushurl", pushSide);
        var tracked = await f.Git(f.One, "rev-parse", "refs/remotes/origin/topic");

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "topic"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("refs/heads/trunk", await f.Git(pushSide, "for-each-ref", "--format=%(refname)", "refs/heads"));
        Assert.Equal(tracked, await f.Git(f.One, "rev-parse", "refs/remotes/origin/topic"));
    }

    // The fetch mapping keeps the remote's branches under feature/: origin/feature/x stands for the remote's x, and the
    // remote's own feature/x is origin/feature/feature/x.
    [Fact]
    public async Task WhenTheFetchMappingRenamesBranches_ShouldDeleteTheBranchTheTrackingRefStandsFor()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "push", "origin", "trunk:x", "trunk:feature/x");
        await f.Git(f.One, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/feature/*");
        await f.Git(f.One, "fetch", "origin");

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "feature/x"), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("refs/heads/feature/x\nrefs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)", "refs/heads"));
        Assert.Equal("refs/remotes/origin/feature/feature/x\nrefs/remotes/origin/feature/trunk",
            await f.Git(f.One, "for-each-ref", "--format=%(refname)", "refs/remotes/origin/feature"));
    }

    // A remote-tracking branch no fetch mapping writes stands for no branch Trag can name on the remote.
    [Fact]
    public async Task WhenNoFetchMappingWritesTheTrackingRef_ShouldRefuseAndDeleteNothing()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "push", "origin", "trunk:stale");
        await f.Git(f.One, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/feature/*");
        await f.Git(f.One, "update-ref", "refs/remotes/origin/stale", "HEAD");

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "stale"), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RemoteBranchDeleteText.NotFetched("origin/stale", "origin")), (result.Outcome, result.Message));
        Assert.Equal("refs/heads/stale\nrefs/heads/trunk", await f.Git(f.Bare, "for-each-ref", "--format=%(refname)", "refs/heads"));
    }

    // A branch checked out in another worktree cannot go: the refusal says where it is in use, not only Git's words.
    [Fact]
    public async Task WhenTheBranchIsCheckedOutInAnotherWorktree_ShouldSayToRemoveThatWorktreeFirst()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "branch", "elsewhere");
        await f.Git(f.One, "worktree", "add", Path.Combine(f.Root, "other-tree"), "elsewhere");

        var refused = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new BranchRequest(await f.Reader.ReadAsync(f.One, f.Token), new("elsewhere"), BranchAction.Delete), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, BranchOutcomeText.InOtherWorktree("elsewhere")), (refused.Outcome, refused.Message));
        Assert.Contains("elsewhere", await f.Git(f.One, "branch", "--no-column", "--format=%(refname:short)"));
    }

    // Git's -d also takes a branch whose commits only its upstream holds, HEAD not: nothing is lost, so it goes at once.
    [Fact]
    public async Task WhenTheBranchIsMergedIntoItsUpstreamButNotHead_ShouldDeleteAtOnce()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "switch", "-c", "pushed"); await f.Commit(f.One, "on the remote"); var tip = await f.Head(f.One);
        await f.Git(f.One, "push", "-u", "origin", "pushed"); await f.Git(f.One, "switch", "trunk");

        var deleted = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new BranchRequest(await f.Reader.ReadAsync(f.One, f.Token), new("pushed"), BranchAction.Delete), f.Token);

        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + deleted.Details);
        Assert.Equal("trunk", await f.Git(f.One, "branch", "--no-column", "--format=%(refname:short)"));
        Assert.Equal(tip, await f.Git(f.One, "rev-parse", "refs/remotes/origin/pushed"));
    }

    // The remote cannot be read before the deletion: nothing is sent, and the refusal says the remote could not be read.
    [Fact]
    public async Task WhenLsRemoteFails_ShouldSayTheRemoteCouldNotBeReadAndDeleteNothing()
    {
        using var f = await SyncFixture.Create();
        await f.Git(f.One, "push", "origin", "trunk:topic"); await f.Git(f.One, "fetch", "origin");
        var away = f.Bare + "-away";
        Directory.Move(f.Bare, away);

        var result = await new GitWriteRouter(new(), f.Sync).ExecuteAsync(
            new RemoteBranchDeleteRequest(await f.Reader.ReadAsync(f.One, f.Token), "origin", "topic"), f.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.StartsWith("The remote could not be read.", result.Message); Assert.EndsWith("Nothing was deleted.", result.Message);
        Assert.Equal(await f.Git(f.One, "rev-parse", "trunk"), await f.Git(away, "rev-parse", "refs/heads/topic"));
        Assert.Equal(await f.Git(f.One, "rev-parse", "trunk"), await f.Git(f.One, "rev-parse", "refs/remotes/origin/topic"));
    }

    private sealed class OnReport(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    // merged sits on trunk, unmerged has a commit of its own, and origin has a topic branch at trunk that One has fetched.
    // Answers the commit only unmerged has.
    private static async Task<string> Branches(SyncFixture f)
    {
        await f.Git(f.One, "branch", "merged");
        await f.Git(f.One, "switch", "-c", "unmerged"); await f.Commit(f.One, "only here"); var lost = await f.Head(f.One);
        await f.Git(f.One, "switch", "trunk");
        await f.Git(f.One, "push", "origin", "trunk:topic"); await f.Git(f.One, "fetch", "origin");
        return lost;
    }

    // The local deletions of the first test without their checks: the current branch refused, merged deleted, unmerged
    // asked about, forced, and refused once more over the same snapshot.
    private static async Task DeleteLocalBranches(SyncFixture f, GitWriteRouter writer)
    {
        var snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("trunk"), BranchAction.Delete), f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("merged"), BranchAction.Delete), f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged"), BranchAction.Delete), f.Token);
        snapshot = await f.Reader.ReadAsync(f.One, f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged", ForceAt: await f.Git(f.One, "rev-parse", "refs/heads/unmerged")), BranchAction.Delete), f.Token);
        await writer.ExecuteAsync(new BranchRequest(snapshot, new("unmerged"), BranchAction.Delete), f.Token);
    }

    // Cancel that stops git branch -d before it deleted anything leaves the branch, and the delete says so in its own words.
    [Fact]
    public async Task WhenTheDeleteIsCanceled_ShouldReportDeleteInterrupted()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "topic");
        var hooks = Path.Combine(f.Container, "hooks");
        var ready = Path.Combine(f.Root, ".git", "trag-ready");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "reference-transaction"),
            "#!/bin/sh\nupdates=$(cat)\n[ \"$1\" = prepared ] || exit 0\necho ready > '" + ready.Replace('\\', '/') + "'\nsleep 30\n", f.Token);
        await f.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
        using var cancellation = new CancellationTokenSource();

        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Delete), cancellation.Token);
        while (!File.Exists(ready) && !pending.IsCompleted) await Task.Delay(20, f.Token);
        await cancellation.CancelAsync();
        var result = await pending;

        Assert.Equal((GitWriteOutcome.Cancelled, BranchOutcomeText.DeleteInterrupted("topic")), (result.Outcome, result.Message));
        Assert.Equal(0, (await f.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/topic")).ExitCode);
    }

    // The user agreed to lose the commits the branch had when the question came. A commit added to it since, from a
    // terminal or another Trag, is not among them: the delete goes unforced, and Git keeps the branch as unmerged.
    [Fact]
    public async Task WhenTheBranchMovedWhileTheForceQuestionWasOpen_ShouldNotDeleteTheNewCommits()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("switch", "-q", "-c", "topic");
        await f.CommitAsync("unmerged");
        await f.GitAsync("switch", "-q", "main");
        var shown = new BranchInfo("topic", "refs/heads/topic", (await f.GitAsync("rev-parse", "refs/heads/topic")).Trim(), false);
        var writer = new GitWriteRouter(f.Runner);
        var asked = await writer.ExecuteAsync(BranchMoves.Delete(await f.ReadAsync(), shown, confirmed: false), f.Token);
        var later = (await f.GitAsync("commit-tree", "-p", "refs/heads/topic", "-m", "later", "refs/heads/topic^{tree}")).Trim();
        await f.GitAsync("update-ref", "refs/heads/topic", later);

        var confirmed = await writer.ExecuteAsync(BranchMoves.Delete(await f.ReadAsync(), shown, confirmed: true), f.Token);

        Assert.Equal(GitWriteOutcome.NeedsResolution, asked.Outcome);
        Assert.Equal((GitWriteOutcome.NeedsResolution, BranchOutcomeText.NotFullyMerged("topic")), (confirmed.Outcome, confirmed.Message));
        Assert.Equal(later, (await f.GitAsync("rev-parse", "refs/heads/topic")).Trim());
    }

    // Git removes the branch's section of the configuration after its ref, through a lock of the whole file. A Git stopped
    // there leaves that lock, which refuses every later change of the configuration, and the section, which a new branch of
    // the same name would take for its own upstream; the write removes both as Git would have.
    [Fact]
    public async Task WhenTheDeleteIsCanceledWhileGitRewritesTheConfiguration_ShouldLeaveNoLockNorTheBranchsSettings()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "--track", "topic", "main");
        await ConfigurationLock.LengthenAsync(f);

        var result = await ConfigurationLock.CancelWhileHeldAsync(f, new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Delete));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Outcome + " " + result.Message + result.Details);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "config.lock")));
        Assert.Equal(1, (await f.RunAsync("config", "--local", "--get-regexp", "^branch\\.topic\\.")).ExitCode);
    }

    // A configuration lock another program holds lets Git delete the branch, with only a warning, but not its section, and
    // Trag cannot remove the section either, so a new branch of the same name would take that upstream for its own.
    [Fact]
    public async Task WhenAnotherProgramHoldsTheConfigurationLock_ShouldSayTheDeleteIsNotVerified()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("one");
        await f.GitAsync("branch", "--track", "topic", "main");
        await File.WriteAllTextAsync(Path.Combine(f.Root, ".git", "config.lock"), "", f.Token);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Delete), f.Token);

        Assert.Equal((GitWriteOutcome.Uncertain, BranchOutcomeText.DeleteNotVerified), (result.Outcome, result.Message));
    }
}
