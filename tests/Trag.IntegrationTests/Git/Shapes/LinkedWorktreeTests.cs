using System.Security.Cryptography;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A worktree added with git worktree add keeps its own index and the state of an operation in progress in
// .git/worktrees/<name>/, and shares the refs, the objects and the configuration in .git with the main worktree. Trag
// reads one, and writes to none of it yet.
public sealed class LinkedWorktreeTests
{
    // Every kind of write goes through the router composed with the synchronizer, as the shell composes them, in the state
    // each write needs: changes and no operation, then a merge, a rebase and a cherry-pick each stopped on a conflict in
    // the linked worktree. Each write is refused with its writer's sentence, neither worktree's status, the git
    // directory's files nor the bare remote's refs change around any batch of them, and the main worktree's index keeps
    // the bytes it had once the linked worktree was added.
    [Fact]
    public async Task WhenTheRepositoryIsALinkedWorktree_ShouldOpenWithTheLimitationAndRefuseEveryWrite()
    {
        using var f = await GitFixture.CreateAsync("main", scenarioTimeout: TimeSpan.FromMinutes(3));
        await f.GitAsync("config", "core.autocrlf", "false");
        await f.WriteAsync("a.txt", "a\n"); await f.WriteAsync("b.txt", "b\n"); var first = await f.CommitAsync("First");
        var bare = Path.Combine(f.Container, "origin.git");
        await f.GitAtAsync(f.Container, "init", "--bare", "--initial-branch=main", bare);
        await f.GitAsync("remote", "add", "origin", bare); await f.GitAsync("push", "-u", "origin", "main"); await f.GitAsync("tag", "v1");
        await f.GitAsync("switch", "-c", "topic");
        await f.WriteAsync("a.txt", "topic\n"); await f.WriteAsync("c.txt", "c\n"); var topic = await f.CommitAsync("Topic");
        await f.GitAsync("switch", "main");
        var linked = Path.Combine(f.Container, "linked");
        await f.GitAsync("worktree", "add", "--track", "-b", "linked", linked, "origin/main");
        var mainIndex = await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token);
        await Write("a.txt", "mine\n"); await Git("add", "a.txt"); await Git("commit", "-m", "Mine"); var mine = await Git("rev-parse", "HEAD");
        var reader = new CliRepositoryReader(f.Runner);
        var sync = new GitSyncFacade(f.Runner, reader);
        var writer = new GitWriteRouter(f.Runner, sync, reader);
        HashSet<Type> sent = [];
        GitPath a = Snapshots.Path("a.txt"), b = Snapshots.Path("b.txt");

        await Write("b.txt", "staged\n"); await Git("add", "b.txt");
        await Write("a.txt", "mine\nchanged\n"); await Write("build.log", "log\n");
        var changed = await f.ReadAsync(linked);
        Assert.Equal([LimitationText.LinkedWorktree], changed.Limitations);
        Assert.Equal((Path.GetFullPath(Path.Combine(f.Root, ".git", "worktrees", "linked")), Path.GetFullPath(Path.Combine(f.Root, ".git")), "linked"),
            (Path.GetFullPath(changed.GitDirectory!), Path.GetFullPath(changed.CommonDirectory!), changed.Branch));
        var diff = await new CliFileDiffReader(f.Runner).ReadAsync(linked, changed.Files.Single(file => file.Path.IsSame(a)), false, false, f.Token);
        var hunk = DiffBlocks.At(diff.Lines, diff.Lines.ToList().FindIndex(line => line.Text == "+changed"))!.Value.Lines;
        var state = await sync.ReadAsync(changed, "origin", null, f.Token);
        await RefuseAsync(
            new CommitRequest(changed, "Commit in the linked worktree"), new StageRequest(changed, [a]), new UnstageRequest(changed, [b]),
            new DiscardRequest(changed, [a]), new PatchRequest(changed, a, HunkPatch.For(diff.Lines, hunk, a, PatchAction.Stage), PatchAction.Stage, Lines: false),
            new BranchRequest(changed, new("feature"), BranchAction.Create), new BranchRenameRequest(changed, "topic", "renamed"),
            new TagRequest(changed, new("v2", mine, "Second release")), new TagDeleteRequest(changed, "v1"),
            new RevertRequest(changed, mine), new CherryPickRequest(changed, topic), new ResetRequest(changed, first, ResetMode.Mixed),
            new MergeRequest(changed, "topic", topic, false, "Merge topic"), new RebaseRequest(changed, "topic", topic),
            new RewriteRequest(changed, first, [new(mine, "Mine", "Mine", RewriteAction.Reword, "Mine, reworded")]),
            new StashRequest(changed, new("work"), StashAction.Push),
            new RemoteAddRequest(changed, "second", "https://example.invalid/second.git"), new RemoteEditRequest(changed, "origin", "upstream", null),
            new RemoteRemoveRequest(changed, "origin"), new IgnoreRequest(changed, Snapshots.Path("build.log"), "*.log"),
            new RestoreFileRequest(changed, first, a), new RevertFileRequest(changed, mine, a, null),
            new SyncRequest(changed, state, SyncAction.Fetch), new SyncRequest(changed, state, SyncAction.SelectAccount, "account"),
            new PublishRequest(changed, PublishOperation.PushTo("origin", "linked", null)), new RemoteBranchDeleteRequest(changed, "origin", "main"),
            new RemoteTagRequest(changed, "origin", "v1", RemoteTagAction.Push), new SubmoduleUpdateRequest(changed));

