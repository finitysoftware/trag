using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.CherryPick;

// Cherry-picking the commit chosen in History: the question first, what keeps a cherry-pick back, and how it ends.
public sealed class CherryPickViewModelTests
{
    private static readonly string Older = Snapshots.Id('b');

    [Fact]
    public async Task WhenAnotherBranchsCommitIsPickedAfterTheQuestion_ShouldPickThatCommitAndClose()
    {
        var h = Harness();
        await using var model = await HistoryShownAsync(h);
        Assert.False(model.CherryPick.RequestCherryPickCommand.CanExecute(null));
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);

        Assert.True(model.CherryPick.Question.IsOpen);
        Assert.Equal("Cherry-pick commit " + Older[..7] + "?", model.CherryPick.Title);
        Assert.Equal("A new commit on top of main applies “Older commit” again. The original commit stays where it is.", model.CherryPick.Question.Text);
        Assert.Equal("", model.CherryPick.Refusal);
        await model.CherryPick.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Older, Assert.IsType<CherryPickRequest>(Assert.Single(h.Writer.Requests)).Commit);
        Assert.False(model.CherryPick.Question.IsOpen);
    }

    // Whether the branch has the commit already is read before the question opens. A read that fails decides nothing:
    // the question is asked, and the cherry-pick itself checks again before Git runs.
    [Fact]
    public async Task WhenWhetherTheBranchHasTheCommitCannotBeRead_ShouldStillAskAndLeaveTheCheckToTheCherryPick()
    {
        var h = Harness();
        h.Branches.QuestionFailure = new TimeoutException("the read took longer than its limit");
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);

        Assert.True(model.CherryPick.Question.IsOpen);
        Assert.Equal("", model.CherryPick.Refusal);
    }

    // A commit the current branch has already is no cherry-pick: the question says so and offers none.
    [Fact]
    public async Task WhenTheCurrentBranchHasTheCommitAlready_ShouldSaySoAndOfferNoCherryPick()
    {
        var h = Harness();
        h.Branches.Contained.Add(Older);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);

        Assert.Equal("Commit " + Older[..7] + " is already on main. To bring back a reverted commit, revert its revert.", model.CherryPick.Refusal);
        Assert.False(model.CherryPick.Question.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheTreeHasChangesOrTheCommitIsAMerge_ShouldSayWhyAndOfferNoCherryPick()
    {
        var h = Harness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Merge topic", Older, Snapshots.Id('c')), Snapshots.Commit(Older, "Older commit")], false, page);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);
        Assert.Equal("Cherry-picking a merge commit is not supported. Choose one of the commits it brought in instead.", model.CherryPick.Refusal);

        model.CherryPick.Question.CancelCommand.Execute(null);
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);

        Assert.Equal("Commit or stash your changes before cherry-picking a commit.", model.CherryPick.Refusal);
        Assert.False(model.CherryPick.Question.ConfirmCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    // A cherry-pick that stops at conflicts has started: the question closes and the view of the pick in progress opens.
    [Fact]
    public async Task WhenTheCherryPickStopsAtConflicts_ShouldCloseTheQuestionAndOpenThePickView()
    {
        var h = Harness();
        h.Writer.Handle = (_, _) =>
        {
            h.Reader.Snapshot = Snapshots.Create(operation: RepositoryOperation.CherryPick, files: [Snapshots.Conflict("a.txt")]) with { PickHead = Older };
            return Task.FromResult(new GitWriteResult(GitWriteOutcome.NeedsResolution, "Cherry-picking bbbbbbb stopped at 1 conflicted file."));
        };
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);

        await model.CherryPick.Question.ConfirmCommand.ExecuteAsync(null);

        Assert.False(model.CherryPick.Question.IsOpen);
        Assert.Equal(WorkspaceSection.Pick, model.Navigator.Section);
        Assert.Equal("Cherry-pick in progress", model.Pick.Words.Title);
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
