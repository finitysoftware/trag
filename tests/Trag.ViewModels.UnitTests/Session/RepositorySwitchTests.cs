using System.Runtime.CompilerServices;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Session;

// Opening one repository after another: the first one leaves when the second begins to open, and every feature starts on
// the second one as if nothing had been shown before. Each opened repository has features of its own, and nothing of
// the application keeps the ones that went.
public sealed class RepositorySwitchTests
{
    private static readonly string Second = Snapshots.Id('b');

    // The application's parts (navigator, automatic fetch, messenger, Cancel, requery, the shell) all let go of it,
    // its writer and its view models.
    [Fact]
    public async Task WhenAnotherRepositoryIsShown_ShouldLetTheOneThatWentBeCollectedWithItsFeatures()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update", Second), Snapshots.Commit(Second, "Initial")], false, page);
        await using var model = h.Shell();
        var first = await OpenAndUseAsync(model, "first");

        await model.Session.OpenRepositoryAsync("second"); await model.Sync.SyncCompletion;
        for (var attempt = 0; attempt < 3 && first.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            await Task.Yield();
        }

        Assert.All(first, reference => Assert.False(reference.IsAlive, reference.Target?.GetType().Name));
    }

    // Opens a repository, uses its features the way a user would, and hands back only weak references to them.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference[]> OpenAndUseAsync(ShellViewModel model, string path)
    {
        await model.Session.OpenRepositoryAsync(path); await model.Sync.SyncCompletion;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        model.Navigator.ShowFileStatusCommand.Execute(null);
        return [new(model.Repository), new(model.Repository.Writer), new(model.FileStatus), new(model.History), new(model.Sync), new(model.Branches), new(model.FileStatusPage)];
    }

    [Fact]
    public async Task WhenAnotherRepositoryOpens_ShouldStartEveryFeatureFreshOnIt()
    {
        var h = new ShellHarness(); var stash = h.Stashes.Add("On main: spike"); h.Branches.Remote.Add("origin/topic");
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update", Second), Snapshots.Commit(Second, "Initial")], false, page);
        h.Reader.Read = (path, _) => Task.FromResult(path == "first"
            ? h.Synced(Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt")) with { Root = "first", StashHead = stash.Id }
            : Snapshots.Create(root: "second", files: [Snapshots.Modified("c.txt")]));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("first"); await model.Sync.SyncCompletion; await model.Stash.List.Completion;

        // The file status: a selection, a multi-selection, the discard question and a commit draft.
        var row = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.FileStatus.SelectedUnstaged = row; model.FileStatus.UnstagedSelection.Add(row); await model.WorkingDiff.Diff.Completion;
        model.Discard.DiscardSelectedCommand.Execute(null);
        model.Commit.CommitMessage = "draft";
        // The branch picker's filter, a remote branch's delete question and the create dialog.
        await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
        model.Branches.BranchSearch = "top";
        await model.Branches.RequestDeleteCommand.ExecuteAsync(model.Branches.Rows.Remote.Single());
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        // History with a search typed, a commit chosen and the go-to dialog for it.
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SearchQuery = "needle";
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        // The merge and publish dialogs, then the stash: its draft, its preview in the commit pane and its drop question.
        await model.MergeDialog.OpenCommand.ExecuteAsync(null);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Stash.OpenStashCommand.Execute(null); model.Stash.Draft.Message = "spike 2";
        var entry = model.Stash.Rows.Single();
        await model.Stash.PreviewCommand.ExecuteAsync(entry);
        model.Stash.RequestDropCommand.Execute(entry);
        Assert.True(model.Discard.Confirmation.IsOpen); Assert.True(model.Branches.Delete.IsOpen); Assert.True(model.Branches.Draft.IsOpen);
        Assert.True(model.GoTo.Target.IsOpen); Assert.True(model.MergeDialog.IsOpen); Assert.True(model.Publish.Draft.IsOpen);
        Assert.True(model.Stash.Draft.IsOpen); Assert.NotNull(model.Stash.SelectedStash); Assert.True(model.Stash.Drop.IsOpen);
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name);

        await model.Session.OpenRepositoryAsync("second"); await model.Sync.SyncCompletion; await model.Stash.List.Completion; await model.Branches.List.Completion;

        Assert.Equal("second", model.Repository.Snapshot!.Root); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        Assert.Empty(model.FileStatus.StagedFiles); Assert.Single(model.FileStatus.UnstagedFiles); Assert.Equal("c.txt", ((StatusRow)model.FileStatus.UnstagedFiles[0]).PathText);
        Assert.Null(model.FileStatus.SelectedStatus); Assert.Empty(model.FileStatus.UnstagedSelection); Assert.Empty(model.WorkingDiff.Diff.Document.Lines);
        Assert.False(model.Discard.Confirmation.IsOpen); Assert.Equal("", model.Commit.CommitMessage);
        Assert.Equal("", model.Branches.BranchSearch); Assert.False(model.Branches.Delete.IsOpen); Assert.False(model.Branches.Draft.IsOpen);
        Assert.Equal("second", h.Branches.LastRoot); Assert.Equal("main", model.Branches.Rows.Current!.Name); Assert.Empty(model.Branches.Rows.Local);
        Assert.Equal("", model.History.SearchQuery); Assert.Null(model.History.SelectedCommit); Assert.Empty(model.History.Page.Commits);
        Assert.StartsWith("Select a commit", model.History.Details.Panel.Heading);
        Assert.False(model.GoTo.Target.IsOpen); Assert.False(model.GoTo.Keep.IsOpen);
        Assert.False(model.MergeDialog.IsOpen); Assert.False(model.MergeDialog.Merge.Draft.IsOpen); Assert.False(model.Publish.Draft.IsOpen);
        Assert.False(model.Stash.HasStashes); Assert.Empty(model.Stash.Rows); Assert.False(model.Stash.Draft.IsOpen); Assert.Equal("", model.Stash.Draft.Message);
        Assert.Null(model.Stash.SelectedStash); Assert.False(model.Stash.Drop.IsOpen);
        Assert.Null(model.Sync.Selection.Remote); Assert.Empty(model.Sync.Selection.RemoteNames);
        Assert.False(model.StatusBar.HasError);
    }

    [Fact]
    public async Task WhenAnotherRepositoryOpens_ShouldLeaveTheMergeOfTheFirstOneBehind()
    {
        var h = new ShellHarness();
        var merge = new RepositoryMerge("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", "");
        h.Reader.Read = (path, _) => Task.FromResult(path == "first"
            ? Snapshots.Create(root: "first", files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: merge)
            : Snapshots.Create(root: "second"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("first");
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.NotNull(model.Resolution.SelectedConflict);
        model.Merge.Message = "Reviewed"; model.Merge.RequestAbortMergeCommand.Execute(null);

        await model.Session.OpenRepositoryAsync("second");

        Assert.False(model.Repository.HasMerge); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        Assert.Equal("", model.Merge.Message); Assert.False(model.Merge.Abort.IsOpen);
        Assert.Empty(model.Resolution.Conflicts); Assert.Null(model.Resolution.SelectedConflict); Assert.Empty(model.ConflictsPage.Conflict.Parts);
    }

    [Fact]
    public async Task WhenAnotherRepositoryOpensAndFails_ShouldStopShowingTheFirstAtOnceAndLeaveNothingShown()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("first");
        Assert.Single(model.FileStatus.UnstagedFiles);

        h.Reader.Gate.IsClosed = true;
        var opening = model.Session.OpenRepositoryAsync("second");
        await h.Reader.Gate.Arrival(2);
        // The first repository left when the second began to open: nothing of it is shown, and nothing can be written.
        Assert.False(model.Repository.HasRepository); Assert.True(model.Repository.IsReading); Assert.False(model.Repository.CanWrite);
        Assert.Empty(model.FileStatus.UnstagedFiles); Assert.Equal("No repository open", model.Header.RepositoryName);
        Assert.Equal("Opening repository…", model.StatusBar.Status);

        h.Reader.Failure = new IOException("unreadable");
        h.Reader.Gate.Open();
        await opening;
        Assert.False(model.Repository.HasRepository); Assert.False(model.Repository.IsBusy);
        Assert.Empty(model.FileStatus.UnstagedFiles); Assert.Equal("No repository open", model.Header.RepositoryName);
        Assert.Equal("Repository could not be opened", model.StatusBar.Status); Assert.True(model.StatusBar.HasError);
        Assert.True(model.RefreshCommand.CanExecute(null));
    }
}
