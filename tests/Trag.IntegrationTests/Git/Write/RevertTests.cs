using Trag.Domain;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class RevertTests
{
    // The new commit sits on the current branch and undoes exactly the reverted commit; what came after it stays.
    [Fact]
    public async Task WhenACommitIsReverted_ShouldAddACommitThatUndoesItOnTheCurrentBranch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, three) = await ThreeCommits(fixture);
        var snapshot = await fixture.ReadAsync();

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(snapshot, two), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        var head = await fixture.GitAsync("rev-parse", "HEAD");
        Assert.Equal(RevertText.Reverted(GitObjectId.Short(two), GitObjectId.Short(head)), result.Message);
        Assert.Equal(three, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("a", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "g.txt")));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "h.txt")));
        Assert.StartsWith("Revert \"two\"", await fixture.GitAsync("log", "-1", "--format=%B"));
        var after = await fixture.ReadAsync();
        Assert.Equal((RepositoryOperation.None, "main"), (after.Operation, after.Branch));
    }

    // Reverting the revert brings the change back, as a commit of its own.
    [Fact]
    public async Task WhenARevertIsReverted_ShouldBringTheChangeBack()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, _) = await ThreeCommits(fixture);
        var writer = new GitWriteRouter(fixture.Runner);
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), two), fixture.Token)).Outcome);
        var revert = await fixture.GitAsync("rev-parse", "HEAD");

        var result = await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), revert), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("b", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        Assert.True(File.Exists(Path.Combine(fixture.Root, "g.txt")));
    }

    // A commit made without a message is reverted all the same; Git's revert names it as it can.
    [Fact]
    public async Task WhenACommitWithAnEmptyMessageIsReverted_ShouldStillMakeTheRevertCommit()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await ThreeCommits(fixture);
        await fixture.WriteAsync("q.txt", "q\n");
        await fixture.GitAsync("add", "q.txt");
        await fixture.GitAsync("commit", "--allow-empty-message", "-m", "");
        var silent = await fixture.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), silent), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "q.txt")));
        Assert.Equal(silent, await fixture.GitAsync("rev-parse", "HEAD^"));
    }

    // The user finished the stopped revert in a terminal while Trag still showed it: Trag's abort is for a revert that is
    // over, and is refused without touching the commit made.
    [Fact]
    public async Task WhenTheAbortIsAskedAfterTheUserCommittedOutsideTrag_ShouldRefuseIt()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await Conflicted(fixture);
        var shown = await fixture.ReadAsync();
        await fixture.WriteAsync("f.txt", "resolved\n");
        await fixture.GitAsync("add", "f.txt");
        await fixture.GitAsync("-c", "core.editor=true", "revert", "--continue");
        var committed = await fixture.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PickAbortRequest(shown), fixture.Token);

        Assert.Equal(GitWriteOutcome.Rejected, result.Outcome);
        Assert.Equal(committed, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // A later commit changed the same lines, so Git stops and leaves the conflict for the user, in a revert in progress.
    [Fact]
    public async Task WhenTheRevertConflicts_ShouldStopWithTheConflictLeftForTheUser()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, _) = await ThreeCommits(fixture);
        await fixture.WriteAsync("f.txt", "c\n");
        var four = await fixture.CommitAsync("four", "f.txt");
        var snapshot = await fixture.ReadAsync();

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(snapshot, two), fixture.Token);

        Assert.Equal((GitWriteOutcome.NeedsResolution, RevertText.Stopped(GitObjectId.Short(two), 1)), (result.Outcome, result.Message));
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.Revert, after.Operation);
        Assert.Equal(["f.txt"], after.Conflicts.Select(file => file.Path.Display));
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    [Fact]
    public async Task WhenARevertCannotBeMadeHere_ShouldRefuseItAndChangeNothing()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, three) = await ThreeCommits(fixture);
        await fixture.GitAsync("switch", "-c", "side", two);
        await fixture.WriteAsync("side.txt", "side\n");
        await fixture.CommitAsync("side", "side.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.GitAsync("merge", "--no-ff", "--no-edit", "side");
        var merge = await fixture.GitAsync("rev-parse", "HEAD");
        var writer = new GitWriteRouter(fixture.Runner);

        var mergeCommit = await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), merge), fixture.Token);
        var gone = await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), new string('e', 40)), fixture.Token);
        await fixture.WriteAsync("h.txt", "edited\n");
        var dirty = await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RevertText.MergeCommit), (mergeCommit.Outcome, mergeCommit.Message));
        Assert.Equal((GitWriteOutcome.Rejected, HistoryText.CommitGone), (gone.Outcome, gone.Message));
        Assert.Equal((GitWriteOutcome.Rejected, RevertText.NeedsCleanTree), (dirty.Outcome, dirty.Message));
        Assert.Equal(merge, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(three, await fixture.GitAsync("rev-parse", "HEAD^1"));
    }

    // Reverting changes that are undone already would make an empty commit; Git makes none, and Trag says why.
    [Fact]
    public async Task WhenTheCommitsChangesAreUndoneAlready_ShouldSaySoAndChangeNothing()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, _) = await ThreeCommits(fixture);
        await fixture.GitAsync("revert", "--no-edit", two);
        var head = await fixture.GitAsync("rev-parse", "HEAD");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, RevertText.AlreadyUndone(GitObjectId.Short(two))), (result.Outcome, result.Message));
        Assert.Equal(head, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.None, (await fixture.ReadAsync()).Operation);
    }

    // The first commit has no parent, which is no shallow boundary: its revert undoes it against the empty tree, removing
    // the file it added, and what came after stays.
    [Fact]
    public async Task WhenARootCommitIsReverted_ShouldRevertAgainstTheEmptyTree()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("r.txt", "r\n");
        var root = await fixture.CommitAsync("root", "r.txt");
        await fixture.WriteAsync("s.txt", "s\n");
        var second = await fixture.CommitAsync("second", "s.txt");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), root), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(second, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("s.txt", await fixture.GitAsync("ls-files"));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "r.txt")));
    }

    // A prepare-commit-msg hook that fails stops Git after it applied the revert and before it committed, and Git leaves no
    // revert to finish, only the changes staged: they go back, as for a revert a key could not sign, and Trag says why.
    [Fact]
    public async Task WhenPrepareCommitMsgFails_ShouldPutTheChangesBackAndSaySo()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, three) = await ThreeCommits(fixture);
        await CherryPickTests.RefusingHookAsync(fixture);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.HookRefusedUndone), (result.Outcome, result.Message));
        Assert.Contains("refused by the hook", result.Details);
        Assert.Equal(three, await fixture.GitAsync("rev-parse", "HEAD"));
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.False(after.HasChanges);
    }

    // A revert stopped at a conflict is one Trag finishes: the conflict takes a side like a merge's, Finish waits for the
    // last one, and then commits the revert with Git's message.
    [Fact]
    public async Task WhenARevertsConflictIsResolved_ShouldFinishTheRevertWithGitsMessageOnlyOnceNoneIsLeft()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (two, four) = await Conflicted(fixture);
        var writer = new GitWriteRouter(fixture.Runner);
        var stopped = await fixture.ReadAsync();
        Assert.True(stopped.HasSupportedPick); Assert.True(stopped.HasSupportedResolution);
        Assert.Equal(two, stopped.PickHead);

        var early = await writer.ExecuteAsync(new PickContinueRequest(stopped), fixture.Token);
        var side = await writer.ExecuteAsync(new ConflictSideRequest(stopped, Snapshots.Path("f.txt"), ConflictChoice.Theirs), fixture.Token);
        var finished = await writer.ExecuteAsync(new PickContinueRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.ConflictsLeft(RepositoryOperation.Revert)), (early.Outcome, early.Message));
        Assert.True(side.Outcome == GitWriteOutcome.Succeeded, side.Message + " " + side.Details);
        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + " " + finished.Details);
        var head = await fixture.GitAsync("rev-parse", "HEAD");
        Assert.Equal(PickText.Finished(RepositoryOperation.Revert, GitObjectId.Short(two), GitObjectId.Short(head)), finished.Message);
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.StartsWith("Revert \"two\"", await fixture.GitAsync("log", "-1", "--format=%B"));
        Assert.Equal("a", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        Assert.Equal(RepositoryOperation.None, (await fixture.ReadAsync()).Operation);
    }

    // A revert Trag starts prepares Git's message, which quotes the reverted commit's subject, in UTF-8, whatever commit
    // encoding the user set for other tools, so the finished revert keeps every letter of it.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndTheRevertOfANonAsciiMessageConflicts_ShouldFinishItWithEveryLetter()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("f.txt", "a\n"); await fixture.CommitAsync("one", "f.txt");
        await fixture.WriteAsync("f.txt", "b\n"); var reverted = await fixture.CommitAsync("Čvor đak", "f.txt");
        await fixture.WriteAsync("f.txt", "c\n"); await fixture.CommitAsync("three", "f.txt");
        await fixture.GitAsync("config", "i18n.commitEncoding", "cp1250");
        var writer = new GitWriteRouter(fixture.Runner);
        await writer.ExecuteAsync(new RevertRequest(await fixture.ReadAsync(), reverted), fixture.Token);
        await writer.ExecuteAsync(new ConflictSideRequest(await fixture.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs), fixture.Token);

        var finished = await writer.ExecuteAsync(new PickContinueRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + " " + finished.Details);
        Assert.Equal("Revert \"Čvor đak\"", await fixture.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%s"));
    }

    // Like a merge's: the open conflict has its blocks, and the file staged with its markers outside Trag keeps Finish back
    // by name.
    [Fact]
    public async Task WhenTheRevertsConflictIsStagedWithItsMarkers_ShouldCountItsBlocksAndRefuseToFinish()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await Conflicted(fixture);
        Assert.NotNull(Assert.Single((await fixture.ReadAsync()).Resolutions.Open).Blocks);

        await fixture.GitAsync("add", "f.txt");
        var marked = await fixture.ReadAsync();
        var finish = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PickContinueRequest(marked), fixture.Token);

        Assert.Equal(["f.txt"], marked.Resolutions.Resolved.Where(path => path.HasMarkers).Select(path => path.Path.Display));
        Assert.Equal((GitWriteOutcome.Rejected, WriteText.WouldCommitMarkers("Finish", "f.txt")), (finish.Outcome, finish.Message));
        Assert.Equal(RepositoryOperation.Revert, (await fixture.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenAConflictedRevertIsAborted_ShouldLeaveTheBranchAndItsFilesAsTheyWereBefore()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, four) = await Conflicted(fixture);

        var aborted = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PickAbortRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, PickText.Aborted(RepositoryOperation.Revert, "main")), (aborted.Outcome, aborted.Message));
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("c", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.False(after.HasChanges);
    }

    // The commits of ThreeCommits and four, which changes f.txt again, then two reverted: Git stops at f.txt. Answers two
    // and four.
    private static async Task<(string Two, string Four)> Conflicted(GitFixture fixture)
    {
        var (two, _) = await ThreeCommits(fixture);
        await fixture.WriteAsync("f.txt", "c\n");
        var four = await fixture.CommitAsync("four", "f.txt");
        Assert.NotEqual(0, (await fixture.RunAsync("revert", "--no-edit", two)).ExitCode);
        return (two, four);
    }

    // one writes f.txt = a; two changes it to b and adds g.txt; three adds h.txt. Answers two and three.
    private static async Task<(string Two, string Three)> ThreeCommits(GitFixture fixture)
    {
        await fixture.WriteAsync("f.txt", "a\n");
        await fixture.CommitAsync("one", "f.txt");
        await fixture.WriteAsync("f.txt", "b\n");
        await fixture.WriteAsync("g.txt", "g\n");
        var two = await fixture.CommitAsync("two", "f.txt", "g.txt");
        await fixture.WriteAsync("h.txt", "h\n");
        return (two, await fixture.CommitAsync("three", "h.txt"));
    }
}
