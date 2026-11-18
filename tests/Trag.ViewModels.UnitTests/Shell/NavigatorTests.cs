using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Shell;

public sealed class NavigatorTests
{
    [Fact]
    public void WhenASectionIsChosenAndChosenAgain_ShouldMoveOnceAndAnnounceTheReselection()
    {
        var navigator = Following(new Repository());
        var sections = new List<WorkspaceSection>();
        var reselected = new List<WorkspaceSection>();
        navigator.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Navigator.Section)) sections.Add(navigator.Section); };
        navigator.Reselected += reselected.Add;
        navigator.ShowHistory();
        Assert.Equal([WorkspaceSection.History], sections);
        navigator.ShowHistory();
        Assert.Single(sections); Assert.Equal([WorkspaceSection.History], reselected);
        navigator.ShowSearchCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Search, navigator.Section);
        navigator.ShowFileStatus();
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
    }

    [Fact]
    public void WhenNoMergeIsInProgress_ShouldNotShowTheMerge()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        navigator.ShowMergeCommand.Execute(null);
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
        repository.Apply(Merging());
        Assert.Equal(WorkspaceSection.Merge, navigator.Section); // a merge that begins opens its section by itself
        navigator.ShowFileStatus();
        navigator.ShowMergeCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Merge, navigator.Section);
    }

    [Fact]
    public void WhenANewMergeBeginsOrEnds_ShouldOpenItsSectionFromAnywhereAndLeaveItOnlyFromThere()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        repository.Apply(Snapshots.Create());
        navigator.Section = WorkspaceSection.History;
        repository.Apply(Merging());
        Assert.Equal(WorkspaceSection.Merge, navigator.Section);
        navigator.Section = WorkspaceSection.History;
        repository.Apply(Merging(files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")]));
        Assert.Equal(WorkspaceSection.History, navigator.Section); // the same merge, seen again
        repository.Apply(Merging(incoming: Snapshots.Id('e')));
        Assert.Equal(WorkspaceSection.Merge, navigator.Section); // another incoming commit is another merge
        navigator.Section = WorkspaceSection.History;
        repository.Apply(Snapshots.Create());
        Assert.Equal(WorkspaceSection.History, navigator.Section);
        repository.Apply(Merging());
        repository.Apply(Snapshots.Create());
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
    }

    [Fact]
    public void WhenARebaseBeginsStopsAgainAndEnds_ShouldOpenItsSectionKeepTheUsersChoiceAndLeaveIt()
    {
        var repository = new Repository(hasWriter: true);
        var navigator = Following(repository);
        repository.Apply(Snapshots.Create());
        repository.Apply(Snapshots.Rebasing());
        Assert.Equal(WorkspaceSection.Rebase, navigator.Section);
        navigator.Section = WorkspaceSection.History;
        repository.Apply(Snapshots.Rebasing(Snapshots.Rebase(step: 2)));
        Assert.Equal(WorkspaceSection.History, navigator.Section);
        navigator.Section = WorkspaceSection.Rebase;
        repository.Apply(Snapshots.Create());
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
    }

    // So the merge view finds its conflicts listed.
    [Fact]
    public void WhenAMergeIsApplied_ShouldOpenTheMergeSectionOnlyAfterEveryFollowerAppliedTheSnapshot()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        var seen = new List<WorkspaceSection>();
        repository.SnapshotApplied += (_, _) => seen.Add(navigator.Section);
        repository.Apply(Merging());
        Assert.Equal([WorkspaceSection.FileStatus], seen); Assert.Equal(WorkspaceSection.Merge, navigator.Section);
    }

    // The merge and the history chosen after it are what the other repository returns from.
    [Fact]
    public void WhenAnotherRepositoryIsFollowed_ShouldReturnToTheFileStatusAndIgnoreTheOneThatWent()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        repository.Apply(Merging());
        navigator.ShowHistory();
        var next = new Repository();
        navigator.Follow(next);
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
        repository.Apply(Merging(incoming: Snapshots.Id('e')));
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section); // the repository that went no longer moves it
    }

    [Fact]
    public void WhenTheOpenRepositoryIsShownWithoutAMergeOrRebase_ShouldShowTheFileStatus()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        repository.Apply(Snapshots.Create());
        navigator.ShowHistory();
        navigator.ShowOpenRepository();
        Assert.Equal(WorkspaceSection.FileStatus, navigator.Section);
    }

    [Fact]
    public void WhenTheOpenRepositoryIsShownDuringAMerge_ShouldShowTheMerge()
    {
        var repository = new Repository();
        var navigator = Following(repository);
        repository.Apply(Merging());
        navigator.ShowHistory();
        navigator.ShowOpenRepository();
        Assert.Equal(WorkspaceSection.Merge, navigator.Section);
    }

    [Fact]
    public void WhenTheOpenRepositoryIsShownDuringARebase_ShouldShowTheRebase()
    {
        var repository = new Repository(hasWriter: true);
        var navigator = Following(repository);
        repository.Apply(Snapshots.Rebasing());
        navigator.ShowHistory();
        navigator.ShowOpenRepository();
        Assert.Equal(WorkspaceSection.Rebase, navigator.Section);
    }

    private static Navigator Following(Repository repository)
    {
        var navigator = new Navigator();
        navigator.Follow(repository);
        return navigator;
    }

    private static RepositorySnapshot Merging(string incoming = "incoming", FileStatus[]? files = null) =>
        Snapshots.Create(files: files ?? [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge,
            merge: new(incoming, Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
}
