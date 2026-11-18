using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Merge;

// What the merge view writes for a conflict and a decided file, what waits for what, and how the file status hands its
// conflicts over.
public sealed class ConflictActionsTests
{
    private static readonly RepositoryMerge Incoming = new(Snapshots.Id('b'), Snapshots.HeadId,
        "Merge remote-tracking branch 'origin/dotnet8.0' into main\n\n# Conflicts:\n#\tapp.csproj\n", "fp", "");

    private static RepositorySnapshot Merging(MergeResolutions resolutions, params FileStatus[] files) =>
        Snapshots.Create(files: files, operation: RepositoryOperation.Merge, merge: Incoming) with { Resolutions = resolutions };

    private static ShellHarness Harness(RepositorySnapshot snapshot)
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = snapshot;
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        return h;
    }

    // app.csproj staged by hand with conflict markers still in it.
    private static ShellHarness MarkersStaged() =>
        Harness(Merging(new([new(Snapshots.Path("app.csproj"), Resolution.Edited, HasMarkers: true)], []), Snapshots.Staged("app.csproj")));

    // Opens with the markers staged, then reads app.csproj back as a conflict beside an unstaged notes.txt.
    private static async Task OpenThenConflictAgainAsync(ShellHarness h, ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        h.Reader.Snapshot = Merging(MergeResolutions.None, Snapshots.Conflict("app.csproj"), Snapshots.Modified("notes.txt"));
        await model.RefreshCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task WhenTheSelectedConflictIsResolved_ShouldWriteEachBlockOrSideAndWaitForTheLastBlockBeforeMarkingResolved()
    {
        var h = Harness(Merging(new([], [new(Snapshots.Path("app.csproj"), 1)]), Snapshots.Conflict("app.csproj")));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Equal("Merge remote-tracking branch 'origin/dotnet8.0' into main", model.Merge.Message);
        Assert.Equal("origin/dotnet8.0 → main", model.Merge.Overview.Direction);
        Assert.Equal("Conflict · 1 block left · mine = main · theirs = origin/dotnet8.0", model.Resolution.DetailLine);
        Assert.False(model.Resolution.StageResolutionCommand.CanExecute(null)); Assert.True(model.Resolution.ShowsMarkResolvedReason);

        var block = model.ConflictsPage.Conflict.Parts.Single(row => row.Part.IsBlock);
        Assert.True(model.ConflictsPage.Conflict.TakeOtherCommand.CanExecute(block));
        Assert.False(model.ConflictsPage.Conflict.TakeOtherCommand.CanExecute(model.ConflictsPage.Conflict.Parts.First(row => !row.Part.IsBlock)));
        await model.ConflictsPage.Conflict.TakeOtherCommand.ExecuteAsync(block);
        var take = Assert.IsType<ConflictBlockRequest>(h.Writer.LastRequest);
        Assert.Equal(("app.csproj", 0, ConflictChoice.Theirs, "hash"), (take.Path.Display, take.Block, take.Choice, take.ContentHash));

        await model.Resolution.UseYoursCommand.ExecuteAsync(null);
        var side = Assert.IsType<ConflictSideRequest>(h.Writer.LastRequest);
        Assert.Equal(("app.csproj", ConflictChoice.Mine), (side.Path.Display, side.Side));

        h.Reader.Snapshot = Merging(new([], [new(Snapshots.Path("app.csproj"), 0)]), Snapshots.Conflict("app.csproj"));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Resolution.StageResolutionCommand.CanExecute(null)); Assert.False(model.Resolution.ShowsMarkResolvedReason);
        // Resolved exactly as on the branch, the file has nothing to stage; the status line says so.
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Selected whole files staged.", Snapshot: Merging(MergeResolutions.None));
        await model.Resolution.StageResolutionCommand.ExecuteAsync(null);
        Assert.True(Assert.IsType<StageRequest>(h.Writer.LastRequest).Resolution);
        Assert.Equal("app.csproj is resolved. It is the same as on main, so there is nothing to stage for it.", model.StatusBar.Status);
    }

    [Fact]
    public async Task WhenTheOtherSideDeletedAConflictedFile_ShouldAskWhetherItStaysInsteadOfShowingBlocks()
    {
        var h = Harness(Merging(new([], [new(Snapshots.Path("Legacy.cs"), null)]), new FileStatus(Snapshots.Path("Legacy.cs"), 'U', 'D', IsConflict: true)));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.ConflictsPage.Conflict.Document.Completion;
        Assert.Equal("Conflict · origin/dotnet8.0 deleted this file, main changed it", model.Resolution.DetailLine);
        Assert.Empty(model.ConflictsPage.Conflict.Document.Value.Parts);
        Assert.Equal("Use mine to keep the version from main, or Use theirs to delete the file, as origin/dotnet8.0 did.", model.ConflictsPage.Conflict.Document.Value.Message);
        Assert.True(model.Resolution.UseOtherCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenDecidedFilesAreChosen_ShouldShowTheirDiffAndBringOneBackDirectlyOrAfterAQuestionWhenItWasEdited()
    {
        var h = Harness(Merging(new([new(Snapshots.Path("mine.txt"), Resolution.Mine), new(Snapshots.Path("edited.txt"), Resolution.Edited)], []),
            Snapshots.Staged("edited.txt"), Snapshots.Staged("auto.txt")));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(["auto.txt", "edited.txt", "mine.txt"], model.Resolution.Resolved.Select(row => row.PathText));
        Assert.Null(model.Resolution.SelectedConflict);

        model.Resolution.SelectedResolved = model.Resolution.Resolved[0];
        Assert.False(model.Resolution.RequestReopenCommand.CanExecute(null));
        Assert.Equal("auto.txt", model.FileStatus.SelectedStatus!.PathText); Assert.True(model.Resolution.ShowsDiff);

        model.Resolution.SelectedResolved = model.Resolution.Resolved[2];
        Assert.Null(model.Resolution.SelectedResolved!.Resolved.Staged); Assert.Null(model.FileStatus.SelectedStatus); Assert.False(model.Resolution.ShowsDiff);
        await model.Resolution.RequestReopenCommand.ExecuteAsync(null);
        Assert.Equal("mine.txt", Assert.IsType<ReopenConflictRequest>(h.Writer.LastRequest).Path.Display);

        var requests = h.Writer.Requests.Count;
        await model.Resolution.RequestReopenCommand.ExecuteAsync(model.Resolution.Resolved[1]);
        Assert.Equal(requests, h.Writer.Requests.Count);
        Assert.True(model.Resolution.Reopen.IsOpen);
        Assert.StartsWith("Bring the conflict back in edited.txt?", model.Resolution.Reopen.Text);
        await model.Resolution.Reopen.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("edited.txt", Assert.IsType<ReopenConflictRequest>(h.Writer.LastRequest).Path.Display);
    }

    // The write may have changed the decided file left selected.
    [Fact]
    public async Task WhenAWriteEndsWhileTheMergeViewShows_ShouldReadTheSelectedFilesDiffAgain()
    {
        var h = Harness(Merging(new([new(Snapshots.Path("mine.txt"), Resolution.Mine)], []), Snapshots.Staged("auto.txt")));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Resolution.SelectedResolved = model.Resolution.Resolved.Single(row => row.PathText == "auto.txt");
        await model.WorkingDiff.Diff.Completion;
        var reads = h.Diff.Requests.Count;

        await model.Resolution.RequestReopenCommand.ExecuteAsync(model.Resolution.Resolved.Single(row => row.PathText == "mine.txt"));
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal("mine.txt", Assert.IsType<ReopenConflictRequest>(h.Writer.LastRequest).Path.Display);
        Assert.Equal("auto.txt", model.FileStatus.SelectedStatus!.PathText); Assert.True(h.Diff.Requests.Count > reads);
    }

    // The diff shows in the pane beside the lists.
    [Theory]
    [InlineData(RepositoryOperation.Merge)]
    [InlineData(RepositoryOperation.Rebase)]
    public async Task WhenADecidedFileIsSelected_ShouldShowItsDiffInTheMergeViewAndInTheRebaseView(RepositoryOperation operation)
    {
        var decided = Snapshots.Staged("auto.txt");
        var h = Harness(operation == RepositoryOperation.Merge
            ? Snapshots.Create(files: [decided], operation: RepositoryOperation.Merge, merge: Incoming)
            : Snapshots.Rebasing(files: [decided]));
        h.Diff.Diff = (_, _, _) => new([], [new('+', "+changed", null, 1)], DiffContentKind.Text, "");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(operation == RepositoryOperation.Merge ? WorkspaceSection.Merge : WorkspaceSection.Rebase, model.Navigator.Section);

        model.Resolution.SelectedResolved = Assert.Single(model.Resolution.Resolved);
        await model.WorkingDiff.Diff.Completion;
        Assert.Equal("auto.txt", model.WorkingDiff.Diff.Document.Presentation.Source?.Path);
        Assert.Contains(model.WorkingDiff.Diff.Document.Lines, line => line.Text == "+changed");
    }

    [Fact]
    public async Task WhenAFileIsStagedWithMarkersLeft_ShouldTagItAndKeepFinishBack()
    {
        var h = MarkersStaged();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.True(model.Merge.Overview.HasMarkers);
        Assert.False(model.Merge.FinishMergeCommand.CanExecute(null));
        Assert.Equal("markers left", Assert.Single(model.Resolution.Resolved).Tag);
        Assert.Equal(model.Resolution.Resolved[0], model.Resolution.SelectedResolved);
    }

    // The notice over Finish passes that file's row to its Show file and Mark as unresolved buttons.
    [Fact]
    public async Task WhenAFileIsStagedWithMarkersLeft_ShouldNameItsRowInTheMarkersNotice()
    {
        var h = MarkersStaged();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Same(model.Resolution.Resolved[0], model.Resolution.MarkedRow);
    }

    // app.csproj comes back as a conflict after it was staged with markers left and selected as a decided file.
    [Fact]
    public async Task WhenTheFileStatusShowsAMergeConflict_ShouldLeaveItOutOfTheFilesAndSendItToTheMergeView()
    {
        var h = MarkersStaged();
        await using var model = h.Shell();
        await OpenThenConflictAgainAsync(h, model);
        model.Navigator.ShowFileStatusCommand.Execute(null);
        Assert.Equal(["notes.txt"], model.Repository.Status.Unstaged.Select(file => file.Path.Display));
        Assert.Equal(model.Resolution.Conflicts, model.FileStatusPage.Resolution.Conflicts);
        Assert.Equal("A merge is in progress. To change a file's resolution, open the merge view and choose Mark as unresolved.", model.Commit.UnstageTip);
        model.Resolution.ShowConflictCommand.Execute(model.Resolution.Conflicts[0]);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        Assert.Equal("app.csproj", model.Resolution.SelectedConflict!.PathText);
    }
}