        await Git("reset", "--hard");
        Assert.NotEqual(0, (await f.RunAtAsync(linked, "merge", "topic")).ExitCode);
        var merging = await f.ReadAsync(linked);
        Assert.Equal((RepositoryOperation.Merge, topic), (merging.Operation, merging.Merge?.IncomingHead));
        Assert.True(File.Exists(Path.Combine(merging.GitDirectory!, "MERGE_HEAD")));
        Assert.False(File.Exists(Path.Combine(merging.CommonDirectory!, "MERGE_HEAD")));
        var conflict = await new CliFileDiffReader(f.Runner).ReadConflictAsync(linked, merging.Conflicts.Single(), f.Token);
        await RefuseAsync(
            new CommitRequest(merging, "Merge topic", FinishesMerge: true), new MergeAbortRequest(merging),
            new StageRequest(merging, [a], Resolution: true), new ConflictSideRequest(merging, a, ConflictChoice.Mine),
            new ConflictBlockRequest(merging, a, 0, ConflictChoice.Mine, conflict.Hash), new MergeToolRequest(merging, a, Editor: true),
            new ReopenConflictRequest(merging, Snapshots.Path("c.txt")));
        await Git("merge", "--abort");

        Assert.NotEqual(0, (await f.RunAtAsync(linked, "rebase", "topic")).ExitCode);
        var rebasing = await f.ReadAsync(linked);
        Assert.Equal(RepositoryOperation.Rebase, rebasing.Operation);
        Assert.True(Directory.Exists(Path.Combine(rebasing.GitDirectory!, "rebase-merge")));
        Assert.False(Directory.Exists(Path.Combine(rebasing.CommonDirectory!, "rebase-merge")));
        await RefuseAsync(new RebaseContinueRequest(rebasing, "Mine"), new RebaseSkipRequest(rebasing), new RebaseAbortRequest(rebasing));
        await Git("rebase", "--abort");

        Assert.NotEqual(0, (await f.RunAtAsync(linked, "cherry-pick", "topic")).ExitCode);
        var picking = await f.ReadAsync(linked);
        Assert.Equal((RepositoryOperation.CherryPick, topic), (picking.Operation, picking.PickHead));
        Assert.True(File.Exists(Path.Combine(picking.GitDirectory!, "CHERRY_PICK_HEAD")));
        Assert.False(File.Exists(Path.Combine(picking.CommonDirectory!, "CHERRY_PICK_HEAD")));
        await RefuseAsync(new PickContinueRequest(picking), new PickAbortRequest(picking));
        await Git("cherry-pick", "--abort");

