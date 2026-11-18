using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Reset;

// Resetting the current branch to the commit chosen in History: the question says how far the branch goes back, the
// mode decides what happens to the changes, and what keeps a reset back is said before Git is asked.
public sealed class ResetViewModelTests
{
    private static readonly string Older = Snapshots.Id('b');

    [Fact]
    public async Task WhenAnOlderCommitOfTheBranchIsChosen_ShouldAskHowFarTheBranchGoesBackAndResetMixedByDefault()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        await using var model = await HistoryShownAsync(h);
        Assert.False(model.Reset.RequestResetCommand.CanExecute(null));
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.Reset.RequestResetCommand.ExecuteAsync(null);

        Assert.True(model.Reset.Question.IsOpen);
        Assert.Equal("Reset main to " + Older[..7] + "?", model.Reset.Title);
        Assert.Equal("main moves back 1 commit, from " + Snapshots.HeadId[..7] + " to " + Older[..7] + ".", model.Reset.Question.Text);
        Assert.Equal((ResetMode.Mixed, true, ""), (model.Reset.Mode, model.Reset.IsMixed, model.Reset.Refusal));
        Assert.Equal(("", ""), (model.Reset.Loss, model.Reset.PushedWarning));
        await model.Reset.Question.ConfirmCommand.ExecuteAsync(null);
        var request = Assert.IsType<ResetRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal((Older, ResetMode.Mixed), (request.Commit, request.Mode));
        Assert.False(model.Reset.Question.IsOpen);
    }

    // Two tracked changes and an untracked file: only the tracked ones are lost, and only a hard reset loses them.
    [Fact]
    public async Task WhenHardIsChosen_ShouldSayWhatUncommittedWorkIsLostAndResetHard()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt"), Snapshots.Untracked("c.txt")]);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);

        model.Reset.IsHard = true;

        Assert.Equal((ResetMode.Hard, false), (model.Reset.Mode, model.Reset.IsMixed));
        Assert.Equal("2 changed files lose their uncommitted changes for good. Untracked files stay.", model.Reset.Loss);
        model.Reset.IsSoft = true;
        Assert.Equal((ResetMode.Soft, ""), (model.Reset.Mode, model.Reset.Loss));
        model.Reset.IsHard = true;
        await model.Reset.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(ResetMode.Hard, Assert.IsType<ResetRequest>(Assert.Single(h.Writer.Requests)).Mode);
    }

    // An untracked file where the commit tracks one is not kept: a hard reset writes the commit's file over it, and the
    // question names it. Another untracked file still stays.
    [Fact]
    public async Task WhenAnUntrackedFileIsWhereTheCommitTracksOne_ShouldNameItAsLostOnAHardReset()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        h.Branches.NotInIndex[Older] = [Snapshots.Path("b.txt")];
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Untracked("b.txt"), Snapshots.Untracked("c.txt")]);
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);

        model.Reset.IsHard = true;

        Assert.Equal("No uncommitted change is lost. The untracked file b.txt is lost for good: the commit has a file in its place. Other untracked files stay.",
            model.Reset.Loss);
    }

    // The working tree can change while the question is open; what a hard reset loses follows it.
    [Fact]
    public async Task WhenTheWorkingTreeChangesWhileTheQuestionIsOpen_ShouldSayAgainWhatAHardResetLoses()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        model.Reset.IsHard = true;
        var said = new List<string?>();
        model.Reset.PropertyChanged += (_, e) => said.Add(e.PropertyName);

        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.True(model.Reset.Question.IsOpen);
        Assert.Contains(nameof(model.Reset.Loss), said);
        Assert.Equal(ResetText.HardLoss(1, []), model.Reset.Loss);
    }

    // The question says how many commits leave the branch from the HEAD it was asked on. A commit made meanwhile, in a
    // terminal or another tool, would leave too without being counted: the reset waits until it is asked again.
    [Fact]
    public async Task WhenHeadMovesWhileTheQuestionIsOpen_ShouldRefuseUntilTheQuestionIsAskedAgain()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        Assert.True(model.Reset.Question.ConfirmCommand.CanExecute(null));

        h.Reader.Snapshot = h.Reader.Snapshot with { Head = Snapshots.Id('e') };
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.True(model.Reset.Question.IsOpen);
        Assert.Equal(ResetText.HeadMoved("main"), model.Reset.Refusal);
        Assert.False(model.Reset.Question.ConfirmCommand.CanExecute(null));
    }

    // How far the branch goes back is read before the question opens; a read that fails says so and offers no reset,
    // instead of the failure leaving the command and ending the application.
    [Fact]
    public async Task WhenHowFarTheBranchGoesBackCannotBeRead_ShouldSaySoAndOfferNoReset()
    {
        var h = Harness();
        h.Branches.QuestionFailure = new TimeoutException("the read took longer than its limit");
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.Reset.RequestResetCommand.ExecuteAsync(null);

        Assert.True(model.Reset.Question.IsOpen);
        Assert.Equal(ResetText.DistanceUnreadable("main"), model.Reset.Refusal);
        Assert.False(model.Reset.Question.ConfirmCommand.CanExecute(null));
    }

    // The tip of the branch, a commit of another branch, and a detached HEAD: there is no reset to offer.
    [Fact]
    public async Task WhenTheCommitIsTheTipOrNotOnTheBranchOrHeadIsDetached_ShouldSayWhyAndOfferNoReset()
    {
        var h = Harness();
        h.Branches.Distances[Snapshots.HeadId] = 0;
        await using var model = await HistoryShownAsync(h);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        Assert.Equal("main is already at " + Snapshots.HeadId[..7] + ".", model.Reset.Refusal);
        Assert.False(model.Reset.Question.ConfirmCommand.CanExecute(null));
        model.Reset.Question.CancelCommand.Execute(null);

        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        Assert.Equal("Commit " + Older[..7] + " is not in the history of main. A reset moves main back to one of its own commits.", model.Reset.Refusal);
        model.Reset.Question.CancelCommand.Execute(null);

        h.Reader.Snapshot = Snapshots.Create(branch: null);
        await model.RefreshCommand.ExecuteAsync(null);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Reset.RequestResetCommand.ExecuteAsync(null);
        Assert.Equal("A reset moves a branch back. Switch to a branch first.", model.Reset.Refusal);
        Assert.False(model.Reset.Question.ConfirmCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    // The upstream has every commit of the branch, so the commit leaving it is one of its.
    [Fact]
    public async Task WhenALeavingCommitIsOnTheUpstream_ShouldWarnThatAPullWouldBringItBack()
    {
        var h = Harness();
        h.Branches.Distances[Older] = 1;
        h.Reader.Snapshot = h.Synced();
        await using var model = await HistoryShownAsync(h);
        await model.Sync.SyncCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.Reset.RequestResetCommand.ExecuteAsync(null);

        Assert.Equal("1 of these commits is already on origin/main; a Pull would bring it back.", model.Reset.PushedWarning);
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
