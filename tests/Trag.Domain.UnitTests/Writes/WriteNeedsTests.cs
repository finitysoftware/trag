using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Writes;

// What each write needs from the repository: the operation it continues, or none in progress.
public sealed class WriteNeedsTests
{
    // Before any writer starts: a state the write cannot run in, a repository other than the one shown, and for a commit
    // an identity missing or other than the one shown.
    [Fact]
    public void WhenTheFreshRepositoryIsNotTheOneShown_ShouldRefuseTheWriteWithTheReason()
    {
        var shown = Snapshots.Create(files: [Snapshots.Modified("a")]) with { GitDirectory = "fixture/.git" };
        var stage = new StageRequest(shown, [Snapshots.Path("a")]);
        Assert.Null(WriteNeeds.Refusal(stage, shown));
        Assert.Equal(WriteText.StateUnsupported, WriteNeeds.Refusal(stage, shown with { GitDirectory = null }));
        Assert.Equal(WriteText.StateUnsupported, WriteNeeds.Refusal(stage, shown with { Operation = RepositoryOperation.Rebase }));
        Assert.Equal(WriteText.ChangedOutside, WriteNeeds.Refusal(stage, shown with { Head = Snapshots.Id('d') }));

        var commit = new CommitRequest(shown, "Fix it");
        Assert.Null(WriteNeeds.Refusal(commit, shown));
        Assert.Equal(WriteText.IdentityMissing, WriteNeeds.Refusal(commit, Snapshots.Create(files: [Snapshots.Modified("a")], identity: false) with { GitDirectory = "fixture/.git" }));
        Assert.Equal(WriteText.IdentityChanged, WriteNeeds.Refusal(commit, shown with { Author = shown.Author! with { Name = "Someone Else" } }));
    }

    [Fact]
    public void WhenAMergeIsAborted_ShouldNeedThatMerge()
    {
        var snapshot = Snapshots.Create();
        Assert.Equal(WriteNeed.Merge, WriteNeeds.Of(new MergeAbortRequest(snapshot)));
    }

    [Fact]
    public void WhenAnOrdinaryCommitIsMade_ShouldNeedAWritableRepository()
    {
        var snapshot = Snapshots.Create();
        Assert.Equal(WriteNeed.Writable, WriteNeeds.Of(new CommitRequest(snapshot, "m")));
    }

    [Fact]
    public void WhenAConflictIsResolved_ShouldNeedAMergeOrARebase()
    {
        var (clean, merging, rebasing) = States();
        var side = new ConflictSideRequest(clean, Snapshots.Path("f.txt"), ConflictChoice.Mine);
        Assert.Equal([false, true, true], new[] { clean, merging, rebasing }.Select(state => WriteNeeds.StateAllows(side, state)));
    }

    [Fact]
    public void WhenARebaseContinues_ShouldNeedThatRebase()
    {
        var (clean, merging, rebasing) = States();
        var next = new RebaseContinueRequest(clean, "m");
        Assert.Equal([false, false, true], new[] { clean, merging, rebasing }.Select(state => WriteNeeds.StateAllows(next, state)));
    }

    [Fact]
    public void WhenACommitFinishesAMerge_ShouldNeedThatMerge()
    {
        var (clean, merging, rebasing) = States();
        var finish = new CommitRequest(clean, "m", FinishesMerge: true);
        Assert.Equal([false, true, false], new[] { clean, merging, rebasing }.Select(state => WriteNeeds.StateAllows(finish, state)));
    }

    [Fact]
    public void WhenARebaseStarts_ShouldNeedNoOperationInProgress()
    {
        var (clean, merging, rebasing) = States();
        var start = new RebaseRequest(clean, "main", Snapshots.Id('b'));
        Assert.Equal([true, false, false], new[] { clean, merging, rebasing }.Select(state => WriteNeeds.StateAllows(start, state)));
    }

    // Finishing and aborting a revert need a revert Trag can finish; a conflict of one is resolved like a merge's, and a
    // revert in progress without what Git keeps for it is none Trag finishes.
    [Fact]
    public void WhenARevertIsFinishedOrAborted_ShouldNeedThatRevert()
    {
        var (clean, merging, _) = States();
        var reverting = Snapshots.Create(operation: RepositoryOperation.Revert) with { PickHead = Snapshots.Id('b') };
        var unknown = Snapshots.Create(operation: RepositoryOperation.Revert);
        GitWriteRequest[] writes = [new PickContinueRequest(clean), new PickAbortRequest(clean)];
        Assert.All(writes, write => Assert.Equal([false, false, true, false],
            new[] { clean, merging, reverting, unknown }.Select(state => WriteNeeds.StateAllows(write, state))));
        Assert.True(WriteNeeds.StateAllows(new ConflictSideRequest(clean, Snapshots.Path("f.txt"), ConflictChoice.Mine), reverting));
    }

