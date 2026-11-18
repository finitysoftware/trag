using System.Text;
using Trag.Domain;
using Trag.Domain.Blame;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A repository made with git init --object-format=sha256 names every object by 64 hex characters instead of 40. The
// suite's repositories are SHA-1, so the main flows are run here once on SHA-256: read, staged, committed, rewritten,
// stashed, tagged and pushed, with the outcome checked in Git itself.
public sealed class Sha256RepositoryTests
{
    private static async Task<GitFixture> Sha256Async()
    {
        var f = await GitFixture.CreateAsync("main", objectFormat: "sha256");
        try { await f.GitAsync("config", "core.autocrlf", "false"); return f; }
        catch { f.Dispose(); throw; }
    }

    private static Task<GitWriteResult> WriteAsync(GitFixture f, GitWriteRequest request) => new GitWriteRouter(f.Runner).ExecuteAsync(request, f.Token);

    private static GitPath Named(string path) => new(Encoding.UTF8.GetBytes(path));

    [Fact]
    public async Task WhenASha256RepositoryIsOpened_ShouldReadItsBranchAndFullHeadWithoutALimitation()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); var head = await f.CommitAsync("First");

        var snapshot = await f.ReadAsync();

        Assert.Equal("sha256", await f.GitAsync("rev-parse", "--show-object-format"));
        Assert.Equal(64, head.Length);
        Assert.Equal(("main", head, RepositoryOperation.None), (snapshot.Branch, snapshot.Head, snapshot.Operation));
        Assert.Empty(snapshot.Limitations);
    }

    [Fact]
    public async Task WhenFilesAreChangedAddedAndDeleted_ShouldListEachWithItsStatus()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("changed.txt", "one\n"); await f.WriteAsync("deleted.txt", "gone\n"); await f.CommitAsync("Base");
        await f.WriteAsync("changed.txt", "two\n");
        File.Delete(Path.Combine(f.Root, "deleted.txt"));
        await f.WriteAsync("added.txt", "added\n"); await f.GitAsync("add", "added.txt");
        await f.WriteAsync("untracked.txt", "untracked\n");

        var files = (await f.ReadAsync()).Files;

        Assert.Equal([("added.txt", 'A', '.'), ("changed.txt", '.', 'M'), ("deleted.txt", '.', 'D'), ("untracked.txt", '?', '?')],
            files.Select(file => (file.Path.Display, file.Index, file.Worktree)).OrderBy(file => file.Display, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WhenAChangedFileIsRead_ShouldShowItsHunkAndBothBlobs()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("sample.txt", "a\nb\nc\n"); await f.CommitAsync("Before");
        await f.WriteAsync("sample.txt", "a\nB\nc\n"); await f.GitAsync("add", "sample.txt");
        var file = Assert.Single((await f.ReadAsync()).Files);

        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, file, true, false, f.Token);

        var metadata = Assert.Single(diff.Metadata);
        Assert.Equal((await f.GitAsync("rev-parse", "HEAD:sample.txt"), await f.GitAsync("rev-parse", ":sample.txt")), (metadata.OldObject, metadata.NewObject));
        Assert.Equal(64, metadata.NewObject.Length);
        Assert.Equal(DiffContentKind.Text, diff.Kind);
        Assert.Equal(["-b", "+B"], diff.Lines.Where(line => line.IsChange).Select(line => line.Text));
    }

    [Fact]
    public async Task WhenAFileIsStaged_ShouldPutItInTheIndex()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.WriteAsync("a.txt", "staged\n");

        var result = await WriteAsync(f, new StageRequest(await f.ReadAsync(), [Named("a.txt")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("staged", await f.GitAsync("show", ":a.txt"));
        var row = Assert.Single((await f.ReadAsync()).Files);
        Assert.Equal(('M', '.'), (row.Index, row.Worktree));
    }

    [Fact]
    public async Task WhenOneOfTwoHunksIsStaged_ShouldStageItAloneAndLeaveTheOtherInTheWorkingTree()
    {
        const string After = "a\nb\nc\nd\nE\nf\ng\nh\nI\n";
        using var f = await Sha256Async();
        await f.WriteAsync("sample.txt", "a\nb\nc\nd\ne\nf\ng\nh\ni\n"); await f.CommitAsync("Before");
        await f.WriteAsync("sample.txt", After);
        var snapshot = await f.ReadAsync();
        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, Assert.Single(snapshot.Files), false, false, f.Token);
        var changed = diff.Lines.ToList().FindIndex(line => line.Text == "-e");
        var hunk = HunkPatch.For(diff.Lines, DiffBlocks.At(diff.Lines, changed)!.Value.Lines, Named("sample.txt"), PatchAction.Stage);

        var result = await WriteAsync(f, new PatchRequest(snapshot, Named("sample.txt"), hunk, PatchAction.Stage, Lines: false));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni", await f.GitAsync("show", ":sample.txt"));
        Assert.Equal(After, await File.ReadAllTextAsync(Path.Combine(f.Root, "sample.txt"), f.Token));
    }

    [Fact]
    public async Task WhenStagedChangesAreCommitted_ShouldMakeTheCommitAndVerifyIt()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); var parent = await f.CommitAsync("Base");
        await f.WriteAsync("a.txt", "b\n"); await f.GitAsync("add", "a.txt");

        var result = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), "Second"));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(CommitOutcomeText.Created, result.Message);
        Assert.Equal(await f.GitAsync("rev-parse", "HEAD"), result.CommitId);
        Assert.Equal(64, result.CommitId!.Length);
        Assert.Equal(parent, await f.GitAsync("rev-parse", "HEAD^"));
        Assert.Empty((await f.ReadAsync()).Files);
    }

    [Fact]
    public async Task WhenTheLastCommitIsAmended_ShouldReplaceItOnTheSameParent()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); var parent = await f.CommitAsync("Base");
        await f.WriteAsync("b.txt", "b\n"); var replaced = await f.CommitAsync("Secnod");
        await f.WriteAsync("c.txt", "c\n"); await f.GitAsync("add", "c.txt");

        var result = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), "Second", Amend: true));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(CommitOutcomeText.Amended, result.Message);
        var head = await f.GitAsync("rev-parse", "HEAD");
        Assert.Equal(head, result.CommitId); Assert.NotEqual(replaced, head);
        Assert.Equal(parent, await f.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal(("Second", "b.txt\nc.txt"), (await f.GitAsync("log", "-1", "--format=%B"), await f.GitAsync("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")));
    }

    // base, then main and feature apart, joined by a merge: the merge's second parent takes a lane of its own that
    // closes into the base's row.
    [Fact]
    public async Task WhenTheHistoryIsRead_ShouldListFullIdsWithTenCharacterShortIdsAndJoinTheMergeInTheGraph()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("base.txt", "base\n"); var root = await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("feature.txt", "feature\n"); var feature = await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("main.txt", "main\n"); var main = await f.CommitAsync("Main");
        await f.GitAsync("merge", "--no-ff", "--no-edit", "feature");
        var head = await f.GitAsync("rev-parse", "HEAD");

        var page = await new CliHistoryReader(f.Runner).ReadPageAsync(f.Root, head, new(), f.Token);
        var graph = CommitGraphLayout.Build(page.Commits);

        Assert.Equal((await f.GitAsync("log", "--topo-order", "--format=%H")).Split('\n'), page.Commits.Select(commit => commit.Id));
        Assert.All(page.Commits, commit => Assert.Equal((64, commit.Id[..10]), (commit.Id.Length, commit.ShortId)));
        Assert.Equal([main, feature], page.Commits.Single(commit => commit.Id == head).Parents);
        Assert.Equal((2, 0, 1), (graph.LaneCount, graph.For(main)!.Lane, graph.For(feature)!.Lane));
        Assert.Contains(graph.For(head)!.Outgoing, edge => (edge.FromLane, edge.ToLane) == (0, 1));
        Assert.Contains(graph.For(root)!.Incoming, edge => (edge.FromLane, edge.ToLane) == (1, 0));
    }

    [Fact]
    public async Task WhenTheHistoryIsSearchedByAFullId_ShouldFindOnlyThatCommit()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("First");
        await f.WriteAsync("a.txt", "b\n"); var wanted = await f.CommitAsync("Second");
        await f.WriteAsync("a.txt", "c\n"); var head = await f.CommitAsync("Third");

        var page = await new CliHistoryReader(f.Runner).ReadPageAsync(f.Root, head, new(wanted, 0), f.Token);

        Assert.Equal(wanted, Assert.Single(page.Commits).Id);
    }

    [Fact]
    public async Task WhenAFileWithAnUncommittedLineIsBlamed_ShouldNameEachLinesCommitAndMarkTheNewLineUncommitted()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "one\ntwo\n"); var added = await f.CommitAsync("Add a");
        await f.WriteAsync("a.txt", "one\nTWO\n"); var changed = await f.CommitAsync("Change two");
        await f.WriteAsync("a.txt", "one\nTWO\nthree\n");

        var blame = await new CliHistoryReader(f.Runner).ReadBlameAsync(f.Root, null, "a.txt", BlameOptions.None, f.Token);

        Assert.Equal([(1, "one", added), (2, "TWO", changed), (3, "three", new string('0', 64))], blame.Lines.Select(line => (line.Number, line.Text, line.Commit.Id)));
        Assert.True(blame.Lines[2].Commit.IsUncommitted);
    }

    [Fact]
    public async Task WhenAMergeStopsOnConflicts_ShouldReadASupportedMergeAndFinishItOnceResolved()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("conflict.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("conflict.txt", "feature\n"); var feature = await f.CommitAsync("Feature");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("conflict.txt", "main\n"); var main = await f.CommitAsync("Main");

        var started = await WriteAsync(f, new MergeRequest(await f.ReadAsync(), "feature", feature, false, "Merge branch 'feature'"));
        var stopped = await f.ReadAsync();
        await f.WriteAsync("conflict.txt", "resolved\n");
        var staged = await WriteAsync(f, new StageRequest(await f.ReadAsync(), [Named("conflict.txt")], Resolution: true));
        var finished = await WriteAsync(f, new CommitRequest(await f.ReadAsync(), "Merge branch 'feature'", FinishesMerge: true));

        Assert.True(started.Outcome == GitWriteOutcome.NeedsResolution, started.Message + started.Details);
        Assert.True(stopped.HasSupportedMerge, stopped.Merge?.Limitation);
        Assert.Equal((main, feature), (stopped.Merge!.OriginalHead, stopped.Merge.IncomingHead));
        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);
        Assert.True(finished.Outcome == GitWriteOutcome.Succeeded, finished.Message + finished.Details);
        Assert.Equal([main, feature], (await f.GitAsync("show", "-s", "--format=%P", "HEAD")).Split(' '));
        Assert.Equal("resolved", await f.GitAsync("show", "HEAD:conflict.txt"));
    }

    [Fact]
    public async Task WhenTheBranchIsRebasedOntoAnother_ShouldReplayItsCommitsOnTop()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("base.txt", "base\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "feature");
        await f.WriteAsync("f.txt", "f\n"); await f.CommitAsync("Feature adds f");
        await f.WriteAsync("g.txt", "g\n"); await f.CommitAsync("Feature adds g");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("m.txt", "m\n"); var main = await f.CommitAsync("Main adds m");
        await f.GitAsync("switch", "feature");

        var result = await WriteAsync(f, new RebaseRequest(await f.ReadAsync(), "main", main));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(RebaseText.Rebased("feature", "main"), result.Message);
        Assert.Equal(main, await f.GitAsync("rev-parse", "HEAD~2"));
        Assert.Equal("Feature adds g\nFeature adds f\nMain adds m", await f.GitAsync("log", "--format=%s", "-3"));
        Assert.Equal(RepositoryOperation.None, (await f.ReadAsync()).Operation);
    }

    [Fact]
    public async Task WhenACommitIsRewordedInARewrite_ShouldReplayTheBranchWithTheNewMessage()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); var start = await f.CommitAsync("Base");
        await f.WriteAsync("b.txt", "b\n"); var b = await f.CommitAsync("Add b");
        await f.WriteAsync("c.txt", "c\n"); var c = await f.CommitAsync("Add c");

        var result = await WriteAsync(f, new RewriteRequest(await f.ReadAsync(), start,
            [new(b, "Add b", "Add b", RewriteAction.Reword, "Add b, reworded"), new(c, "Add c", "Add c", RewriteAction.Pick, "Add c")]));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(RewriteText.Rewritten("main"), result.Message);
        Assert.Equal("Add c\nAdd b, reworded", await f.GitAsync("log", "--format=%s", start + "..HEAD"));
        Assert.Equal(start, await f.GitAsync("rev-parse", "HEAD~2"));
    }

    [Fact]
    public async Task WhenACommitOfAnotherBranchIsPicked_ShouldApplyItOnTopOfTheCurrentBranch()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.GitAsync("switch", "-c", "side");
        await f.WriteAsync("s.txt", "s\n"); var picked = await f.CommitAsync("Side adds s");
        await f.GitAsync("switch", "main");
        await f.WriteAsync("m.txt", "m\n"); var main = await f.CommitAsync("Main adds m");

        var result = await WriteAsync(f, new CherryPickRequest(await f.ReadAsync(), picked));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var head = await f.GitAsync("rev-parse", "HEAD");
        Assert.Equal(CherryPickText.Picked(GitObjectId.Short(picked), GitObjectId.Short(head)), result.Message);
        Assert.Equal((main, "Side adds s"), (await f.GitAsync("rev-parse", "HEAD^"), await f.GitAsync("log", "-1", "--format=%s")));
        Assert.Equal("s", await f.GitAsync("show", "HEAD:s.txt"));
    }

    [Fact]
    public async Task WhenACommitIsReverted_ShouldAddACommitThatUndoesIt()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.WriteAsync("g.txt", "g\n"); var reverted = await f.CommitAsync("Add g");
        await f.WriteAsync("h.txt", "h\n"); var last = await f.CommitAsync("Add h");

        var result = await WriteAsync(f, new RevertRequest(await f.ReadAsync(), reverted));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var head = await f.GitAsync("rev-parse", "HEAD");
        Assert.Equal(RevertText.Reverted(GitObjectId.Short(reverted), GitObjectId.Short(head)), result.Message);
        Assert.Equal(last, await f.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal("a.txt\nh.txt", await f.GitAsync("ls-tree", "--name-only", "HEAD"));
    }

    [Fact]
    public async Task WhenChangesAreStashed_ShouldTakeThemOutOfTheWorkingTreeAndListTheStashByItsFullId()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.WriteAsync("a.txt", "stashed\n");

        var result = await WriteAsync(f, new StashRequest(await f.ReadAsync(), new("aside"), StashAction.Push));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal("a\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"), f.Token));
        var snapshot = await f.ReadAsync();
        var stash = Assert.Single(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token));
        Assert.Equal((await f.GitAsync("rev-parse", "refs/stash"), "aside"), (stash.Id, stash.Title));
        Assert.Equal((64, stash.Id), (stash.Id.Length, snapshot.StashHead));
        Assert.Empty(snapshot.Files);
    }

    [Fact]
    public async Task WhenTheStashIsAppliedAndDroppedById_ShouldBringTheChangesBackAndRemoveIt()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("Base");
        await f.WriteAsync("a.txt", "stashed\n"); await f.GitAsync("stash", "push", "-m", "aside");
        var id = await f.GitAsync("rev-parse", "refs/stash");

        var applied = await WriteAsync(f, new StashRequest(await f.ReadAsync(), new(Id: id), StashAction.Apply));
        var kept = await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token);
        var dropped = await WriteAsync(f, new StashRequest(await f.ReadAsync(), new(Id: id), StashAction.Drop));

        Assert.True(applied.Outcome == GitWriteOutcome.Succeeded, applied.Message + applied.Details);
        Assert.Equal(id, Assert.Single(kept).Id);
        Assert.True(dropped.Outcome == GitWriteOutcome.Succeeded, dropped.Message + dropped.Details);
        Assert.Equal("stashed\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "a.txt"), f.Token));
        Assert.Empty(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token));
        Assert.Null((await f.ReadAsync()).StashHead);
    }

    [Fact]
    public async Task WhenATagIsCreatedOnACommit_ShouldPointAtThatCommit()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); var first = await f.CommitAsync("First");
        await f.WriteAsync("a.txt", "b\n"); var head = await f.CommitAsync("Second");

        var result = await WriteAsync(f, new TagRequest(await f.ReadAsync(), new("v1.0", first)));

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(TagOutcomeText.Created("v1.0", GitObjectId.Short(first)), result.Message);
        Assert.Equal((first, head), (await f.GitAsync("rev-parse", "refs/tags/v1.0"), await f.GitAsync("rev-parse", "HEAD")));
    }

    // Git refuses to push a SHA-256 repository to a SHA-1 remote, so the bare remote is made SHA-256 too. Trag reads and
    // checks the remote's 64-character tip before and after the push.
    [Fact]
    public async Task WhenTheBranchIsPushedToASha256BareRemote_ShouldSendItAndShowItUpToDate()
    {
        using var f = await Sha256Async();
        await f.WriteAsync("a.txt", "a\n"); await f.CommitAsync("First");
        var bare = Path.Combine(f.Container, "origin.git");
        await f.GitAtAsync(f.Container, "init", "--bare", "--object-format=sha256", bare);
        await f.GitAsync("remote", "add", "origin", bare);
        await f.GitAsync("push", "-u", "origin", "main");
        await f.WriteAsync("a.txt", "b\n"); var head = await f.CommitAsync("Second");
        var sync = new GitSyncFacade(f.Runner, new CliRepositoryReader(f.Runner));
        var snapshot = await f.ReadAsync();
        var before = await sync.ReadAsync(snapshot, "origin", null, f.Token);

        var result = await sync.ExecuteAsync(new SyncRequest(snapshot, before, SyncAction.Push), f.Token);

        Assert.Equal(1, before.Ahead);
        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(SyncOutcomeText.Pushed, result.Message);
        Assert.Equal((head, head), (await f.GitAtAsync(bare, "rev-parse", "refs/heads/main"), await f.GitAsync("rev-parse", "refs/remotes/origin/main")));
        var after = await sync.ReadAsync(await f.ReadAsync(), "origin", null, f.Token);
        Assert.Equal((0, 0), (after.Ahead, after.Behind));
    }
}
