using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Revert;

// Reverting the commit chosen in History: the question first, what keeps a revert back, and how the question ends.
public sealed class RevertViewModelTests
{
    private static readonly string Older = Snapshots.Id('b');

    [Fact]
    public async Task WhenTheChosenCommitIsRevertedAfterTheQuestion_ShouldRevertThatCommitAndClose()
    {
        var h = Harness();
        await using var model = await HistoryShownAsync(h);
        Assert.False(model.Revert.RequestRevertCommand.CanExecute(null));
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        model.Revert.RequestRevertCommand.Execute(null);

        Assert.True(model.Revert.Question.IsOpen);
        Assert.Equal("Revert commit " + Older[..7] + "?", model.Revert.Title);
        Assert.Equal("A new commit on top of main undoes “Older commit”. The commit itself stays in the history.", model.Revert.Question.Text);
        Assert.Equal("", model.Revert.Refusal);
        await model.Revert.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Older, Assert.IsType<RevertRequest>(Assert.Single(h.Writer.Requests)).Commit);
        Assert.False(model.Revert.Question.IsOpen);
    }

    // Git refuses a revert over local changes, and Trag reverts no merge commit; the question says which and offers no
    // Revert.
    [Fact]
    public async Task WhenTheTreeHasChangesOrTheCommitIsAMerge_ShouldSayWhyAndOfferNoRevert()
    {
        var h = Harness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Merge topic", Older, Snapshots.Id('c')), Snapshots.Commit(Older, "Older commit")], false, page);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);

        Assert.Equal("Reverting a merge commit is not supported. Choose one of the commits it brought in instead.", model.Revert.Refusal);
        Assert.False(model.Revert.Question.ConfirmCommand.CanExecute(null));

        model.Revert.Question.CancelCommand.Execute(null);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);

        Assert.Equal("Commit or stash your changes before reverting a commit.", model.Revert.Refusal);
        Assert.False(model.Revert.Question.ConfirmCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    // A revert that stops at conflicts has started: the question closes and the conflicts are the next thing to do.
    [Fact]
    public async Task WhenTheRevertStopsAtConflicts_ShouldCloseTheQuestion()
    {
        var h = Harness();
        h.Writer.Result = new(GitWriteOutcome.NeedsResolution, "Reverting bbbbbbb stopped at 1 conflicted file. Resolve it, then finish the revert.");
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);

        await model.Revert.Question.ConfirmCommand.ExecuteAsync(null);

        Assert.False(model.Revert.Question.IsOpen);
    }

    [Fact]
    public async Task WhenGitRefusesTheRevert_ShouldKeepTheQuestionOpenWithTheReason()
    {
        var h = Harness();
        var refusal = "Nothing to revert: the changes of bbbbbbb are already undone here.";
        h.Writer.Result = new(GitWriteOutcome.Rejected, refusal);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        model.Revert.RequestRevertCommand.Execute(null);

        await model.Revert.Question.ConfirmCommand.ExecuteAsync(null);

        Assert.True(model.Revert.Question.IsOpen);
        Assert.Equal(refusal, model.Revert.Question.Message);
    }

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        return h;
    }

    private static async Task<ShellViewModel> HistoryShownAsync(ShellHarness h)
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        return model;
    }
}
