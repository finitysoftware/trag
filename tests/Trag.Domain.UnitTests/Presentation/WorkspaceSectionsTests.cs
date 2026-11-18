using Trag.Domain.Presentation;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation;

// Which section belongs to what: the open repository goes on in the view of its merge, rebase, revert or cherry-pick,
// a decided file's diff shows in those views, and the working tree's diff in them and in the file status.
public sealed class WorkspaceSectionsTests
{
    [Theory]
    [InlineData(RepositoryOperation.Merge, WorkspaceSection.Merge)]
    [InlineData(RepositoryOperation.Rebase, WorkspaceSection.Rebase)]
    [InlineData(RepositoryOperation.Revert, WorkspaceSection.Pick)]
    [InlineData(RepositoryOperation.CherryPick, WorkspaceSection.Pick)]
    [InlineData(RepositoryOperation.Conflict, WorkspaceSection.FileStatus)]
    [InlineData(RepositoryOperation.None, WorkspaceSection.FileStatus)]
    public void WhenTheOpenRepositoryIsShown_ShouldGoWhereItsWorkGoesOn(RepositoryOperation operation, WorkspaceSection section) =>
        Assert.Equal(section, WorkspaceSections.Open(operation));

    // The view of an operation exists only while that operation is in progress; every other section always does.
    [Theory]
    [InlineData(WorkspaceSection.Merge, RepositoryOperation.Merge, true)]
    [InlineData(WorkspaceSection.Merge, RepositoryOperation.None, false)]
    [InlineData(WorkspaceSection.Rebase, RepositoryOperation.Rebase, true)]
    [InlineData(WorkspaceSection.Rebase, RepositoryOperation.Merge, false)]
    [InlineData(WorkspaceSection.Conflicts, RepositoryOperation.Conflict, true)]
    [InlineData(WorkspaceSection.Conflicts, RepositoryOperation.None, false)]
    [InlineData(WorkspaceSection.Pick, RepositoryOperation.Revert, true)]
    [InlineData(WorkspaceSection.Pick, RepositoryOperation.CherryPick, true)]
    [InlineData(WorkspaceSection.Pick, RepositoryOperation.Rebase, false)]
    [InlineData(WorkspaceSection.FileStatus, RepositoryOperation.Merge, true)]
    [InlineData(WorkspaceSection.History, RepositoryOperation.Rebase, true)]
    [InlineData(WorkspaceSection.Repositories, RepositoryOperation.None, true)]
    public void WhenAnOperationIsOrIsNotInProgress_ShouldSayWhetherASectionExists(WorkspaceSection section, RepositoryOperation operation, bool exists) =>
        Assert.Equal(exists, WorkspaceSections.Exists(section, operation));

    // The conflict view a stash left lists no decided files, so it shows no diff.
    [Fact]
    public void WhenASectionIsShown_ShouldSayWhetherItShowsADecidedFilesDiffAndTheWorkingDiff()
    {
        Assert.Equal([WorkspaceSection.Merge, WorkspaceSection.Rebase, WorkspaceSection.Pick],
            Enum.GetValues<WorkspaceSection>().Where(WorkspaceSections.ShowsDecidedDiff));
        Assert.Equal([WorkspaceSection.FileStatus, WorkspaceSection.Merge, WorkspaceSection.Rebase, WorkspaceSection.Pick],
            Enum.GetValues<WorkspaceSection>().Where(WorkspaceSections.ShowsWorkingDiff));
    }
}
