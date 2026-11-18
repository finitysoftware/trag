using System.Text;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Write;
using Trag.IntegrationTests.Git.Sync;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A repository whose refs Git keeps in reftable tables rather than as files. Each step of the everyday flow, from
// opening it to pushing it, reads and writes its branches, tags, stash and reflog through Git, and leaves every
// repository it touched one git fsck accepts. The repositories are migrated with git refs migrate (Git 2.46 or newer;
// the sync tests need 2.48, since their clones already have reflogs when they are migrated).
public sealed class ReftableRepositoryTests
{
    private const string Before = "a\nb\nc\nd\ne\nf\ng\nh\ni\n";
    private const string After = "a\nb\nc\nd\nE\nf\ng\nh\nI\n";

    [GitFact(2, 46)]
    public async Task WhenAReftableRepositoryIsOpened_ShouldReadItsBranchHeadAndFilesWithoutAnOperationOrLimitation()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "staged\n"); await f.GitAsync("add", "one.txt");
        await f.WriteAsync("two.txt", "unstaged\n"); await f.WriteAsync("new.txt", "untracked\n");

        var snapshot = await f.ReadAsync();

        Assert.Equal(("main", await f.GitAsync("rev-parse", "HEAD")), (snapshot.Branch, snapshot.Head));
        Assert.Equal(RepositoryOperation.None, snapshot.Operation);
        Assert.Empty(snapshot.Limitations);
        Assert.Equal([("new.txt", '?', '?'), ("one.txt", 'M', '.'), ("two.txt", '.', 'M')],
            snapshot.Files.Select(file => (file.Path.Display, file.Index, file.Worktree)).Order());
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    // A watcher update of one path first asks Git for the root refs, which this repository keeps in its tables.
    [GitFact(2, 46)]
    public async Task WhenAFileChangesAfterTheRead_ShouldReadItsNewStatusForThatPathAlone()
    {
        using var f = await ReftableAsync();
        var previous = await f.ReadAsync();
        await f.WriteAsync("one.txt", "changed\n"); await f.WriteAsync("two.txt", "changed\n");

        var incremental = await new CliRepositoryReader(f.Runner).ReadPathsAsync(previous, ["one.txt"], f.Token);

        var row = Assert.Single(incremental.Files);
        Assert.Equal(("one.txt", '.', 'M'), (row.Path.Display, row.Index, row.Worktree));
        Assert.Equal(2, (await f.ReadAsync()).Files.Count);
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenFilesAreStagedAndUnstaged_ShouldMoveThemThroughThePrivateIndex()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "changed\n");
        var writer = new GitWriteRouter(f.Runner);
        GitPath[] one = [new("one.txt"u8.ToArray())];

        var staged = await writer.ExecuteAsync(new StageRequest(await f.ReadAsync(), one), f.Token);
        var index = await f.GitAsync("diff", "--cached", "--name-only");
        var unstaged = await writer.ExecuteAsync(new UnstageRequest(await f.ReadAsync(), one), f.Token);

        Assert.True(staged.Outcome == GitWriteOutcome.Succeeded, staged.Message + staged.Details);
        Assert.True(unstaged.Outcome == GitWriteOutcome.Succeeded, unstaged.Message + unstaged.Details);
        Assert.Equal(("one.txt", ""), (index, await f.GitAsync("diff", "--cached", "--name-only")));
        Assert.Equal("changed\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.Root, ".git"), "trag-write-*"));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenOneOfTwoHunksIsStaged_ShouldStageItAlone()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("sample.txt", Before); await f.CommitAsync("Sample", "sample.txt");
        await f.WriteAsync("sample.txt", After);
        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(f.Root, Assert.Single((await f.ReadAsync()).Files), false, false, f.Token);
        var hunk = DiffBlocks.At(diff.Lines, diff.Lines.ToList().FindIndex(line => line.Text == "-e"))!.Value.Lines;
        var patch = HunkPatch.For(diff.Lines, hunk, Sample(), PatchAction.Stage);

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new PatchRequest(await f.ReadAsync(), Sample(), patch, PatchAction.Stage, Lines: false), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + " " + result.Details);
        Assert.Equal("a\nb\nc\nd\nE\nf\ng\nh\ni\n", await f.GitAsync("show", ":sample.txt") + "\n");
        Assert.Equal(After, await File.ReadAllTextAsync(Path.Combine(f.Root, "sample.txt"), f.Token));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    // Trag finds its own commit again by the reflog action it set, and Git keeps that reflog in its tables.
    [GitFact(2, 46)]
    public async Task WhenACommitIsMade_ShouldVerifyItAndLeaveTragsEntryInTheReflogGitReads()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "changed\n"); await f.GitAsync("add", "one.txt");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "Change one"), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, CommitOutcomeText.Created), (result.Outcome, result.Message));
        Assert.Equal(await f.GitAsync("rev-parse", "HEAD"), result.CommitId);
        Assert.StartsWith("trag-commit-", await f.GitAsync("reflog", "-1", "--format=%gs", "HEAD"));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    // Git is stopped in its post-commit hook, after it made the commit; with Git's exit lost, only the reflog entry says
    // the commit is the one this write made.
    [GitFact(2, 46)]
    public async Task WhenACommitIsCanceledAfterGitMadeIt_ShouldFindItByTheReflogGitReads()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "changed\n"); await f.GitAsync("add", "one.txt");
        await HookAsync(f, "post-commit", "sleep 30 & echo ready > .git/trag-hook-ready\nwait\n");
        using var cancellation = new CancellationTokenSource();
        var pending = new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "Change one"), cancellation.Token);
        while (!File.Exists(Path.Combine(f.Root, ".git", "trag-hook-ready")))
        {
            if (pending.IsCompleted) throw new InvalidOperationException((await pending).Message);
            await Task.Delay(30, f.Token);
        }

        cancellation.Cancel();
        var result = await pending;

        Assert.Equal((GitWriteOutcome.Succeeded, CommitOutcomeText.CreatedAfterCancel), (result.Outcome, result.Message));
        Assert.Equal(await f.GitAsync("rev-parse", "HEAD"), result.CommitId);
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "index.lock")));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenTheLastCommitIsAmended_ShouldReplaceItOnTheSameParent()
    {
        using var f = await ReftableAsync();
        var parent = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync("one.txt", "first\n"); var replaced = await f.CommitAsync("Frist");
        await f.WriteAsync("two.txt", "second\n"); await f.GitAsync("add", "two.txt");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new CommitRequest(await f.ReadAsync(), "First", Amend: true), f.Token);

        Assert.Equal((GitWriteOutcome.Succeeded, CommitOutcomeText.Amended), (result.Outcome, result.Message));
        var head = await f.GitAsync("rev-parse", "HEAD");
        Assert.Equal(head, result.CommitId); Assert.NotEqual(replaced, head);
        Assert.Equal(parent, await f.GitAsync("rev-parse", "HEAD^"));
        Assert.Equal(("First", "second"), (await f.GitAsync("log", "-1", "--format=%B"), await f.GitAsync("show", "HEAD:two.txt")));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenABranchIsCreatedAndSwitchedBack_ShouldMoveHeadBetweenTheBranches()
    {
        using var f = await ReftableAsync();
        var main = await f.GitAsync("rev-parse", "HEAD");
        var writer = new GitWriteRouter(f.Runner);

        var created = await writer.ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Create), f.Token);
        var onTopic = (await f.ReadAsync()).Branch;
        await f.WriteAsync("one.txt", "topic\n"); var topic = await f.CommitAsync("On topic");
        var back = await writer.ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("main"), BranchAction.Switch), f.Token);

        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + created.Details);
        Assert.True(back.Outcome == GitWriteOutcome.Succeeded, back.Message + back.Details);
        var snapshot = await f.ReadAsync();
        Assert.Equal(("topic", "main", main), (onTopic, snapshot.Branch, snapshot.Head));
        Assert.Equal(topic, await f.GitAsync("rev-parse", "refs/heads/topic"));
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenAMergedBranchIsDeleted_ShouldRemoveItsRef()
    {
        using var f = await ReftableAsync();
        await f.GitAsync("branch", "topic");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("topic"), BranchAction.Delete), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(1, (await f.RunAsync("rev-parse", "--verify", "--quiet", "refs/heads/topic")).ExitCode);
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenChangesAreStashed_ShouldListTheEntryAndRestoreTheCommittedFiles()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "work\n");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new StashRequest(await f.ReadAsync(), new("work"), StashAction.Push), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var snapshot = await f.ReadAsync();
        var entry = Assert.Single(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token));
        Assert.NotNull(snapshot.StashHead);
        Assert.Equal((snapshot.StashHead, "work"), (entry.Id, entry.Title));
        Assert.Empty(snapshot.Files);
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    // Dropping the last entry deletes refs/stash, and with it the stash's reflog, from Git's tables.
    [GitFact(2, 46)]
    public async Task WhenTheOnlyStashIsAppliedAndDropped_ShouldBringTheChangesBackAndLeaveNoStash()
    {
        using var f = await ReftableAsync();
        await f.WriteAsync("one.txt", "work\n"); await f.GitAsync("stash", "push", "-m", "work");
        var id = await f.GitAsync("rev-parse", "refs/stash");
        var writer = new GitWriteRouter(f.Runner);

        var applied = await writer.ExecuteAsync(new StashRequest(await f.ReadAsync(), new(Id: id), StashAction.Apply), f.Token);
        var dropped = await writer.ExecuteAsync(new StashRequest(await f.ReadAsync(), new(Id: id), StashAction.Drop), f.Token);

        Assert.True(applied.Outcome == GitWriteOutcome.Succeeded, applied.Message + applied.Details);
        Assert.True(dropped.Outcome == GitWriteOutcome.Succeeded, dropped.Message + dropped.Details);
        Assert.Equal("work\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token));
        Assert.Empty(await new CliStashReader(f.Runner).ReadStashesAsync(f.Root, f.Token));
        Assert.Equal(1, (await f.RunAsync("rev-parse", "--verify", "--quiet", "refs/stash")).ExitCode);
        Assert.Null((await f.ReadAsync()).StashHead);
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 46)]
    public async Task WhenAnAnnotatedTagIsCreatedAndDeleted_ShouldWriteAndRemoveItsRef()
    {
        using var f = await ReftableAsync();
        var head = await f.GitAsync("rev-parse", "HEAD");
        var writer = new GitWriteRouter(f.Runner);
        var untagged = await f.ReadAsync();

        var created = await writer.ExecuteAsync(new TagRequest(untagged, new("v1.0", head, "First release")), f.Token);
        var tagged = await f.ReadAsync();
        var kind = await f.GitAsync("cat-file", "-t", "refs/tags/v1.0");
        var deleted = await writer.ExecuteAsync(new TagDeleteRequest(tagged, "v1.0"), f.Token);

        Assert.True(created.Outcome == GitWriteOutcome.Succeeded, created.Message + created.Details);
        Assert.True(deleted.Outcome == GitWriteOutcome.Succeeded, deleted.Message + deleted.Details);
        Assert.Equal("tag", kind);
        Assert.False(tagged.HasSameTags(untagged));
        Assert.Equal(1, (await f.RunAsync("rev-parse", "--verify", "--quiet", "refs/tags/v1.0")).ExitCode);
        Assert.True((await f.ReadAsync()).HasSameTags(untagged));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    // The reset leaves ORIG_HEAD in Git's tables, not as a file; read with the other root refs, it is no operation.
    [GitFact(2, 46)]
    public async Task WhenTheBranchIsResetHardToItsParent_ShouldMoveItThereAndReadNoOperation()
    {
        using var f = await ReftableAsync();
        var parent = await f.GitAsync("rev-parse", "HEAD");
        await f.WriteAsync("one.txt", "second\n"); var second = await f.CommitAsync("Second");

        var result = await new GitWriteRouter(f.Runner).ExecuteAsync(new ResetRequest(await f.ReadAsync(), parent, ResetMode.Hard), f.Token);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        var snapshot = await f.ReadAsync();
        Assert.Equal(("main", parent, RepositoryOperation.None), (snapshot.Branch, snapshot.Head, snapshot.Operation));
        Assert.Equal(second, await f.GitAsync("rev-parse", "--verify", "ORIG_HEAD"));
        Assert.False(File.Exists(Path.Combine(f.Root, ".git", "ORIG_HEAD")));
        Assert.Equal("one\n", await File.ReadAllTextAsync(Path.Combine(f.Root, "one.txt"), f.Token));
        await FsckAsync(f.GitAtAsync, f.Root);
    }

    [GitFact(2, 48)]
    public async Task WhenTheOtherClonePushedToTheReftableBare_ShouldFetchItIntoTheTrackingRef()
    {
        using var f = await SyncReftableAsync();
        var before = await f.Head(f.One);
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");

        var result = await f.Execute(SyncAction.Fetch);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.Two), await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"));
        Assert.Equal((1, before), ((await f.State()).Behind, await f.Head(f.One)));
        await FsckAsync(f.Git, f.One, f.Two, f.Bare);
    }

    [GitFact(2, 48)]
    public async Task WhenTheBranchIsBehindTheReftableBare_ShouldPullItFastForward()
    {
        using var f = await SyncReftableAsync();
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");

        var result = await f.Execute(SyncAction.Pull);

        Assert.True(result.Outcome == GitWriteOutcome.Succeeded, result.Message + result.Details);
        Assert.Equal(await f.Head(f.Two), await f.Head(f.One));
        Assert.Empty((await f.Reader.ReadAsync(f.One, f.Token)).Files);
        await FsckAsync(f.Git, f.One, f.Two, f.Bare);
    }

    [GitFact(2, 48)]
    public async Task WhenTheBranchIsAheadOfTheReftableBare_ShouldPushItAndRecordTheTrackingRef()
    {
        using var f = await SyncReftableAsync();
        await f.Commit(f.One, "from Trag");

        var result = await f.Execute(SyncAction.Push);

        Assert.Equal((GitWriteOutcome.Succeeded, SyncOutcomeText.Pushed), (result.Outcome, result.Message));
        var head = await f.Head(f.One);
        Assert.Equal(head, await f.Git(f.Bare, "rev-parse", "refs/heads/trunk"));
        Assert.Equal(head, await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"));
        Assert.Equal(0, (await f.State()).Ahead);
        await FsckAsync(f.Git, f.One, f.Two, f.Bare);
    }

    // one.txt and two.txt committed on main, in a repository migrated to reftable before its first commit.
    private static async Task<GitFixture> ReftableAsync()
    {
        var f = await GitFixture.CreateAsync("main");
        try
        {
            await MigrateAsync(f.GitAtAsync, f.Root);
            await f.GitAsync("config", "core.autocrlf", "false");
            await f.WriteAsync("one.txt", "one\n"); await f.WriteAsync("two.txt", "two\n"); await f.CommitAsync("Base");
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    // The bare origin and both clones of the sync fixture, each migrated after the fixture made it.
    private static async Task<SyncFixture> SyncReftableAsync()
    {
        var f = await SyncFixture.Create();
        try
        {
            foreach (var root in new[] { f.Bare, f.One, f.Two }) await MigrateAsync(f.Git, root);
            return f;
        }
        catch { f.Dispose(); throw; }
    }

    private static async Task MigrateAsync(Func<string, string[], Task<string>> git, string root)
    {
        await git(root, ["refs", "migrate", "--ref-format=reftable"]);
        Assert.Equal("reftable", await git(root, ["rev-parse", "--show-ref-format"]));
    }

    // A repository git fsck finds anything wrong with, in its objects or its refs, fails the test with Git's stderr.
    private static async Task FsckAsync(Func<string, string[], Task<string>> git, params string[] roots)
    {
        foreach (var root in roots) await git(root, ["fsck", "--no-progress"]);
    }

    private static async Task HookAsync(GitFixture f, string name, string body)
    {
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(f.Container, "empty-hooks")).FullName, name);
        await File.WriteAllTextAsync(path, "#!/bin/sh\n" + body, new UTF8Encoding(false), f.Token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static GitPath Sample() => new("sample.txt"u8.ToArray());
}
