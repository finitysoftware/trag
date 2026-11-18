using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// A cherry-pick stopped at its conflicts: resolved in Trag in the view a revert uses, named for the cherry-pick; the
// current branch is the user's side, the picked commit the other.
public sealed class CherryPickConflictTests
{
    private static readonly string Picked = Snapshots.Id('b');

    private static RepositorySnapshot Stopped(params FileStatus[] files) =>
        Snapshots.Create(operation: RepositoryOperation.CherryPick, files: files) with { PickHead = Picked };

    [Fact]
    public void WhenACherryPickStoppedAtConflicts_ShouldSendThemToTheCherryPickView()
    {
        var scope = ConflictScope.For(Stopped(Snapshots.Conflict("a.txt")), MergeFiles.Empty);

        Assert.Equal(WorkspaceSection.Pick, scope.Section);
        Assert.Equal(("Open cherry-pick view", "Resolve in cherry-pick view →"), (scope.OpenText, scope.ResolveText));
        Assert.Equal("1 file is in conflict. Conflicts are resolved in the cherry-pick view, not here.", scope.Banner);
        Assert.Equal("1 file is in conflict. Resolve each, then finish the cherry-pick.", scope.ViewLine);
        Assert.Equal("Cherry-pick in progress", PickText.InProgress(RepositoryOperation.CherryPick));
    }

    [Fact]
    public void WhenACherryPickStoppedAtConflicts_ShouldNameTheBranchAsYoursAndThePickedCommitAsTheOther()
    {
        var sides = ConflictSides.For(Stopped(Snapshots.Conflict("a.txt")));

        Assert.Equal(("CURRENT", "main", ConflictChoice.Mine), (sides.Yours.Label, sides.Yours.Name, sides.Yours.Git));
        Assert.Equal(("CHERRY-PICK", "the cherry-pick of " + Picked[..7], ConflictChoice.Theirs), (sides.Other.Label, sides.Other.Name, sides.Other.Git));
        Assert.Equal("the cherry-pick", sides.Change);
    }

    // Like a revert's, a cherry-pick's view lists what is decided beside what is open, and the pick takes the writes a
    // revert in progress takes.
    [Fact]
    public void WhenACherryPickStoppedAtConflicts_ShouldListItsDecidedFilesAndTakeTheWritesOfAPickInProgress()
    {
        var stopped = Stopped(Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt"));

        Assert.Equal(["b.txt"], MergeFiles.From(stopped).Resolved.Select(resolved => resolved.Path.Display));
        Assert.True(stopped.HasSupportedPick);
        Assert.True(WriteNeeds.StateAllows(new PickContinueRequest(stopped), stopped));
        Assert.True(WriteNeeds.StateAllows(new ConflictSideRequest(stopped, Snapshots.Path("a.txt"), ConflictChoice.Mine), stopped));
        Assert.Equal(("Finishing the cherry-pick…", "Aborting the cherry-pick and verifying the result…"),
            (WriteText.Progress(new PickContinueRequest(stopped)), WriteText.Progress(new PickAbortRequest(stopped))));
    }
}
