using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.TestSupport;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.History;

// What keeps a commit of the history back before Git is asked, each operation in its own words.
public sealed class CommitRefusalsTests
{
    private static readonly HistoryCommit Commit = Snapshots.Commit(Snapshots.Id('c'), "Fix it", Snapshots.Id('b'));
    private static readonly HistoryCommit Merge = Snapshots.Commit(Snapshots.Id('m'), "Merge", Snapshots.Id('b'), Snapshots.Id('d'));
    private static readonly RepositorySnapshot Clean = Snapshots.Create();
    private static readonly RepositorySnapshot Changed = Snapshots.Create(files: [Snapshots.Modified("a")]);
    private static readonly RepositorySnapshot Detached = Snapshots.Create(branch: null);

    [Fact]
    public void WhenACommitIsCherryPicked_ShouldRefuseAMergeChangesAndACommitTheBranchHas()
    {
        Assert.Equal(CherryPickText.MergeCommit, CommitRefusals.CherryPick(Merge, Clean, onBranch: false));
        Assert.Equal(CherryPickText.NeedsCleanTree, CommitRefusals.CherryPick(Commit, Changed, onBranch: false));
        Assert.Equal(CherryPickText.AlreadyOnBranch("ccccccc", "main"), CommitRefusals.CherryPick(Commit, Clean, onBranch: true));
        Assert.Equal("", CommitRefusals.CherryPick(Commit, Clean, onBranch: false));
    }

    [Fact]
    public void WhenACommitIsReverted_ShouldRefuseAMergeAndChanges()
    {
        Assert.Equal(RevertText.MergeCommit, CommitRefusals.Revert(Merge, Clean));
        Assert.Equal(RevertText.NeedsCleanTree, CommitRefusals.Revert(Commit, Changed));
        Assert.Equal("", CommitRefusals.Revert(Commit, Clean));
    }

    // The distance was read on one HEAD; a branch that moved since has other commits leaving it.
    [Fact]
    public void WhenTheBranchIsResetToACommit_ShouldRefuseADetachedHeadACommitOffTheBranchTheTipItselfAndAMovedBranch()
    {
        var asked = Clean.Head;
        Assert.Equal(ResetText.NeedsBranch, CommitRefusals.Reset(Commit, Detached, 2, asked));
        Assert.Equal(ResetText.NotOnBranch("ccccccc", "main"), CommitRefusals.Reset(Commit, Clean, null, asked));
        Assert.Equal(ResetText.AlreadyHere("main", "ccccccc"), CommitRefusals.Reset(Commit, Clean, 0, asked));
        Assert.Equal(ResetText.HeadMoved("main"), CommitRefusals.Reset(Commit, Clean with { Head = Snapshots.Id('e') }, 2, asked));
        Assert.Equal("Merge in progress. Finish or abort it before resetting the branch.",
            CommitRefusals.Reset(Commit, Clean with { Operation = RepositoryOperation.Merge }, 2, asked));
        Assert.Equal("Rebase in progress. Finish or abort it before resetting the branch.",
            CommitRefusals.Reset(Commit, Detached with { Operation = RepositoryOperation.Rebase }, 2, asked));
        Assert.Equal("", CommitRefusals.Reset(Commit, Clean, 2, asked));
    }

    [Fact]
    public void WhenCommitsAreEdited_ShouldRefuseADetachedHeadACommitOffTheBranchAMergeAmongThemAndChanges()
    {
        var range = new RewriteRange(Snapshots.Id('b'), [], 0);
        Assert.Equal(RewriteText.NeedsBranch, CommitRefusals.Rewrite(Commit, Detached, range));
        Assert.Equal(RewriteText.NotOnBranch("ccccccc", "main"), CommitRefusals.Rewrite(Commit, Clean, null));
        Assert.Equal(RewriteText.HasMerges, CommitRefusals.Rewrite(Commit, Clean, range with { Merges = 1 }));
        Assert.Equal(RebaseText.NeedsCleanTree, CommitRefusals.Rewrite(Commit, Changed, range));
        Assert.Equal("", CommitRefusals.Rewrite(Commit, Clean, range));
    }

    // A bisect, a patch application or a Git sequence in progress, and a limitation of the repository, keep each of them
    // back as they keep every write: the question names the operation as the header does, or the limitation itself, before
    // anything about the commit or the branch.
    [Fact]
    public void WhenBisectAmOrASequencerIsInProgressOrALimitationApplies_ShouldNameTheReason()
    {
        var range = new RewriteRange(Snapshots.Id('b'), [], 0);
        var bisect = Detached with { Operation = RepositoryOperation.Bisect };
        Assert.Equal("Bisect in progress. Finish or abort it before cherry-picking a commit.", CommitRefusals.CherryPick(Merge, bisect, onBranch: true));
        Assert.Equal("Bisect in progress. Finish or abort it before reverting a commit.", CommitRefusals.Revert(Merge, bisect));
        Assert.Equal("Bisect in progress. Finish or abort it before resetting the branch.", CommitRefusals.Reset(Commit, bisect, 2, Clean.Head));
        Assert.Equal("Bisect in progress. Finish or abort it before editing commits.", CommitRefusals.Rewrite(Commit, bisect, range));
        Assert.Equal("Patch application in progress. Finish or abort it before reverting a commit.",
            CommitRefusals.Revert(Commit, Clean with { Operation = RepositoryOperation.ApplyMailbox }));
        Assert.Equal("Git sequence in progress. Finish or abort it before cherry-picking a commit.",
            CommitRefusals.CherryPick(Commit, Clean with { Operation = RepositoryOperation.Sequencer }, onBranch: false));

        var sparse = Changed with { Limitations = [LimitationText.SparseCheckout] };
        Assert.Equal(LimitationText.SparseCheckout, CommitRefusals.CherryPick(Commit, sparse, onBranch: false));
        Assert.Equal(LimitationText.SparseCheckout, CommitRefusals.Revert(Commit, sparse));
        Assert.Equal(LimitationText.SparseCheckout, CommitRefusals.Reset(Commit, sparse, 2, Clean.Head));
        Assert.Equal(LimitationText.SparseCheckout, CommitRefusals.Rewrite(Commit, sparse, range));
    }

    // Edited from the root, the oldest commit of a shallow clone would lose the parent it names. Only a missing branch
    // comes first; merges and changes come after.
    [Fact]
    public void WhenCommitsAreEditedFromACommitWhoseParentTheShallowCloneLacks_ShouldRefuseWithTheShallowSentence()
    {
        var oldest = Snapshots.Commit(Snapshots.Id('c'), "Oldest here");
        var range = new RewriteRange(null, [], 0) { StartsAtShallowBoundary = true };
        Assert.Equal(RewriteText.NeedsBranch, CommitRefusals.Rewrite(oldest, Detached, range));
        Assert.Equal(RewriteText.ShallowStart("ccccccc"), CommitRefusals.Rewrite(oldest, Clean, range));
        Assert.Equal(RewriteText.ShallowStart("ccccccc"), CommitRefusals.Rewrite(oldest, Changed, range with { Merges = 1 }));
    }
}
