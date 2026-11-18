using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Merge;

// Conflicts Git leaves with no merge or rebase in progress, as applying a stash over changed files does. They are
// resolved in Trag file by file in a view of their own; the stash is the user's side, the current branch the other.
public sealed class StashConflictTests
{
    private static RepositorySnapshot Applied(IReadOnlyList<string>? limitations = null) =>
        Snapshots.Create(operation: RepositoryOperation.Conflict, files: [Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt")], limitations: limitations);

    [Fact]
    public void WhenAStashLeftConflicts_ShouldLetThemBeResolved() => Assert.True(Applied().HasSupportedResolution);

    [Fact]
    public void WhenTheRepositoryHasALimitation_ShouldLeaveTheConflictsToGitTools()
    {
        var limited = Applied([LimitationText.LinkedWorktree]);
        Assert.False(limited.HasSupportedResolution);
        Assert.False(ConflictScope.For(limited, MergeFiles.Empty).HasView);
    }

    [Fact]
    public void WhenAStashLeftConflicts_ShouldSendThemToTheConflictView()
    {
        var scope = ConflictScope.For(Applied(), MergeFiles.Empty);
        Assert.Equal(WorkspaceSection.Conflicts, scope.Section);
        Assert.Equal(("Open conflict view", "Resolve in conflict view →"), (scope.OpenText, scope.ResolveText));
        Assert.True(scope.ShowsBanner);
        Assert.Equal("2 files are in conflict. Conflicts are resolved in the conflict view, not here.", scope.Banner);
        Assert.Equal("Unstaging waits until every conflict is resolved in the conflict view.", scope.UnstageTip);
        Assert.Equal("2 files are in conflict. A file is done once it is marked as resolved; nothing is finished afterwards.", scope.ViewLine);
    }

    [Fact]
    public void WhenAStashLeftConflicts_ShouldNameTheStashAsYoursAndTheBranchAsTheOther()
    {
        var sides = ConflictSides.For(Applied());
        Assert.Equal(("STASH", "the stash", ConflictChoice.Theirs), (sides.Yours.Label, sides.Yours.Name, sides.Yours.Git));
        Assert.Equal(("CURRENT", "main", ConflictChoice.Mine), (sides.Other.Label, sides.Other.Name, sides.Other.Git));
    }
}
