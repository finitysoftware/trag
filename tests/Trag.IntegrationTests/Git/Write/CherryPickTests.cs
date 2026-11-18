using Trag.Domain;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Write;

public sealed class CherryPickTests
{
    // A commit of another branch arrives on the current one as a new commit with its message; the branch it came from
    // stays as it is.
    [Fact]
    public async Task WhenACommitOfAnotherBranchIsPicked_ShouldApplyItOnTopOfTheCurrentBranch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, two, _, four) = await Branches(fixture);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        var head = await fixture.GitAsync("rev-parse", "HEAD");
        Assert.Equal(CherryPickText.Picked(GitObjectId.Short(two), GitObjectId.Short(head)), result.Message);
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("two", await fixture.GitAsync("log", "-1", "--format=%s"));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "s.txt")));
        Assert.Equal((RepositoryOperation.None, "main"), ((await fixture.ReadAsync()).Operation, (await fixture.ReadAsync()).Branch));
    }

    // main changed the same line meanwhile, so Git stops and leaves the conflict for the user, in a cherry-pick in progress.
    [Fact]
    public async Task WhenThePickConflicts_ShouldStopWithTheConflictLeftForTheUser()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, _, three, four) = await Branches(fixture);
        await fixture.WriteAsync("f.txt", "c\n");
        var five = await fixture.CommitAsync("five", "f.txt");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), three), fixture.Token);

        Assert.Equal((GitWriteOutcome.NeedsResolution, CherryPickText.Stopped(GitObjectId.Short(three), 1)), (result.Outcome, result.Message));
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.CherryPick, after.Operation);
        Assert.Equal(["f.txt"], after.Conflicts.Select(file => file.Path.Display));
        Assert.Equal(five, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.NotEqual(four, five);
    }

    [Fact]
    public async Task WhenAPickCannotBeMadeHere_ShouldRefuseItAndChangeNothing()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (one, two, _, four) = await Branches(fixture);
        var writer = new GitWriteRouter(fixture.Runner);
        await fixture.GitAsync("switch", "-c", "merged", one);
        await fixture.GitAsync("merge", "--no-ff", "--no-edit", "side");
        var merge = await fixture.GitAsync("rev-parse", "HEAD");
        await fixture.GitAsync("switch", "main");

        var already = await writer.ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), one), fixture.Token);
        var mergeCommit = await writer.ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), merge), fixture.Token);
        var gone = await writer.ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), new string('e', 40)), fixture.Token);
        await fixture.WriteAsync("m.txt", "edited\n");
        var dirty = await writer.ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CherryPickText.AlreadyOnBranch(GitObjectId.Short(one), "main")), (already.Outcome, already.Message));
        Assert.Equal((GitWriteOutcome.Rejected, CherryPickText.MergeCommit), (mergeCommit.Outcome, mergeCommit.Message));
        Assert.Equal((GitWriteOutcome.Rejected, HistoryText.CommitGone), (gone.Outcome, gone.Message));
        Assert.Equal((GitWriteOutcome.Rejected, CherryPickText.NeedsCleanTree), (dirty.Outcome, dirty.Message));
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD"));
    }

    // Looking around on a detached HEAD, a commit picked lands on that HEAD and the branch stays where it was.
    [Fact]
    public async Task WhenPickingOnADetachedHead_ShouldApplyItThereAndLeaveTheBranch()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, two, _, four) = await Branches(fixture);
        await fixture.GitAsync("switch", "--detach", "main");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "main"));
        Assert.True(File.Exists(Path.Combine(fixture.Root, "s.txt")));
    }

    // The first commit of another history has no parent: its whole tree is its change.
    [Fact]
    public async Task WhenARootCommitIsPicked_ShouldApplyItsTree()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, _, _, four) = await Branches(fixture);
        await fixture.GitAsync("switch", "--orphan", "other");
        await fixture.WriteAsync("root.txt", "root\n");
        var root = await fixture.CommitAsync("other root", "root.txt");
        await fixture.GitAsync("switch", "main");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), root), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("root", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "root.txt"), fixture.Token)).Trim());
    }

    // The same change came by another commit, so there is nothing left to apply; Git stops at an empty pick, which Trag
    // gives up at once instead of leaving it in progress.
    [Fact]
    public async Task WhenTheChangesAreHereAlready_ShouldSaySoAndLeaveNoPickInProgress()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, two, _, _) = await Branches(fixture);
        await fixture.WriteAsync("s.txt", "s\n");
        var same = await fixture.CommitAsync("same change", "s.txt");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, CherryPickText.NothingToApply(GitObjectId.Short(two))), (result.Outcome, result.Message));
        Assert.Equal(same, await fixture.GitAsync("rev-parse", "HEAD"));
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.False(after.HasChanges);
    }

    // A commit made with --allow-empty changes nothing from the start: the cherry-pick the user asked for is a new empty
    // commit with its message, not a change that is already here.
    [Fact]
    public async Task WhenTheCommitWasEmptyToBeginWith_ShouldNotSayNothingToApply()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, _, _, four) = await Branches(fixture);
        await fixture.GitAsync("switch", "side");
        await fixture.GitAsync("commit", "--allow-empty", "-m", "marker");
        var marker = await fixture.GitAsync("rev-parse", "HEAD");
        await fixture.GitAsync("switch", "main");

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), marker), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal((four, "marker"), (await fixture.GitAsync("rev-parse", "HEAD^"), await fixture.GitAsync("log", "-1", "--format=%s")));
        Assert.Equal(RepositoryOperation.None, (await fixture.ReadAsync()).Operation);
    }

    // A prepare-commit-msg hook that fails stops Git after it applied the commit and before it committed: the pick is
    // left ready to finish, as one a key could not sign, and Trag says the hook refused it instead of an unknown outcome.
    [Fact]
    public async Task WhenPrepareCommitMsgFails_ShouldNotEndUncertain()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, two, _, four) = await Branches(fixture);
        await RefusingHookAsync(fixture);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), two), fixture.Token);

        Assert.Equal((GitWriteOutcome.NeedsResolution, PickText.HookRefused(RepositoryOperation.CherryPick)), (result.Outcome, result.Message));
        Assert.Contains("refused by the hook", result.Details);
        Assert.Equal(four, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal(RepositoryOperation.CherryPick, (await fixture.ReadAsync()).Operation);
        Assert.Equal("s.txt", await fixture.GitAsync("diff", "--cached", "--name-only"));
    }

    // A prepare-commit-msg hook that refuses every commit, in a hooks folder beside the repository.
    internal static async Task RefusingHookAsync(GitFixture fixture)
    {
        var hooks = Path.Combine(fixture.Container, "hooks");
        Directory.CreateDirectory(hooks);
        await ShellScript.WriteAsync(Path.Combine(hooks, "prepare-commit-msg"), "#!/bin/sh\necho refused by the hook >&2\nexit 1\n", fixture.Token);
        await fixture.GitAsync("config", "core.hooksPath", hooks.Replace('\\', '/'));
    }

    // A cherry-pick stopped at a conflict is one Trag finishes: the conflict takes a side like a merge's, and Finish
    // commits the pick with the commit's own message.
    [Fact]
    public async Task WhenAPicksConflictIsResolved_ShouldFinishThePickWithTheCommitsMessage()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (three, five) = await Conflicted(fixture);
        var writer = new GitWriteRouter(fixture.Runner);
        var stopped = await fixture.ReadAsync();
        Assert.True(stopped.HasSupportedPick); Assert.Equal(three, stopped.PickHead);

        var early = await writer.ExecuteAsync(new PickContinueRequest(stopped), fixture.Token);
        var side = await writer.ExecuteAsync(new ConflictSideRequest(stopped, Snapshots.Path("f.txt"), ConflictChoice.Theirs), fixture.Token);
        var finished = await writer.ExecuteAsync(new PickContinueRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.Equal((GitWriteOutcome.Rejected, PickText.ConflictsLeft(RepositoryOperation.CherryPick)), (early.Outcome, early.Message));
        Assert.True(side.Outcome == GitWriteOutcome.Succeeded, side.Message + " " + side.Details);
        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + " " + finished.Details);
        var head = await fixture.GitAsync("rev-parse", "HEAD");
        Assert.Equal(PickText.Finished(RepositoryOperation.CherryPick, GitObjectId.Short(three), GitObjectId.Short(head)), finished.Message);
        Assert.Equal(five, await fixture.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("three", await fixture.GitAsync("log", "-1", "--format=%s"));
        Assert.Equal("b", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        Assert.Equal(RepositoryOperation.None, (await fixture.ReadAsync()).Operation);
    }

    // A cherry-pick Trag starts prepares the commit's message for Finish in UTF-8, whatever commit encoding the user set for
    // other tools, so the finished pick keeps every letter of it.
    [Fact]
    public async Task WhenCommitEncodingIsCp1250AndThePickOfANonAsciiMessageConflicts_ShouldFinishItWithEveryLetter()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("f.txt", "a\n"); await fixture.CommitAsync("one", "f.txt");
        await fixture.GitAsync("switch", "-c", "side");
        await fixture.WriteAsync("f.txt", "b\n"); var picked = await fixture.CommitAsync("Čvor đak", "f.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.WriteAsync("f.txt", "c\n"); await fixture.CommitAsync("two", "f.txt");
        await fixture.GitAsync("config", "i18n.commitEncoding", "cp1250");
        var writer = new GitWriteRouter(fixture.Runner);
        await writer.ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), picked), fixture.Token);
        await writer.ExecuteAsync(new ConflictSideRequest(await fixture.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs), fixture.Token);

        var finished = await writer.ExecuteAsync(new PickContinueRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + " " + finished.Details);
        Assert.Equal("Čvor đak", await fixture.GitAsync("log", "-1", "--encoding=UTF-8", "--format=%B"));
    }

    // A line of the commit's message that starts with '#', an issue number, is the user's text: Finish commits it, and only
    // Git's list of the conflicts is left out.
    [Fact]
    public async Task WhenThePickedCommitsMessageHasLinesStartingWithAHash_ShouldFinishWithThemAndWithoutGitsConflictList()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await fixture.WriteAsync("f.txt", "a\n"); await fixture.CommitAsync("one", "f.txt");
        await fixture.GitAsync("switch", "-c", "side");
        await fixture.WriteAsync("f.txt", "b\n"); var picked = await fixture.CommitAsync("#12 side change\n\n#13 body", "f.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.WriteAsync("f.txt", "c\n"); await fixture.CommitAsync("main", "f.txt");
        Assert.NotEqual(0, (await fixture.RunAsync("cherry-pick", picked)).ExitCode);
        var writer = new GitWriteRouter(fixture.Runner);
        Assert.Equal(GitWriteOutcome.Succeeded, (await writer.ExecuteAsync(new ConflictSideRequest(await fixture.ReadAsync(), Snapshots.Path("f.txt"), ConflictChoice.Theirs), fixture.Token)).Outcome);

        var finished = await writer.ExecuteAsync(new PickContinueRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + " " + finished.Details);
        Assert.Equal("#12 side change\n\n#13 body", await fixture.GitAsync("log", "-1", "--format=%B"));
    }

    // Like a merge's: the open conflict has its blocks, and the file staged with its markers outside Trag keeps Finish back
    // by name.
    [Fact]
    public async Task WhenThePicksConflictIsStagedWithItsMarkers_ShouldCountItsBlocksAndRefuseToFinish()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        await Conflicted(fixture);
        Assert.NotNull(Assert.Single((await fixture.ReadAsync()).Resolutions.Open).Blocks);

        await fixture.GitAsync("add", "f.txt");
        var marked = await fixture.ReadAsync();
        var finish = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PickContinueRequest(marked), fixture.Token);

        Assert.Equal(["f.txt"], marked.Resolutions.Resolved.Where(path => path.HasMarkers).Select(path => path.Path.Display));
        Assert.Equal((GitWriteOutcome.Rejected, WriteText.WouldCommitMarkers("Finish", "f.txt")), (finish.Outcome, finish.Message));
        Assert.Equal(RepositoryOperation.CherryPick, (await fixture.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenAConflictedPickIsAborted_ShouldLeaveTheBranchAndItsFilesAsTheyWereBefore()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, five) = await Conflicted(fixture);

        var aborted = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new PickAbortRequest(await fixture.ReadAsync()), fixture.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, PickText.Aborted(RepositoryOperation.CherryPick, "main")), (aborted.Outcome, aborted.Message));
        Assert.Equal(five, await fixture.GitAsync("rev-parse", "HEAD"));
        Assert.Equal("c", (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).Trim());
        var after = await fixture.ReadAsync();
        Assert.Equal(RepositoryOperation.None, after.Operation); Assert.False(after.HasChanges);
    }

    // git replace has Git use another commit wherever this one is read, the user's own choice: the details show what the
    // replacement says and changes, and the pick applies that.
    [Fact]
    public async Task WhenAReplaceRefExists_ShouldApplyWhatTheDetailsShowed()
    {
        using var fixture = await GitFixture.CreateAsync("main");
        var (_, two, three, _) = await Branches(fixture);
        await fixture.GitAsync("switch", "-c", "replacement", two);
        await fixture.WriteAsync("f.txt", "r\n");
        var replacement = await fixture.CommitAsync("three, replaced", "f.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.GitAsync("replace", three, replacement);
        var history = new CliHistoryReader(fixture.Runner);
        var details = await history.ReadCommitAsync(fixture.Root, three, fixture.Token);
        var diff = await history.ReadDiffAsync(fixture.Root, details, Assert.Single(details.Files), false, fixture.Token);

        var result = await new GitWriteRouter(fixture.Runner).ExecuteAsync(new CherryPickRequest(await fixture.ReadAsync(), three), fixture.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal((details.Commit.Subject, diff.Lines.Last(line => line.IsChange).Text),
            (await fixture.GitAsync("log", "-1", "--format=%s"), "+" + (await File.ReadAllTextAsync(Path.Combine(fixture.Root, "f.txt"), fixture.Token)).TrimEnd()));
    }

    // The branches of Branches and five on main, which changes f.txt again, then three picked: Git stops at f.txt.
    // Answers three and five.
    private static async Task<(string Three, string Five)> Conflicted(GitFixture fixture)
    {
        var (_, _, three, _) = await Branches(fixture);
        await fixture.WriteAsync("f.txt", "c\n");
        var five = await fixture.CommitAsync("five", "f.txt");
        Assert.NotEqual(0, (await fixture.RunAsync("cherry-pick", three)).ExitCode);
        return (three, five);
    }

    // main: one (f.txt = a), then four (m.txt). side, from one: two (s.txt), three (f.txt = b). On main afterwards.
    private static async Task<(string One, string Two, string Three, string Four)> Branches(GitFixture fixture)
    {
        await fixture.WriteAsync("f.txt", "a\n");
        var one = await fixture.CommitAsync("one", "f.txt");
        await fixture.GitAsync("switch", "-c", "side");
        await fixture.WriteAsync("s.txt", "s\n");
        var two = await fixture.CommitAsync("two", "s.txt");
        await fixture.WriteAsync("f.txt", "b\n");
        var three = await fixture.CommitAsync("three", "f.txt");
        await fixture.GitAsync("switch", "main");
        await fixture.WriteAsync("m.txt", "m\n");
        return (one, two, three, await fixture.CommitAsync("four", "m.txt"));
    }
}