    // What the fresh repository keeps a write from starting with, before Git is asked: a pick, a merge, a rebase or an edit
    // of commits needs a clean tree, a merge and a rebase a branch with a commit, a new stash a first commit and something
    // to keep, and the branch checked out is not deleted.
    [Fact]
    public void WhenTheFreshRepositoryCannotStartTheWrite_ShouldRefuseItBeforeGitStarts()
    {
        var clean = Snapshots.Create() with { GitDirectory = "fixture/.git" };
        var changed = clean with { Files = [Snapshots.Modified("a")] };
        var detached = clean with { Branch = null };
        var unborn = Snapshots.Create(unborn: true) with { GitDirectory = "fixture/.git" };
        string? Refusal(Func<RepositorySnapshot, GitWriteRequest> build, RepositorySnapshot fresh) => WriteNeeds.Refusal(build(fresh), fresh);

        Assert.Equal(RevertText.NeedsCleanTree, Refusal(fresh => new RevertRequest(fresh, Snapshots.Id('b')), changed));
        Assert.Equal(CherryPickText.NeedsCleanTree, Refusal(fresh => new CherryPickRequest(fresh, Snapshots.Id('b')), changed));
        Assert.Equal(MergeOutcomeText.NeedsCleanTree, Refusal(fresh => new MergeRequest(fresh, "topic", Snapshots.Id('b'), false, "m"), changed));
        Assert.Equal(MergeOutcomeText.NeedsBranch, Refusal(fresh => new MergeRequest(fresh, "topic", Snapshots.Id('b'), false, "m"), detached));
        Assert.Equal(RebaseText.NeedsCleanTree, Refusal(fresh => new RebaseRequest(fresh, "main", Snapshots.Id('b')), changed));
        Assert.Equal(RebaseText.NeedsBranch, Refusal(fresh => new RewriteRequest(fresh, null, []), detached));
        Assert.Equal(StashOutcomeText.FirstCommitNeeded, Refusal(fresh => new StashRequest(fresh, new(), StashAction.Push), unborn));
        Assert.Equal(StashOutcomeText.NothingToStash, Refusal(fresh => new StashRequest(fresh, new(), StashAction.Push), clean));
        Assert.Equal(BranchOutcomeText.DeleteCurrent("main"), Refusal(fresh => new BranchRequest(fresh, new("main"), BranchAction.Delete), clean));
        Assert.Null(Refusal(fresh => new RevertRequest(fresh, Snapshots.Id('b')), clean));
        Assert.Null(Refusal(fresh => new StashRequest(fresh, new(), StashAction.Push), changed));
        Assert.Null(Refusal(fresh => new BranchRequest(fresh, new("topic"), BranchAction.Delete), clean));
    }

    // A clean repository, one in a merge Trag understands, and one in a rebase.
    private static (RepositorySnapshot Clean, RepositorySnapshot Merging, RepositorySnapshot Rebasing) States() =>
        (Snapshots.Create(), Snapshots.Create(operation: RepositoryOperation.Merge, merge: new(Snapshots.Id('b'), Snapshots.HeadId, "m", "fp", "")), Snapshots.Rebasing());

    // A stash keeps nothing of a submodule, neither what it has checked out nor a move of it already staged: Git saves
    // no stash then and says there are no local changes. A tree whose only changes are submodules is refused with that reason, unless one is a staged typechange between a file and a gitlink, which Git keeps.
    [Fact]
    public void WhenOnlySubmodulesChanged_ShouldRefuseTheStashAndSayWhy()
    {
        var clean = Snapshots.Create() with { GitDirectory = "fixture/.git" };
        var moved = new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, IsSubmoduleMoved: true);
        string? Push(params FileStatus[] files) => WriteNeeds.Refusal(new StashRequest(clean with { Files = files }, new(), StashAction.Push), clean with { Files = files });

        Assert.Contains("submodule", Push(moved), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("submodule", Push(moved with { IsSubmoduleMoved = false, SubmoduleHasOwnChanges = true }), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(StashOutcomeText.OnlySubmodules, Push(moved with { Index = 'M', Worktree = '.' }));
        Assert.Null(Push(moved with { Index = 'T', Worktree = '.', IsSubmoduleMoved = false }));
        Assert.Null(Push(moved, Snapshots.Modified("a.txt")));
    }
}