        Assert.Equal(mainIndex, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".git", "index"), f.Token));
        // Every concrete write was sent, so a new kind fails here until it is. A download of Git LFS content is left out:
        // the synchronizer takes the shown snapshot for it, the application sends it only as the first step of a merge or
        // of a new branch from a remote branch, and its own gate (RepositoryWriter.Allows) refuses that step in a
        // repository with a limitation; it writes to the Git LFS store alone, never to an index.
        Assert.Equal(typeof(GitWriteRequest).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(GitWriteRequest)) && !type.IsAbstract)
            .Except([typeof(LfsDownloadRequest)]).Select(type => type.Name).Order(), sent.Select(type => type.Name).Order());

        Task Write(string path, string text) => File.WriteAllTextAsync(Path.Combine(linked, path), text, f.Token);
        Task<string> Git(params string[] arguments) => f.GitAtAsync(linked, arguments);

        // Each write is first checked to be in the state it needs once the limitation is gone, so its refusal is the
        // linked worktree's. The loop stops at the first write that is not refused, before a merge tool
        // or a rebase continued for real could wait on an editor until the fixture times out.
        async Task RefuseAsync(params GitWriteRequest[] writes)
        {
            var before = await WorldAsync(f, linked, bare);
            foreach (var request in writes)
            {
                var (name, (refusal, allowed)) = (request.GetType().Name, Expectation(request));
                Assert.True(allowed, name + " is refused in this state without the limitation too.");
                var result = await writer.ExecuteAsync(request, f.Token);
                Assert.Equal((name, GitWriteOutcome.Rejected, refusal), (name, result.Outcome, result.Message));
                sent.Add(request.GetType());
            }
            Assert.Equal(before, await WorldAsync(f, linked, bare));
        }
    }

    // The main worktree writes as any repository does, but a branch checked out in a linked worktree is the linked
    // worktree's: Git refuses to switch the main one to it, and Trag says what frees the branch. Neither worktree moves.
    [Fact]
    public async Task WhenTheMainWorktreeSwitchesToABranchCheckedOutInALinkedWorktree_ShouldRefuseWithTheWorktreeSentence()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.CommitAsync("Initial");
        await f.GitAsync("branch", "elsewhere");
        var linked = Path.Combine(f.Container, "linked tree");
        await f.GitAsync("worktree", "add", linked, "elsewhere");

        var refused = await new GitWriteRouter(f.Runner).ExecuteAsync(new BranchRequest(await f.ReadAsync(), new("elsewhere"), BranchAction.Switch), f.Token);

        Assert.Equal((GitWriteOutcome.Rejected, BranchOutcomeText.TargetInAnotherWorktree), (refused.Outcome, refused.Message));
        Assert.Equal(("main", "elsewhere"), (await f.GitAsync("symbolic-ref", "--short", "HEAD"), await f.GitAtAsync(linked, "symbolic-ref", "--short", "HEAD")));
    }

    // The sentence the writer that owns a request refuses it with in a linked worktree, and whether the state it was built
    // in passes the state rule asked first once the limitation is gone: the synchronizer's for a write to a remote, the
    // router's state check for every other one. Saving a sync account needs nothing by the router's rule, and the synchronizer refuses
    // it here all the same; the account would go into the configuration both worktrees share.
    private static (string Refusal, bool Allowed) Expectation(GitWriteRequest request)
    {
        var open = request.Expected with { Limitations = [] };
        var remote = SnapshotComparison.ReadyForSync(request.Expected, open);
        return request switch
        {
            SyncRequest => (SyncOutcomeText.StateUnsupported("synchronizing"), remote),
            PublishRequest => (SyncOutcomeText.StateUnsupported("publishing"), remote),
            RemoteBranchDeleteRequest => (SyncOutcomeText.StateUnsupported("deleting"), remote),
            RemoteTagRequest => (SyncOutcomeText.StateUnsupported("pushing"), remote),
            SubmoduleUpdateRequest => (SubmoduleText.Changed, remote),
            _ => (WriteText.StateUnsupported, WriteNeeds.StateAllows(request, open))
        };
    }

    // What a refused write leaves as it was: both indexes, the shared configuration, every ref of the repository and of
    // the bare remote, the worktrees and their HEADs, both working trees' status, and every file in the git directory with
    // its size, an index.lock, a private trag-write index or a rebase's state among them.
    private sealed record World(string MainIndex, string LinkedIndex, string Config, string Refs, string RemoteRefs, string Worktrees,
        string MainStatus, string LinkedStatus, string Files);

    private static async Task<World> WorldAsync(GitFixture f, string linked, string bare)
    {
        var git = Path.Combine(f.Root, ".git");
        string[] status = ["status", "--porcelain=v2"];
        return new(await HashAsync(Path.Combine(git, "index")), await HashAsync(Path.Combine(git, "worktrees", "linked", "index")),
            await File.ReadAllTextAsync(Path.Combine(git, "config"), f.Token),
            await f.GitAsync("for-each-ref"), await f.GitAtAsync(bare, "for-each-ref"), await f.GitAsync("worktree", "list", "--porcelain"),
            await f.GitAsync(status), await f.GitAtAsync(linked, status),
            string.Join('\n', new DirectoryInfo(git).EnumerateFiles("*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(git, file.FullName) + " " + file.Length).Order(StringComparer.Ordinal)));

        async Task<string> HashAsync(string path) => Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, f.Token)));
    }
}
