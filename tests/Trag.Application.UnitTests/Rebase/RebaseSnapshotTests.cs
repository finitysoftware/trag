using Trag.Application.Live;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Rebase;

// A rebase in progress in the snapshot: which one Trag continues, what the header says while Git has HEAD detached, and
// the section it opens and leaves.
public sealed class RebaseSnapshotTests
{
    // Git has HEAD detached while it replays, but that is the rebase, not a detached head that could leave commits behind.
    [Fact]
    public void WhenTragCanContinueARebase_ShouldAllowRebaseAndResolutionButNotTreatItAsADetachedHead()
    {
        var snapshot = Snapshots.Rebasing();
        Assert.True(snapshot.HasSupportedRebase); Assert.True(snapshot.HasSupportedResolution); Assert.False(snapshot.HasSupportedMerge);
        Assert.True(snapshot.IsDetached); Assert.False(snapshot.IsDetachedAtCommit); Assert.False(snapshot.LeavesCommitsBehind);
    }

    [Fact]
    public void WhenTragCanContinueARebase_ShouldNameTheBranchInTheHeaderWithNoWarning()
    {
        var header = RepositoryHeader.From(Snapshots.Rebasing());
        Assert.Equal("Rebasing feature onto main", header.BranchText);
        Assert.Equal("", header.Warning);
    }

    [Fact]
    public void WhenARebaseStopsAtItsFirstCommit_ShouldNameThatCommitWithNoneApplied()
    {
        var snapshot = Snapshots.Rebasing();
        Assert.Equal(("Commit 1", 0), (snapshot.Rebase!.Current!.Subject, snapshot.Rebase.Applied));
    }

    [Fact]
    public void WhenTheRebaseGoesOntoACommitNoBranchNames_ShouldNameItsShortId()
    {
        Assert.Equal("Rebasing feature onto " + Snapshots.Id('c')[..7], RebaseText.Rebasing(Snapshots.Rebase(ontoName: null)));
    }

    [Fact]
    public void WhenTragCannotContinueARebase_ShouldSayWhyInTheHeader()
    {
        var snapshot = Snapshots.Rebasing(Snapshots.Rebase(limitation: "This rebase uses an autostash. Continue or abort it in your Git tools."));
        Assert.False(snapshot.HasSupportedRebase); Assert.False(snapshot.HasSupportedResolution);
        Assert.Equal("This rebase uses an autostash. Continue or abort it in your Git tools.", RepositoryHeader.From(snapshot).Warning);
    }

    [Fact]
    public void WhenARebaseBegins_ShouldAllowRebaseAndResolutionWritesButNoMerge()
    {
        var repository = new Repository(hasWriter: true);
        repository.Apply(Snapshots.Create());
        repository.Apply(Snapshots.Rebasing());
        Assert.True(repository.HasRebase); Assert.True(repository.CanRebase); Assert.True(repository.CanResolve); Assert.False(repository.CanMerge);
    }

    [Fact]
    public void WhenTheRebaseStopsAtItsNextCommit_ShouldStillAllowRebaseWrites()
    {
        var repository = Rebasing();
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 2)));
        Assert.True(repository.HasRebase); Assert.True(repository.CanRebase);
    }

    [Fact]
    public void WhenTheRebaseEnds_ShouldAllowNoRebaseOrResolutionWrites()
    {
        var repository = Rebasing();
        repository.Apply(Snapshots.Create());
        Assert.False(repository.HasRebase); Assert.False(repository.CanRebase); Assert.False(repository.CanResolve);
    }

    // A repository with a writer that saw a rebase begin.
    private static Repository Rebasing()
    {
        var repository = new Repository(hasWriter: true);
        repository.Apply(Snapshots.Create());
        repository.Apply(Snapshots.Rebasing());
        return repository;
    }
}
