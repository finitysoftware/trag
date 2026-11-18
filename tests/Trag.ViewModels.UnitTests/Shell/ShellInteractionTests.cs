using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Shell;

// The cross-feature behaviour the features wire themselves: each test follows one flow through several features, and a
// test that visits several features in turn stays whole because its steps build on each other's state.
public sealed class ShellInteractionTests
{
    [Fact]
    public async Task WhenAWriteRuns_ShouldPauseWatchingUntilItIsVerifiedAndItsSnapshotApplied()
    {
        var h = new ShellHarness();
        h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        var source = h.Watcher.Latest;
        var watching = source.LastToken;
        var steps = new List<string>();
        h.Writer.Handle = (_, _) =>
        {
            steps.Add(watching.IsCancellationRequested ? "write with watcher paused" : "write with watcher running");
            h.Reader.Snapshot = h.Synced(Snapshots.Staged("a.txt"));
            return Task.FromResult(h.Writer.Result);
        };
        h.Reader.Read = (path, _) =>
        {
            steps.Add(source.LastToken.IsCancellationRequested ? "verify with watcher paused" : "verify with watcher running");
            return Task.FromResult(h.Reader.Snapshot with { Root = path });
        };
        model.Repository.SnapshotApplied += (_, _) =>
        {
            steps.Add(source.LastToken.IsCancellationRequested ? "apply with watcher paused" : "apply with watcher running");
            Assert.Single(model.FileStatus.StagedFiles);
            Assert.Empty(model.FileStatus.UnstagedFiles);
        };

        await model.Commit.StageSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["write with watcher paused", "verify with watcher paused", "apply with watcher paused"], steps);
        Assert.NotEqual(watching, source.LastToken);
        Assert.False(source.LastToken.IsCancellationRequested);
        Assert.False(model.Repository.IsWriting);
    }

    // One shell, four features held in turn: history, the working diff, sync and a write. Each step checks the same
    // thing for another feature (Cancel is offered while it runs, reaches its token, and is gone afterwards).
    [Fact]
    public async Task WhenHistoryTheDiffSyncAndAWriteRunInTurn_ShouldOfferCancelOnlyWhileOneRunsAndReachIt()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.False(model.CancelCommand.CanExecute(null));

        h.History.Gate.IsClosed = true;
        model.Navigator.ShowHistoryCommand.Execute(null); await h.History.Gate.Started;
        Assert.True(model.History.IsHistoryBusy); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(h.History.Gate.Token.IsCancellationRequested);
        h.History.Gate.Cancel(); await model.History.HistoryCompletion;
        Assert.False(model.History.IsHistoryBusy); Assert.Contains("canceled", model.History.Page.Message);

        model.Navigator.ShowFileStatusCommand.Execute(null);
        h.Diff.Gate.IsClosed = true;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await h.Diff.Gate.Started;
        Assert.True(model.WorkingDiff.Diff.IsBusy); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(h.Diff.Gate.Token.IsCancellationRequested);
        h.Diff.Gate.Cancel(); await model.WorkingDiff.Diff.Completion;
        Assert.False(model.WorkingDiff.Diff.IsBusy); Assert.Contains("interrupted", model.WorkingDiff.Diff.Document.Message);

        h.Sync.ReadGate.IsClosed = true;
        model.Repository.Remotes.Reread(); await h.Sync.ReadGate.Started;
        Assert.True(model.Sync.IsSyncBusy); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(h.Sync.ReadGate.Token.IsCancellationRequested);
        h.Sync.ReadGate.Cancel(); await model.Sync.SyncCompletion;
        Assert.False(model.Sync.IsSyncBusy); Assert.Contains("canceled", model.Sync.Panel.Message);

        h.Writer.Gate.IsClosed = true;
        var staging = model.Commit.StageSelectedCommand.ExecuteAsync(null); await h.Writer.Gate.Started;
        Assert.True(model.Repository.IsWriting); Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(h.Writer.Gate.Token.IsCancellationRequested); Assert.True(model.Repository.IsWriting); // cancel waits for the verified outcome
        h.Writer.Gate.Open(); await staging;
        Assert.False(model.Repository.IsWriting); Assert.False(model.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenAWriteRuns_ShouldDisableEveryCommandAndHoldSyncAndHistoryReadsUntilItEnds()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        var syncReads = h.Sync.Reads.Count; var historyReads = h.History.PageCalls;
        model.Navigator.ShowFileStatusCommand.Execute(null);
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
        model.Commit.CommitMessage = "draft";
        Assert.True(model.Commit.CommitCommand.CanExecute(null)); Assert.True(model.Sync.FetchCommand.CanExecute(null)); Assert.True(model.Stash.OpenStashCommand.CanExecute(null));
        h.Writer.Gate.IsClosed = true;
        var staging = model.Commit.StageSelectedCommand.ExecuteAsync(null); await h.Writer.Gate.Started;
        Assert.True(model.Repository.IsWriting); Assert.True(model.Repository.IsBusy);
        foreach (var command in new System.Windows.Input.ICommand[] { model.Sync.FetchCommand, model.Sync.PullCommand, model.Sync.PushCommand, model.Commit.CommitCommand, model.Commit.StageSelectedCommand,
            model.Commit.UnstageSelectedCommand, model.Discard.DiscardSelectedCommand, model.Branches.OpenCreateBranchCommand, model.Stash.OpenStashCommand, model.Publish.OpenPublishCommand,
            model.RefreshCommand, model.CloseRepositoryCommand, model.OpenCommand, model.PrepareCommitCommand, model.History.SearchHistoryCommand })
            Assert.False(command.CanExecute(null));
        Assert.False(model.Sync.CanChooseSyncTarget); Assert.False(model.Repository.CanWrite); Assert.Empty(model.WorkingDiff.Diff.Document.Lines);
        model.Navigator.ShowHistoryCommand.Execute(null); // the screen may change, but history does not load while a write runs
        Assert.False(model.History.IsHistoryBusy); Assert.Equal(historyReads, h.History.PageCalls);
        h.Writer.Gate.Open(); await staging;
        Assert.False(model.Repository.IsWriting);
        await model.Sync.SyncCompletion; await model.History.HistoryCompletion;
        Assert.Equal(syncReads + 1, h.Sync.Reads.Count); Assert.Equal(historyReads + 1, h.History.PageCalls);
        Assert.Equal("draft", model.Commit.CommitMessage); Assert.True(model.Sync.FetchCommand.CanExecute(null));
    }

    // Steps: file status with a diff, history, back to file status, repositories, back, search, history. Each move
    // checks what the previous section left behind, so the order is the test.
    [Fact]
    public async Task WhenTheSectionChanges_ShouldClearTheWorkingDiffAndRestartHistoryOrTheCommitPane()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
        Assert.NotEmpty(model.WorkingDiff.Diff.Document.Lines); Assert.Single(h.Diff.Requests);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Empty(model.WorkingDiff.Diff.Document.Lines); Assert.Equal(1, h.History.PageCalls); Assert.NotEmpty(model.History.Page.Commits);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        Assert.Equal(Snapshots.HeadId, model.History.Details.Panel.Heading);
        model.Navigator.ShowFileStatusCommand.Execute(null); await model.WorkingDiff.Diff.Completion;
        Assert.StartsWith("Select a commit", model.History.Details.Panel.Heading); Assert.NotNull(model.History.SelectedCommit); // details go, the row selection stays until history restarts
        Assert.NotEmpty(model.WorkingDiff.Diff.Document.Lines); Assert.Equal(2, h.Diff.Requests.Count); // the kept selection reloads its diff
        Assert.Equal("a.txt", model.FileStatus.SelectedStatus!.PathText);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        Assert.Empty(model.WorkingDiff.Diff.Document.Lines); Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section);
        model.Catalog.BackCommand.Execute(null); await model.WorkingDiff.Diff.Completion;
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.NotEmpty(model.WorkingDiff.Diff.Document.Lines);
        model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal(2, h.History.PageCalls); Assert.Equal("", h.History.LastQuery); Assert.Null(model.History.SelectedCommit);
        model.History.SearchQuery = "needle"; await model.History.HistoryCompletion;
        Assert.Equal(3, h.History.PageCalls); Assert.Equal("needle", h.History.LastQuery);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal("", h.History.LastQuery); Assert.Equal("needle", model.History.SearchQuery); // the query survives, history ignores it
    }

    // The repository list shows as the page, history, merge or rebase in the section column, the file status when neither.
    [Fact]
    public async Task WhenSectionsAreChosenAndACommitIsPrepared_ShouldShowTheMatchingPageOrSectionAndGoWhereTheCommitIsMade()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Null(model.CurrentPage); Assert.Null(model.CurrentSection);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Same(model.HistoryPage, model.CurrentSection);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        Assert.Same(model.Catalog, model.CurrentPage);
        model.Catalog.BackCommand.Execute(null);
        Assert.Null(model.CurrentPage); Assert.Null(model.CurrentSection);
        model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        model.PrepareCommitCommand.Execute(null);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.Null(model.CurrentSection);

        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge,
            merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
        await model.RefreshCommand.ExecuteAsync(null);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.PrepareCommitCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.Same(model.MergePage, model.CurrentSection);

        h.Reader.Snapshot = Snapshots.Rebasing(files: [Snapshots.Conflict("a.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.PrepareCommitCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section); Assert.Same(model.RebasePage, model.CurrentSection);
    }

    // That is how the user asks for it again: history reads its first page anew, and the repository list loads again.
    [Fact]
    public async Task WhenTheShownSectionIsChosenAgain_ShouldReadItAgain()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal(2, h.History.PageCalls);
        model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        model.Navigator.ShowSearchCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal(4, h.History.PageCalls);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var loads = h.Catalog.AccountsReads;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        Assert.Equal(loads + 1, h.Catalog.AccountsReads);
    }

    // Steps: the same snapshot again, a worktree edit, a moved head, a stash appearing and disappearing, remotes gone.
    // Each refresh asserts which readers ran on top of the counts the previous refresh left.
    [Fact]
    public async Task WhenASnapshotIsApplied_ShouldReloadOnlyTheFeaturesWhoseInputsChanged()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        Assert.Single(h.Sync.Reads); Assert.Equal(1, h.History.PageCalls); Assert.False(model.Stash.HasStashes); Assert.False(model.Publish.Context.ShowPublish);

        // History and the stash read what the snapshot already carries, so an unchanged snapshot is reason enough to
        // keep their results. Sync reads the remote's Git configuration, which is not in the snapshot at all, so an
        // explicit refresh has to ask for that again.
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.History.HistoryCompletion;
        Assert.Equal(2, h.Sync.Reads.Count); Assert.Equal(1, h.History.PageCalls); Assert.NotNull(model.History.SelectedCommit);

        h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.History.HistoryCompletion;
        Assert.Equal(3, h.Sync.Reads.Count); Assert.Equal(1, h.History.PageCalls); Assert.Single(model.FileStatus.UnstagedFiles); // worktree edits leave history alone

        // A moved head reads the target through the snapshot itself; the refresh must not make that a second read.
        h.Reader.Snapshot = h.Synced() with { Head = Snapshots.Id('b') };
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; await model.History.HistoryCompletion;
        Assert.Equal(4, h.Sync.Reads.Count); Assert.Equal(2, h.History.PageCalls); Assert.Null(model.History.SelectedCommit); Assert.Equal(Snapshots.Id('b'), h.History.LastHead);

        var stash = h.Stashes.Add("On main: spike");
        h.Reader.Snapshot = h.Synced() with { Head = Snapshots.Id('b'), StashHead = stash.Id };
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion;
        Assert.True(model.Stash.HasStashes); Assert.Single(model.Stash.Rows); Assert.Equal(1, h.Stashes.Calls);
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion;
        Assert.Equal(1, h.Stashes.Calls); // same stash head, no reload
        h.Reader.Snapshot = h.Synced() with { Head = Snapshots.Id('b') };
        await model.RefreshCommand.ExecuteAsync(null); await model.Stash.List.Completion;
        Assert.False(model.Stash.HasStashes); Assert.Empty(model.Stash.Rows);

        h.Reader.Snapshot = Snapshots.Create(head: Snapshots.Id('b'));
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.True(model.Publish.Context.ShowPublish); Assert.False(model.Publish.Context.HasRemotes); Assert.Null(model.Sync.Selection.Remote);
    }

    [Fact]
    public async Task WhenAMergeIsInProgressAndEnds_ShouldOpenTheMergeViewKeepItsDraftAndLeaveItOnlyFromThere()
    {
        var merge = new RepositoryMerge("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", "");
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("shared.txt"), Snapshots.Staged("done.txt")], operation: RepositoryOperation.Merge, merge: merge);
        await model.RefreshCommand.ExecuteAsync(null); await model.WorkingDiff.Diff.Completion;
        Assert.True(model.Repository.HasMerge); Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        // The conflict shows its blocks, not a diff; the file Git merged on its own is in the resolved list.
        Assert.Single(model.Resolution.Conflicts); Assert.NotNull(model.Resolution.SelectedConflict); Assert.Null(model.FileStatus.SelectedStatus);
        await model.ConflictsPage.Conflict.Document.Completion; Assert.Equal("shared.txt", h.Diff.ConflictReads[^1].Path.Display);
        Assert.Equal("done.txt", Assert.Single(model.Resolution.Resolved).PathText);
        Assert.Equal("Merge branch 'topic'", model.Merge.Message); Assert.True(model.Repository.HasMerge); Assert.False(model.Commit.CommitCommand.CanExecute(null));
        model.Merge.Message = "Reviewed";
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal(WorkspaceSection.History, model.Navigator.Section); Assert.True(model.Repository.HasMerge);
        await model.RefreshCommand.ExecuteAsync(null); // same merge: the draft and the screen are kept
        Assert.Equal("Reviewed", model.Merge.Message); Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasMerge); Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
        model.Navigator.ShowMergeCommand.Execute(null); Assert.Equal(WorkspaceSection.History, model.Navigator.Section); // no merge, nothing to show
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("shared.txt")], operation: RepositoryOperation.Merge, merge: merge);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.Null(model.FileStatus.SelectedStatus);
    }

    [Fact]
    public async Task WhenTheDiscardQuestionIsAnsweredWithStash_ShouldOpenTheStashDraftAndWriteNothing()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.Discard.DiscardSelectedCommand.Execute(null);
        Assert.True(model.Discard.Confirmation.IsOpen);
        model.Discard.StashInsteadOfDiscardCommand.Execute(null);
        Assert.False(model.Discard.Confirmation.IsOpen); Assert.True(model.Stash.Draft.IsOpen); Assert.Empty(h.Writer.Requests);
    }

    // Every feature is given state first, then one Close has to take all of it away.
    [Fact]
    public async Task WhenTheRepositoryCloses_ShouldResetEveryFeatureAndShowTheRepositoryList()
    {
        var h = new ShellHarness(); var stash = h.Stashes.Add("On main: spike"); h.Branches.Local.Add("topic");
        h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt")) with { StashHead = stash.Id };
        h.Catalog.Accounts.Add(FakeAccountService.GitHub());
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; await model.Stash.List.Completion;
        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0]; await model.WorkingDiff.Diff.Completion;
        model.Commit.CommitMessage = "draft";
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.History.SearchQuery = "needle";
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); await model.Branches.List.Completion;
        Assert.True(model.Branches.Draft.IsOpen); Assert.NotEmpty(model.Branches.Rows.Local);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.True(model.Publish.Draft.IsOpen);
        await model.CloseRepositoryCommand.ExecuteAsync(null); await model.Catalog.CatalogCompletion;
        Assert.False(model.Repository.HasRepository); Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section);
        Assert.Empty(model.FileStatus.StagedFiles); Assert.Empty(model.FileStatus.UnstagedFiles); Assert.Null(model.FileStatus.SelectedStatus); Assert.Empty(model.WorkingDiff.Diff.Document.Lines);
        Assert.Equal("", model.Commit.CommitMessage); Assert.Equal("", model.History.SearchQuery); Assert.Empty(model.History.Page.Commits); Assert.Null(model.History.SelectedCommit);
        Assert.StartsWith("Select a commit", model.History.Details.Panel.Heading); Assert.Empty(model.History.Details.Panel.Files);
        Assert.False(model.Branches.Draft.IsOpen); Assert.Empty(model.Branches.Rows.Local); Assert.Null(model.Branches.Rows.Current);
        Assert.False(model.Publish.Draft.IsOpen); Assert.Empty(model.Publish.Accounts.All.Value);
        Assert.False(model.Stash.HasStashes); Assert.Empty(model.Stash.Rows); Assert.False(model.Stash.Draft.IsOpen);
        Assert.Empty(model.Sync.Selection.RemoteNames); Assert.Null(model.Sync.Selection.Remote);
        Assert.False(model.Repository.HasMerge); Assert.Equal("No repository open", model.Header.RepositoryName); Assert.Equal("—", model.Header.BranchText);
    }

    [Fact]
    public async Task WhenTheShellIsDisposedWithAReadAndALayoutSavePending_ShouldCancelTheReadAndFlushTheSave()
    {
        var h = new ShellHarness();
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        h.History.Gate.IsClosed = true;
        model.Navigator.ShowHistoryCommand.Execute(null); await h.History.Gate.Started;
        model.Layout.DiffMode = 1;
        Assert.Equal(0, h.Preferences.Saves);
        await model.DisposeAsync();
        Assert.True(h.History.Gate.Token.IsCancellationRequested);
        Assert.Equal(1, h.Preferences.Saves); Assert.Equal(1, h.Preferences.Saved!.Mode);
        h.History.Gate.Cancel();
    }

    // A write pauses the watcher; one that ends after the application stopped belongs to no session.
    [Fact]
    public async Task WhenAWriteEndsAfterTheApplicationStopped_ShouldLeaveTheWatcherStopped()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("Watching for changes", model.Session.Watcher.Status);
        model.FileStatus.UnstagedSelection.Add((StatusRow)model.FileStatus.UnstagedFiles[0]);
        h.Writer.Gate.IsClosed = true;
        var pending = model.Commit.StageSelectedCommand.ExecuteAsync(null);
        await h.Writer.Gate.Started;
        Assert.Equal("", model.Session.Watcher.Status);
        model.Workbench.Stop();
        h.Writer.Gate.Open();
        await pending;
        Assert.False(model.Repository.IsWriting); Assert.Equal("", model.Session.Watcher.Status);
    }

    [Fact]
    public async Task WhenALiveUpdateChangesAnotherFile_ShouldEditTheListsInPlaceAndKeepTheSelectionAndItsDiff()
    {
        var h = new ShellHarness(); h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell(watch: true);
        await model.Session.OpenRepositoryAsync("fixture");
        var list = model.FileStatus.UnstagedFiles;
        model.FileStatus.SelectedUnstaged = (StatusRow)list[0]; await model.WorkingDiff.Diff.Completion;
        Assert.Single(h.Diff.Requests); Assert.Equal("Watching for changes", model.Session.Watcher.Status);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt")]);
        h.Watcher.Latest.Push("b.txt");
        // The status line is written last, once the lists are edited and the snapshot is published.
        await ShellHarness.Until(() => model.StatusBar.Status == "0 staged · 2 unstaged", "the live update");
        await model.WorkingDiff.Diff.Completion;
        Assert.Same(list, model.FileStatus.UnstagedFiles);
        Assert.Equal("a.txt", model.FileStatus.SelectedStatus!.PathText); Assert.Single(h.Diff.Requests);
        Assert.Equal("0 staged · 2 unstaged", model.StatusBar.Status);
    }

    // The commit pane follows the repository: shown while one is open and no merge is in progress. The view binds
    // Repository.HasRepository and Repository.HasMerge: the shell announces another repository, the repository its facts.
    [Fact]
    public async Task WhenARepositoryOpensAndTheListShows_ShouldShowTheWorkspaceAndTheRepositoryActionsOnlyOverTheRepository()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        var announced = new List<string?>();
        model.PropertyChanged += (_, e) => announced.Add(e.PropertyName);
        Assert.Equal((true, false), (model.ShowsWorkspace, model.ShowsRepositoryActions));

        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal((true, true), (model.ShowsWorkspace, model.ShowsRepositoryActions));
        Assert.Contains(nameof(model.ShowsRepositoryActions), announced);

        announced.Clear();
        model.Navigator.ShowRepositories();
        Assert.Equal((false, false), (model.ShowsWorkspace, model.ShowsRepositoryActions));
        Assert.Contains(nameof(model.ShowsWorkspace), announced); Assert.Contains(nameof(model.ShowsRepositoryActions), announced);
    }

    // A dialog kept back by uncommitted changes offers the stash dialog itself.
    [Fact]
    public async Task WhenTheWorkspaceIsComposed_ShouldGiveTheBlockedDialogsTheStashDialogsCommand()
    {
        await using var model = new ShellHarness().Shell();
        Assert.Same(model.Stash.OpenStashCommand, model.GoTo.OpenStash);
        Assert.Same(model.Stash.OpenStashCommand, model.MergeDialog.Merge.OpenStash);
        Assert.Same(model.Stash.OpenStashCommand, model.MergeDialog.Rebase.OpenStash);
    }

    [Fact]
    public async Task WhenARepositoryOpensMergesAndCloses_ShouldAnnounceTheFactsTheCommitPaneFollows()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        var announced = new List<string>();
        void Follow(Repository repository) => repository.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Repository.HasRepository) or nameof(Repository.HasMerge)) announced.Add(e.PropertyName);
        };
        Follow(model.Repository);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not ("" or nameof(model.Repository))) return;
            announced.Add("Repository");
            Follow(model.Repository);
        };
        Assert.False(model.Repository.HasRepository);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.True(model.Repository.HasRepository); Assert.False(model.Repository.HasMerge);
        Assert.Equal(["Repository", "HasRepository"], announced);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge, merge: new("incoming", Snapshots.HeadId, "Merge", "fp", ""));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Repository.HasMerge); Assert.Equal("HasMerge", announced[^1]); Assert.Equal(3, announced.Count);
        h.Reader.Snapshot = Snapshots.Create();
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasMerge); Assert.Equal("HasMerge", announced[^1]); Assert.Equal(4, announced.Count);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.False(model.Repository.HasRepository); Assert.Equal("Repository", announced[^1]); Assert.Equal(5, announced.Count);
    }
}
