using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Merge;

public sealed class MergeViewModelTests
{
    [Fact]
    public async Task WhenAMergeIsPending_ShouldWaitForAnExplicitResolutionAndAbortOnlyAfterConfirmation()
    {
        var backend = Backend(); await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.Single(model.Resolution.Conflicts); Assert.False(model.Commit.CommitCommand.CanExecute(null));
        Assert.False(model.Merge.FinishMergeCommand.CanExecute(null)); Assert.True(model.Resolution.StageResolutionCommand.CanExecute(null));
        Assert.False(model.Merge.Abort.ConfirmCommand.CanExecute(null));
        await model.Merge.Abort.ConfirmCommand.ExecuteAsync(null); Assert.Null(backend.LastRequest);
        model.Merge.RequestAbortMergeCommand.Execute(null); Assert.True(model.Merge.Abort.IsOpen); Assert.Null(backend.LastRequest);
        model.Merge.Abort.CancelCommand.Execute(null); Assert.False(model.Merge.Abort.IsOpen); Assert.Null(backend.LastRequest);
    }

    // Git may refuse the abort: the question stays while Git runs and then says why, with the merge still there to keep.
    [Fact]
    public async Task WhenTheAbortRunsOrGitRefusesIt_ShouldKeepTheQuestionOpenAndShowGitsReasonInIt()
    {
        var backend = Backend(); await using var model = ShellHarness.Compose(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Merge.RequestAbortMergeCommand.Execute(null);
        Assert.StartsWith("Git will try to restore the pre-merge state.", model.Merge.Abort.Text);

        backend.Gate.IsClosed = true;
        var pending = model.Merge.Abort.ConfirmCommand.ExecuteAsync(null); await backend.Gate.Started;
        Assert.True(model.Merge.Abort.IsOpen); Assert.True(model.Merge.Abort.IsRunning);
        backend.Gate.Open(); await pending;

        Assert.IsType<MergeAbortRequest>(backend.LastRequest);
        Assert.True(model.Merge.Abort.IsOpen); Assert.Equal("Hook rejected merge", model.Merge.Abort.Message);
    }

    // The finish follows an abort that was asked for and then kept.
    [Fact]
    public async Task WhenFinishingTheMergeFails_ShouldKeepTheDraft()
    {
        var backend = Backend(); await using var model = ShellHarness.Compose(backend);
        await OpenThenKeepTheMergeAsync(model);
        model.Merge.Message = "Keep my merge draft";
        backend.Snapshot = backend.Snapshot with { Files = [] };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("Keep my merge draft", model.Merge.Message); Assert.True(model.Merge.FinishMergeCommand.CanExecute(null));
        await model.Merge.FinishMergeCommand.ExecuteAsync(null);
        var finish = Assert.IsType<CommitRequest>(backend.LastRequest); Assert.True(finish.FinishesMerge); Assert.Equal("Keep my merge draft", finish.Message);
        Assert.Equal("Keep my merge draft", model.Merge.Message); Assert.True(model.Repository.HasMerge);
    }

    [Fact]
    public async Task WhenTheScreenChangesDuringAMergeWrite_ShouldKeepItRunningAndLetCancelWaitForTheVerifiedOutcome()
    {
        var backend = Backend(); backend.Gate.IsClosed = true;
        await using var model = ShellHarness.Compose(backend); await model.Session.OpenRepositoryAsync("fixture");
        var pending = model.Resolution.OpenConflictEditorCommand.ExecuteAsync(null); await backend.Gate.Started;
        model.Navigator.ShowHistoryCommand.Execute(null); Assert.False(backend.Gate.Token.IsCancellationRequested); Assert.True(model.Repository.IsWriting);
        Assert.False(model.CloseRepositoryCommand.CanExecute(null)); Assert.False(model.Resolution.StageResolutionCommand.CanExecute(null));
        model.CancelCommand.Execute(null); Assert.True(backend.Gate.Token.IsCancellationRequested); Assert.True(model.Repository.IsWriting);
        backend.Result = new(GitWriteOutcome.Cancelled, "Edits kept"); backend.Gate.Open(); await pending;
        Assert.False(model.Repository.IsWriting); Assert.True(model.Repository.HasMerge); Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
        model.Navigator.ShowMergeCommand.Execute(null); Assert.True(model.Resolution.StageResolutionCommand.CanExecute(null));
    }

    // The editor opens the file with its conflict markers; the merge tool is the one Git is configured with.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenAToolIsOpenedForTheSelectedConflict_ShouldAskGitForThatTool(bool editor)
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend); await model.Session.OpenRepositoryAsync("fixture");
        await (editor ? model.Resolution.OpenConflictEditorCommand : model.Resolution.RunMergeToolCommand).ExecuteAsync(null);
        Assert.Equal(editor, Assert.IsType<MergeToolRequest>(backend.LastRequest).Editor);
    }

    // Git's merge tool asks on its input which side to keep for a file one side deleted or never had, or for a submodule;
    // with no terminal to answer it fails and leaves its temporary files in the working tree. The editor still opens.
    [Theory]
    [InlineData('D', 'U', false)]
    [InlineData('U', 'D', false)]
    [InlineData('A', 'U', false)]
    [InlineData('U', 'A', false)]
    [InlineData('U', 'U', true)]
    public async Task WhenTheConflictIsNotAFileBothSidesChanged_ShouldNotOfferTheMergeTool(char index, char worktree, bool submodule)
    {
        var backend = Backend();
        backend.Snapshot = backend.Snapshot with { Files = [new FileStatus(Snapshots.Path("conflict.txt"), index, worktree, IsConflict: true, IsSubmodule: submodule)] };
        await using var model = ShellHarness.Compose(backend); await model.Session.OpenRepositoryAsync("fixture");

        Assert.False(model.Resolution.RunMergeToolCommand.CanExecute(null));
        Assert.True(model.Resolution.OpenConflictEditorCommand.CanExecute(null));
    }

    // A path that is not its own UTF-8 cannot be named to Git as text nor handed to a tool, so only Git's tools resolve it.
    [Fact]
    public async Task WhenTheConflictPathIsNotUtf8_ShouldOfferNoSideAndNoTool()
    {
        var backend = Backend();
        backend.Snapshot = backend.Snapshot with { Files = [new FileStatus(new([(byte)'c', (byte)'a', (byte)'f', 0xE9]), 'U', 'U', IsConflict: true)] };
        await using var model = ShellHarness.Compose(backend); await model.Session.OpenRepositoryAsync("fixture");

        Assert.Single(model.Resolution.Conflicts);
        Assert.False(model.Resolution.UseYoursCommand.CanExecute(null)); Assert.False(model.Resolution.UseOtherCommand.CanExecute(null));
        Assert.False(model.Resolution.OpenConflictEditorCommand.CanExecute(null)); Assert.False(model.Resolution.RunMergeToolCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(RepositoryOperation.Rebase)]
    [InlineData(RepositoryOperation.CherryPick)]
    [InlineData(RepositoryOperation.Sequencer)]
    public async Task WhenAnotherOperationIsInProgress_ShouldBlockAllMergeCommandsWithAnExplanation(RepositoryOperation operation)
    {
        var backend = Backend(); backend.Snapshot = backend.Snapshot with { Operation = operation, Merge = null };
        await using var model = ShellHarness.Compose(backend); await model.Session.OpenRepositoryAsync("fixture");
        Assert.False(model.Resolution.StageResolutionCommand.CanExecute(null)); Assert.False(model.Merge.FinishMergeCommand.CanExecute(null));
        Assert.False(model.Merge.RequestAbortMergeCommand.CanExecute(null)); Assert.False(model.Commit.CommitCommand.CanExecute(null)); Assert.NotEmpty(model.StatusBar.RepositoryWarning);
    }

    // Opens the merge, confirms an abort nobody asked for, asks for one and keeps the merge instead; nothing is written.
    private static async Task OpenThenKeepTheMergeAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Merge.Abort.ConfirmCommand.ExecuteAsync(null);
        model.Merge.RequestAbortMergeCommand.Execute(null);
        model.Merge.Abort.CancelCommand.Execute(null);
    }

    // A merge waiting on one conflict, with the message Git prepared; a write is refused unless a test says otherwise.
    private static FakeRepositoryWorkspace Backend() => new()
    {
        Snapshot = Snapshots.Create(head: "head", operation: RepositoryOperation.Merge, files: [Snapshots.Conflict("conflict.txt")],
            merge: new("incoming", "head", "Default merge message", "merge", "")),
        Result = new(GitWriteOutcome.Rejected, "Hook rejected merge")
    };
}
