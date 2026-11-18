using System.Collections.Specialized;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Status;

// The file lists' selection as the commit pane and a live update see it: a multi-selection in either list asks the
// commands again, and a live update keeps the user's selection whatever the list does while it is edited.
public sealed class StatusSelectionTests
{
    [Fact]
    public async Task WhenSeveralRowsAreSelectedInEitherList_ShouldRenameTheActionsAtOnce()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt"), Snapshots.Staged("b.txt"), Snapshots.Modified("c.txt"), Snapshots.Modified("d.txt")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");

        model.FileStatus.StagedSelection.Add((StatusRow)model.FileStatus.StagedFiles[0]);
        model.FileStatus.StagedSelection.Add((StatusRow)model.FileStatus.StagedFiles[1]);
        Assert.Equal(CommitText.UnstageAction(2), model.Commit.UnstageActionText);
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[0]);
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[1]);
        Assert.Equal(CommitText.StageAction(2), model.Commit.StageActionText);
    }

    // A list control lets go of a row the update replaces, which clears the selection; that is the update's doing, not
    // the user's.
    [Fact]
    public async Task WhenALiveUpdateMakesTheListLetGoOfTheSelectedRow_ShouldPutTheSelectionBackOnTheFilesNewRow()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[1];
        NotifyCollectionChangedEventHandler letGo = (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Replace && model.FileStatus.SelectedUnstaged is { } selected
                && e.OldItems![0] is StatusRow old && old.File.Path.IsSame(selected.File.Path))
                model.FileStatus.SelectedUnstaged = null;
        };
        model.FileStatus.UnstagedFiles.CollectionChanged += letGo;

        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), new FileStatus(Snapshots.Path("b.txt"), '.', 'D')]);
        h.Watcher.Latest.Push("b.txt");
        // The update runs off the test's thread: the selection is back once it is on the file's new row.
        await ShellHarness.Until(() => model.FileStatus.SelectedUnstaged?.File.Worktree == 'D', "the selection back on the new row");
        Assert.Equal("b.txt", model.FileStatus.SelectedUnstaged!.PathText);
    }
}
