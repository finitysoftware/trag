using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Merge;

// The merge state that MergeViewModel and its section and write followers hold between
// them today. The refactor moves it into one place; these tests pin what has to survive the move.
public sealed class MergeStateTests
{
    private static readonly RepositoryMerge Topic = new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", "");

    private static RepositorySnapshot Merging(RepositoryMerge merge, params FileStatus[] files) =>
        Snapshots.Create(files: files, operation: RepositoryOperation.Merge, merge: merge);

    [Fact]
    public async Task WhenTheMergeChangesState_ShouldNameTheStateInTheBanner()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("", model.Merge.Overview.Remaining);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("2 of 2 files are still in conflict.", model.Merge.Overview.Remaining);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Staged("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("All conflicts are resolved. Review the result and finish.", model.Merge.Overview.Remaining);

        // HEAD moved since the merge began: Git's own tools have to finish it.
        h.Reader.Snapshot = Merging(Topic with { OriginalHead = Snapshots.Id('b') }, Snapshots.Conflict("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.StartsWith("The original HEAD or repository state changed.", model.Merge.Overview.Remaining);

        h.Reader.Snapshot = Merging(Topic with { Limitation = "Octopus merges are not supported." }, Snapshots.Conflict("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Octopus merges are not supported.", model.Merge.Overview.Remaining);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt")) with { Limitations = ["Sparse checkout is not supported."] };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Sparse checkout is not supported.", model.Merge.Overview.Remaining);

        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("", model.Merge.Overview.Remaining);
    }

    [Fact]
    public async Task WhenAnotherMergeBegins_ShouldResetTheDraftAndOpenTheMergeViewFromAnySection()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.Equal("Merge branch 'topic'", model.Merge.Message);
        model.Merge.Message = "Reviewed";
        model.Merge.RequestAbortMergeCommand.Execute(null); Assert.True(model.Merge.Abort.IsOpen);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;

        // The same merge with one conflict more staged: the draft, the confirmation and the section are kept.
        h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Reviewed", model.Merge.Message); Assert.True(model.Merge.Abort.IsOpen); Assert.Equal(WorkspaceSection.History, model.Navigator.Section);

        // Another incoming commit is another merge: Git's message replaces the draft, the confirmation closes, the merge view opens.
        var other = Topic with { IncomingHead = Snapshots.Id('e'), Message = "Merge branch 'other'" };
        h.Reader.Snapshot = Merging(other, Snapshots.Conflict("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Merge branch 'other'", model.Merge.Message); Assert.False(model.Merge.Abort.IsOpen); Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
    }

    [Fact]
    public async Task WhenSnapshotsResolveConflicts_ShouldKeepTheSelectedConflictByPathOrFallBackToTheFirst()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt"), Snapshots.Conflict("c.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("a.txt", model.Resolution.SelectedConflict!.PathText);
        // Choosing a conflict in the merge view reads its working file for the blocks; the diff pane has nothing to show.
        model.Resolution.SelectedConflict = model.Resolution.Conflicts[1]; await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Equal("b.txt", h.Diff.ConflictReads[^1].Path.Display); Assert.Null(model.FileStatus.SelectedStatus);
        Assert.NotEmpty(model.ConflictsPage.Conflict.Document.Value.Parts);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Staged("a.txt"), Snapshots.Conflict("b.txt"), Snapshots.Conflict("c.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("b.txt", model.Resolution.SelectedConflict!.PathText); Assert.Same(model.Resolution.Conflicts[0], model.Resolution.SelectedConflict);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Staged("a.txt"), Snapshots.Staged("b.txt"), Snapshots.Conflict("c.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("c.txt", model.Resolution.SelectedConflict!.PathText);

        h.Reader.Snapshot = Merging(Topic, Snapshots.Staged("a.txt"), Snapshots.Staged("b.txt"), Snapshots.Staged("c.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
        // Nothing left to resolve: Git's own message and the identity are enough to finish.
        Assert.Null(model.Resolution.SelectedConflict); Assert.True(model.Repository.HasMerge);
        Assert.False(model.Resolution.StageResolutionCommand.CanExecute(null)); Assert.True(model.Merge.FinishMergeCommand.CanExecute(null));
    }

    // An edit made in the editor shows without choosing the file again; the open and every snapshot read its working file
    // once.
    [Fact]
    public async Task WhenASnapshotArrives_ShouldReadTheSelectedConflictAgainSoAnEditShows()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Single(model.ConflictsPage.Conflict.Parts, part => part.Part.IsBlock); Assert.Single(h.Diff.ConflictReads);

        h.Diff.Conflict = _ => new(string.Join('\n', "a", "<<<<<<< HEAD", "mine", "=======", "theirs", ">>>>>>> incoming",
            "b", "<<<<<<< HEAD", "x", "=======", "y", ">>>>>>> incoming", ""), "edited");
        await model.RefreshCommand.ExecuteAsync(null); await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Equal(2, h.Diff.ConflictReads.Count); Assert.Equal(2, model.ConflictsPage.Conflict.Parts.Count(part => part.Part.IsBlock));
        Assert.Equal("edited", model.ConflictsPage.Conflict.Document.Value.Hash);
    }

    [Fact]
    public async Task WhenAFileIsMarkedResolved_ShouldMoveToTheNextConflict()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Merging(Topic, Snapshots.Conflict("a.txt"), Snapshots.Conflict("b.txt"));
        h.Writer.Handle = (request, _) =>
        {
            var stage = Assert.IsType<StageRequest>(request); Assert.True(stage.Resolution); Assert.Equal("a.txt", Assert.Single(stage.Paths).Display);
            h.Reader.Snapshot = Merging(Topic, Snapshots.Staged("a.txt"), Snapshots.Conflict("b.txt"));
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Staged the resolution."));
        };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Equal("a.txt", model.Resolution.SelectedConflict!.PathText); Assert.Equal("a.txt", h.Diff.ConflictReads[^1].Path.Display);

        await model.Resolution.StageResolutionCommand.ExecuteAsync(null); await model.ConflictsPage.Conflict.Document.Completion;

        Assert.Equal("b.txt", model.Resolution.SelectedConflict!.PathText);
        Assert.Equal("b.txt", h.Diff.ConflictReads[^1].Path.Display);
        Assert.Equal("a.txt", Assert.Single(model.Resolution.Resolved).PathText);
        Assert.Single(model.FileStatus.StagedFiles); Assert.Equal("1 of 2 files is still in conflict.", model.Merge.Overview.Remaining);
    }
}
