using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Merge;

// Conflicts a stash left when it was applied over changed files: the file status sends them to the conflict view, the
// sidebar offers that view while they last, and once the last one is staged the view gives way to the file status.
public sealed class StashConflictViewTests
{
    private static RepositorySnapshot Applied(params FileStatus[] files) => Snapshots.Create(operation: RepositoryOperation.Conflict, files: files);

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Applied(Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt"));
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    [Fact]
    public async Task WhenAConflictIsOpenedFromTheFileStatus_ShouldShowTheConflictView()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);

        model.Resolution.ShowConflictCommand.Execute(model.Resolution.Conflicts[0]);

        Assert.Equal(WorkspaceSection.Conflicts, model.Navigator.Section);
        Assert.Same(model.ConflictsPage, model.CurrentSection);
        Assert.Equal("a.txt", model.Resolution.SelectedConflict?.PathText);
    }

    [Fact]
    public async Task WhenTheSidebarOffersTheConflicts_ShouldOpenTheirViewOnlyWhileThereAreAny()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.True(model.Repository.HasStashConflicts);

        model.Navigator.ShowConflictsCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Conflicts, model.Navigator.Section);

        var raised = new List<string?>();
        model.Repository.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt"), Snapshots.Staged("b.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasStashConflicts);
        Assert.Contains(nameof(model.Repository.HasStashConflicts), raised);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        model.Navigator.ShowConflictsCommand.Execute(null);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
    }
}
