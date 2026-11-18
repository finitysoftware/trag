using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Stash;

public sealed class StashViewModelTests
{
    [Fact]
    public async Task WhenTheRepositoryRefreshesUnderAnOpenStashDraft_ShouldKeepTheDraft()
    {
        var h = Harness(); var stashes = h.Stashes;
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);
        stashes.Add("WIP on main: saved");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Stash.OpenStashCommand.Execute(null);
        model.Stash.Draft.Message = "keep this draft";
        model.Stash.Draft.IncludeUntracked = true;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("keep this draft", model.Stash.Draft.Message);
        Assert.True(model.Stash.Draft.IncludeUntracked);
        Assert.True(model.Stash.Draft.IsOpen);
    }

    // The close follows a stash draft kept open across a refresh, so it has a draft to drop as well.
    [Fact]
    public async Task WhenTheRepositoryClosesWhileTheStashListLoads_ShouldRejectTheLateListAndCloseTheDraft()
    {
        var h = Harness(); var stashes = h.Stashes;
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);
        stashes.Add("WIP on main: saved");
        await using var model = h.Shell();
        await OpenADraftThenRefreshAsync(model);

        stashes.Gate.IsClosed = true;
        model.Stash.OpenStashPickerCommand.Execute(null);
        var pending = model.Stash.List.Completion;
        await stashes.Gate.Started;
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        stashes.Gate.Open();
        await pending;
        Assert.Empty(model.Stash.Rows);
        Assert.False(model.Stash.List.IsBusy);
        Assert.False(model.Stash.Draft.IsOpen);
        Assert.Empty(model.Stash.Draft.Message);
    }
    private static readonly string HeadId = Snapshots.HeadId;

    // Opens, words a stash draft that includes untracked files and refreshes under it.
    private static async Task OpenADraftThenRefreshAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        model.Stash.OpenStashCommand.Execute(null);
        model.Stash.Draft.Message = "keep this draft";
        model.Stash.Draft.IncludeUntracked = true;
        await model.RefreshCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task WhenTheTreeHasChangesToStash_ShouldOpenTheDialogAndStashWithTheMessageAndUntrackedFlag()
    {
        var h = Harness(); var reader = h.Reader; var stashes = h.Stashes; var writer = h.Writer;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.False(model.Stash.OpenStashCommand.CanExecute(null)); Assert.False(model.Stash.HasStashes); Assert.Equal("Stashes", model.Stash.StashButtonText);
        reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] }; await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.Stash.OpenStashCommand.CanExecute(null));
        model.Stash.OpenStashCommand.Execute(null);
        Assert.True(model.Stash.Draft.IsOpen); Assert.Contains("WIP on main", model.Stash.StashDefaultHint); Assert.True(model.Stash.CreateStashCommand.CanExecute(null));
        model.Stash.Draft.Message = "-looks like an option"; Assert.False(model.Stash.CreateStashCommand.CanExecute(null));
        model.Stash.Draft.Message = "half-done login form"; model.Stash.Draft.IncludeUntracked = true;
        writer.Handle = null; writer.Result = new(GitWriteOutcome.Rejected, "The Git index is locked by another operation.");
        await model.Stash.CreateStashCommand.ExecuteAsync(null);
        Assert.True(model.Stash.Draft.IsOpen); Assert.Equal("The Git index is locked by another operation.", model.Stash.Draft.Notice);
        writer.Handle = StashWrites(reader, stashes);
        await model.Stash.CreateStashCommand.ExecuteAsync(null);
        var request = writer.Requests.OfType<StashRequest>().Last(item => item.Action == StashAction.Push);
        Assert.Equal("half-done login form", request.Stash.Message); Assert.True(request.Stash.IncludeUntracked);
        Assert.False(model.Stash.Draft.IsOpen); Assert.Equal("", model.Stash.Draft.Notice); Assert.Contains("Stashed", model.StatusBar.Status);
        Assert.True(model.Stash.HasStashes); await model.Stash.List.Completion;
        var row = Assert.Single(model.Stash.Rows);
        Assert.Equal("half-done login form", row.Title); Assert.StartsWith("main · ", row.Detail); Assert.Equal("half-done login form", model.Stash.StashButtonText); Assert.Equal("", model.Stash.StashCountText);
        Assert.False(model.Stash.OpenStashCommand.CanExecute(null)); // the tree is clean again
    }

    [Fact]
    public async Task WhenAStashIsAppliedThenDropped_ShouldBringTheChangesBackAndAskBeforeRemovingTheEntry()
    {
        var h = Harness(); var reader = h.Reader; var stashes = h.Stashes; var writer = h.Writer;
        stashes.Add("On main: half-done login form"); reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        Assert.True(model.Stash.HasStashes); var row = Assert.Single(model.Stash.Rows);
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.Stash.ApplyCommand.ExecuteAsync(row);
        var apply = Assert.IsType<StashRequest>(Assert.Single(writer.Requests)); Assert.Equal(StashAction.Apply, apply.Action); Assert.Equal(row.Id, apply.Stash.Id);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.Single(model.FileStatus.UnstagedFiles); Assert.True(model.Stash.HasStashes);
        model.Stash.RequestDropCommand.Execute(row);
        Assert.True(model.Stash.Drop.IsOpen); Assert.Contains("stash@{0}", model.Stash.Drop.Text); Assert.True(model.Stash.Drop.ConfirmCommand.CanExecute(null));
        model.Stash.Drop.CancelCommand.Execute(null); Assert.False(model.Stash.Drop.IsOpen); Assert.Single(writer.Requests);
        model.Stash.RequestDropCommand.Execute(row); await model.Stash.Drop.ConfirmCommand.ExecuteAsync(null);
        var drop = Assert.IsType<StashRequest>(writer.Requests[^1]); Assert.Equal(StashAction.Drop, drop.Action); Assert.Equal(row.Id, drop.Stash.Id);
        Assert.False(model.Stash.Drop.IsOpen); Assert.False(model.Stash.HasStashes); await model.Stash.List.Completion; Assert.Empty(model.Stash.Rows);
    }

    // A dropped entry that was being previewed leaves the pane at once, before the list is read again.
    [Fact]
    public async Task WhenThePreviewedStashIsDropped_ShouldLeaveThePaneBeforeTheListIsReadAgain()
    {
        var h = Harness(); var reader = h.Reader; var stashes = h.Stashes;
        stashes.Add("On main: older spike"); stashes.Add("WIP on main: aaaaaaa Update image"); reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        var row = model.Stash.Rows[0];
        await model.Stash.PreviewCommand.ExecuteAsync(row);

        stashes.Gate.IsClosed = true;
        model.Stash.RequestDropCommand.Execute(row);
        await model.Stash.Drop.ConfirmCommand.ExecuteAsync(null);
        Assert.False(model.Stash.IsStashPreview);

        stashes.Gate.Open(); await model.Stash.List.Completion;
    }

    [Fact]
    public async Task WhenAStashIsPreviewed_ShouldShowItThroughTheCommitPaneUntilACommitIsSelectedOrItIsDropped()
    {
        var h = Harness(); var reader = h.Reader; var stashes = h.Stashes;
        stashes.Add("On main: older spike"); stashes.Add("WIP on main: aaaaaaa Update image"); reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion;
        Assert.Equal(2, model.Stash.Rows.Count); Assert.Equal("2", model.Stash.StashCountText); Assert.Equal("aaaaaaa Update image", model.Stash.StashButtonText);
        var row = model.Stash.Rows[0];
        await model.Stash.PreviewCommand.ExecuteAsync(row);
        Assert.Equal(WorkspaceSection.History, model.Navigator.Section); Assert.True(model.Stash.IsStashPreview); Assert.True(model.History.Details.HasSubject); Assert.Null(model.History.SelectedCommit);
        Assert.Equal("Stash stash@{0} · aaaaaaa Update image", model.Stash.StashPreviewTitle);
        Assert.Equal(row.Id, model.History.Details.Panel.Heading); Assert.Single(model.History.Details.Panel.Files); Assert.True(model.Stash.ApplyCommand.CanExecute(model.Stash.SelectedStash));
        await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        Assert.False(model.Stash.IsStashPreview); Assert.Null(model.Stash.SelectedStash); Assert.Equal(HeadId, model.History.Details.Panel.Heading);
        await model.Stash.PreviewCommand.ExecuteAsync(row); Assert.True(model.Stash.IsStashPreview);
        model.Stash.RequestDropCommand.Execute(model.Stash.SelectedStash); Assert.True(model.Stash.Drop.IsOpen);
        await model.Stash.Drop.ConfirmCommand.ExecuteAsync(null);
        Assert.False(model.Stash.IsStashPreview); Assert.False(model.History.Details.HasSubject);
    }

    // One commit with one file in it, enough for the pane the stash preview borrows, and a writer that keeps the
    // stash list and the status reader in step.
    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(HeadId, "Update image")], false, page);
        h.History.Details = id => new(Snapshots.Commit(id, "Update image") with { Parents = [HeadId] }, "Body",
            [new FileDiffMetadata(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'))]);
        h.History.Diff = (_, _, _) => new FileDiff([], [], DiffContentKind.Text, "");
        h.Writer.Handle = StashWrites(h.Reader, h.Stashes);
        return h;
    }

    // Git's own bookkeeping: a push moves the working tree into a new entry, an apply brings a change back, and a
    // drop removes exactly the chosen entry. The status reader has to agree with the stash list after each one.
    private static Func<GitWriteRequest, CancellationToken, Task<GitWriteResult>> StashWrites(FakeRepositoryReader reader, FakeStashReader stashes) => (request, _) =>
    {
        var stash = Assert.IsType<StashRequest>(request);
        switch (stash.Action)
        {
            case StashAction.Push:
                stashes.Add(string.IsNullOrEmpty(stash.Stash.Message) ? "WIP on main: aaaaaaa Update image" : "On main: " + stash.Stash.Message);
                reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head, Files = [] };
                return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Stashed the changes as stash@{0}."));
            case StashAction.Apply:
                reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
                return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Applied stash@{0}."));
            case StashAction.Drop:
                stashes.Remove(stash.Stash.Id!);
                reader.Snapshot = reader.Snapshot with { StashHead = stashes.Head };
                return Task.FromResult(new GitWriteResult(GitWriteOutcome.Succeeded, "Dropped the stash."));
            default: return Task.FromResult(new GitWriteResult(GitWriteOutcome.Rejected, "Unexpected " + stash.Action));
        }
    };
}
