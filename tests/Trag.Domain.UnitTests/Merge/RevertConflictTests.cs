using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// A revert stopped at its conflicts. They are resolved in Trag in the revert view, which finishes or aborts the revert;
// the current branch is the user's side, the revert the other.
public sealed class RevertConflictTests
{
    private static readonly string Reverted = Snapshots.Id('b');

    private static RepositorySnapshot Stopped(params FileStatus[] files) =>
        Snapshots.Create(operation: RepositoryOperation.Revert, files: files) with { PickHead = Reverted };

    [Fact]
    public void WhenARevertStoppedAtConflicts_ShouldSendThemToTheRevertView()
    {
        var scope = ConflictScope.For(Stopped(Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt")), MergeFiles.Empty);

        Assert.Equal(WorkspaceSection.Pick, scope.Section);
        Assert.Equal(("Open revert view", "Resolve in revert view →"), (scope.OpenText, scope.ResolveText));
        Assert.True(scope.ShowsBanner);
        Assert.Equal("2 files are in conflict. Conflicts are resolved in the revert view, not here.", scope.Banner);
        Assert.Equal("2 files are in conflict. Resolve each, then finish the revert.", scope.ViewLine);
        Assert.Equal("Finish would commit conflict markers.", scope.MarkersLead);
        Assert.Equal(Trag.Domain.Presentation.IconKinds.Merge, scope.Icon);
    }

    // With every conflict resolved the revert is still in progress until it is finished, which the banners say.
    [Fact]
    public void WhenEveryConflictOfTheRevertIsResolved_ShouldSayTheRevertIsLeftToFinish()
    {
        var scope = ConflictScope.For(Stopped(Snapshots.Staged("a.txt")), MergeFiles.Empty);

        Assert.Equal("Every conflict is resolved. Finish the revert in the revert view.", scope.Banner);
        Assert.Equal("Every conflict is resolved. Finish the revert to commit it.", scope.ViewLine);
    }

    // Like a merge's, a revert's view lists what is decided beside what is open: here the file Git undid on its own.
    [Fact]
    public void WhenARevertStoppedAtConflicts_ShouldListTheFilesItDecidedBesideTheOpenOnes()
    {
        var files = MergeFiles.From(Stopped(Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")));

        Assert.Equal(["a.txt"], files.Conflicts.Select(conflict => conflict.File.Path.Display));
        Assert.Equal(["b.txt"], files.Resolved.Select(resolved => resolved.Path.Display));
    }

    [Fact]
    public void WhenARevertStoppedAtConflicts_ShouldNameTheBranchAsYoursAndTheRevertAsTheOther()
    {
        var sides = ConflictSides.For(Stopped(Snapshots.Conflict("a.txt")));

        Assert.Equal(("CURRENT", "main", ConflictChoice.Mine), (sides.Yours.Label, sides.Yours.Name, sides.Yours.Git));
        Assert.Equal(("REVERT", "the revert of " + Reverted[..7], ConflictChoice.Theirs), (sides.Other.Label, sides.Other.Name, sides.Other.Git));
        Assert.Equal("the revert", sides.Change);
    }
}
