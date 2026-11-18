using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Rebase;

// Editing the current branch's commits from the one chosen in History: the dialog lists that commit and the ones after it,
// oldest first, each with what happens to it; commits move up and down, a reword or a squash shows the message the commit
// gets; what keeps the edit back and what the upstream already has is said before Git is asked.
public sealed class RewriteViewModelTests
{
    private static readonly string Older = Snapshots.Id('b');
    private static readonly string Base = Snapshots.Id('c');

    // The branch after Base: "Older commit" (with a body), then "Update image" at HEAD.
    private static RewriteRange Range(int merges = 0) => new(Base,
        [new(Older, "Older commit", "Older commit\n\nBody", RewriteAction.Pick, "Older commit\n\nBody"),
         new(Snapshots.HeadId, "Update image", "Update image", RewriteAction.Pick, "Update image")], merges);

    private static ShellHarness Harness()
    {
        var h = new ShellHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        h.Branches.Rewrites[Older] = Range();
        return h;
    }

    // History shown and the older commit asked about.
    private static async Task<ShellViewModel> AskedAsync(ShellHarness h)
    {
        var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null);
        return model;
    }

    private static RewriteRequest Sent(ShellHarness h) => Assert.IsType<RewriteRequest>(Assert.Single(h.Writer.Requests));

    // The title names the branch whose commits would be edited; a terminal that switches the branch while the dialog is
    // open has it name the new one, which the edit would act on (and refuse, its plan being another branch's).
    [Fact]
    public async Task WhenTheBranchIsSwitchedWhileTheDialogIsOpen_ShouldNameTheNewBranchInTheTitle()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var titles = new List<string>();
        model.Rewrite.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Rewrite.Title)) titles.Add(model.Rewrite.Title); };

        h.Reader.Snapshot = h.Reader.Snapshot with { Branch = "topic", Head = Snapshots.Id('e') };
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(["Edit commits on topic"], titles.Distinct());
    }

    [Fact]
    public async Task WhenACommitOfTheBranchIsChosen_ShouldListItAndTheCommitsAfterItAndWaitForAChange()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);

        var rewrite = model.Rewrite;
        Assert.True(rewrite.Question.IsOpen);
        Assert.Equal(("Edit commits on main", "2 commits, from " + Older[..7] + " to the tip"), (rewrite.Title, rewrite.Question.Text));
        Assert.Equal([(Older[..7], "Older commit", RewriteAction.Pick), (Snapshots.HeadId[..7], "Update image", RewriteAction.Pick)],
            rewrite.Rows.Select(row => (row.ShortId, row.Subject, row.Action)));
        Assert.Equal(["Pick", "Reword", "Squash", "Fixup", "Drop"], rewrite.Actions.Select(choice => choice.Name));
        Assert.Equal((RewriteText.Unchanged, ""), (rewrite.Refusal, rewrite.Warning));
        Assert.False(rewrite.Question.ConfirmCommand.CanExecute(null));
        Assert.Same(rewrite.Rows[0], rewrite.SelectedRow);
    }

    // The menu item follows the chosen commit and the writes, and the dialog says its title when it opens.
    [Fact]
    public async Task WhenTheChoiceOrTheRepositoryChanges_ShouldSayWhetherAnEditCanBeAsked()
    {
        var h = Harness();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        var rewrite = model.Rewrite;
        List<bool> allowed = [];
        rewrite.RequestRewriteCommand.CanExecuteChanged += (_, _) => allowed.Add(rewrite.RequestRewriteCommand.CanExecute(null));
        List<string> changed = [];
        rewrite.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        Assert.True(allowed[^1]);
        var release = new TaskCompletionSource();
        h.Writer.Handle = async (_, _) => { await release.Task; return new(GitWriteOutcome.Succeeded, "Done"); };
        var writing = model.Repository.Writer.RunAsync(expected => new ResetRequest(expected, Older, ResetMode.Soft));
        try { Assert.False(allowed[^1]); }
        finally { release.SetResult(); await writing; }

        await rewrite.RequestRewriteCommand.ExecuteAsync(null);
        Assert.Contains(nameof(rewrite.Title), changed);
    }

    // A commit dragged to another place goes there and stays chosen, the way the buttons move it.
    [Fact]
    public async Task WhenACommitIsMovedToAPlace_ShouldPutItThereAndKeepItChosen()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        var moved = rewrite.Rows[0];
        rewrite.SelectedRow = rewrite.Rows[1];

        rewrite.MoveTo(moved, 1);

        Assert.Equal(["Update image", "Older commit"], rewrite.Rows.Select(row => row.Subject));
        Assert.Same(moved, rewrite.SelectedRow);
        Assert.Equal("", rewrite.Refusal);
    }

    // A move may change which commits join which, so an edited joined message is proposed afresh.
    [Fact]
    public async Task WhenACommitIsMoved_ShouldProposeTheJoinedMessageAfresh()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.Rows[1].Action = RewriteAction.Squash;
        rewrite.SelectedRow = rewrite.Rows[1];
        rewrite.Message = "Joined";

        rewrite.MoveUpCommand.Execute(null);
        rewrite.MoveDownCommand.Execute(null);

        Assert.Equal("Older commit\n\nBody\n\nUpdate image", rewrite.Message);
    }

    [Fact]
    public async Task WhenACommitIsMovedUp_ShouldRewriteTheBranchInTheNewOrderAndClose()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        var moved = rewrite.Rows[1];
        rewrite.SelectedRow = moved;
        Assert.False(rewrite.MoveDownCommand.CanExecute(null));

        rewrite.MoveUpCommand.Execute(null);

        Assert.Equal(["Update image", "Older commit"], rewrite.Rows.Select(row => row.Subject));
        Assert.Same(moved, rewrite.SelectedRow);
        Assert.False(rewrite.MoveUpCommand.CanExecute(null));
        Assert.Equal("", rewrite.Refusal);
        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        var request = Sent(h);
        Assert.Equal(Base, request.Base);
        Assert.Equal([Snapshots.HeadId, Older], request.Steps.Select(step => step.Id));
        Assert.False(rewrite.Question.IsOpen);
    }

    // A pick shows its message as it is; a reword lets it be written anew.
    [Fact]
    public async Task WhenACommitIsReworded_ShouldEditItsMessageAndSendIt()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.SelectedRow = rewrite.Rows[0];
        Assert.Equal((false, "Older commit\n\nBody"), (rewrite.CanEditMessage, rewrite.Message));
        List<string> changed = [];
        rewrite.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        rewrite.Rows[0].Action = RewriteAction.Reword;
        rewrite.Message = "Better message";

        Assert.Contains(nameof(rewrite.CanEditMessage), changed);
        Assert.True(rewrite.CanEditMessage);
        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal((RewriteAction.Reword, "Better message"), (Sent(h).Steps[0].Action, Sent(h).Steps[0].Message));
    }

    // A reword taken back keeps the commit's own message; what was written for it goes.
    [Fact]
    public async Task WhenARewordIsTakenBack_ShouldKeepTheOriginalMessage()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.SelectedRow = rewrite.Rows[0];
        rewrite.Rows[0].Action = RewriteAction.Reword;
        rewrite.Message = "Better message";

        rewrite.Rows[0].Action = RewriteAction.Pick;
        rewrite.Rows[1].Action = RewriteAction.Drop;

        Assert.Equal("Older commit\n\nBody", rewrite.Message);
        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal("Older commit\n\nBody", Sent(h).Steps[0].Message);
    }

    // Another write holds the edit back until it ends.
    [Fact]
    public async Task WhenAnotherWriteRuns_ShouldNotStartTheEdit()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.Rows[1].Action = RewriteAction.Drop;
        var release = new TaskCompletionSource();
        h.Writer.Handle = async (_, _) => { await release.Task; return new(GitWriteOutcome.Succeeded, "Done"); };

        var writing = model.Repository.Writer.RunAsync(expected => new ResetRequest(expected, Older, ResetMode.Soft));
        try { Assert.False(rewrite.Question.ConfirmCommand.CanExecute(null)); }
        finally { release.SetResult(); await writing; }
    }

    // Squashed, the commits' messages are proposed together and can be edited; a fixup instead keeps only the first one's,
    // and squashing again proposes them afresh.
    [Fact]
    public async Task WhenACommitIsSquashed_ShouldProposeTheJoinedMessageAndSendTheEditedOne()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.SelectedRow = rewrite.Rows[1];

        rewrite.Rows[1].Action = RewriteAction.Squash;
        Assert.Equal((true, "Older commit\n\nBody\n\nUpdate image"), (rewrite.CanEditMessage, rewrite.Message));
        rewrite.Message = "Joined";
        rewrite.Rows[1].Action = RewriteAction.Fixup;
        Assert.Equal((false, "Older commit\n\nBody"), (rewrite.CanEditMessage, rewrite.Message));
        rewrite.Rows[1].Action = RewriteAction.Squash;
        Assert.Equal("Older commit\n\nBody\n\nUpdate image", rewrite.Message);
        rewrite.Message = "Joined";

        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal([(Older, RewriteAction.Pick, "Joined"), (Snapshots.HeadId, RewriteAction.Squash, "Update image")],
            Sent(h).Steps.Select(step => (step.Id, step.Action, step.Message)));
    }

    [Fact]
    public async Task WhenThePlanBreaksARule_ShouldSayWhyAndNotStart()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;

        rewrite.Rows[0].Action = RewriteAction.Fixup;
        Assert.Equal(RewriteText.FirstFolded, rewrite.Refusal);
        Assert.False(rewrite.Question.ConfirmCommand.CanExecute(null));
        rewrite.Rows[0].Action = RewriteAction.Drop;
        rewrite.Rows[1].Action = RewriteAction.Drop;
        Assert.Equal(RewriteText.AllDropped, rewrite.Refusal);
    }

    // From the first commit the edit has no base, and it is sent so.
    [Fact]
    public async Task WhenTheFirstCommitIsChosen_ShouldEditTheWholeHistoryWithNoBase()
    {
        var h = Harness();
        h.Branches.Rewrites[Older] = Range() with { Base = null };
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.Rows[1].Action = RewriteAction.Drop;

        Assert.Equal("", rewrite.Refusal);
        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Null(Sent(h).Base);
    }

    // A commit of another branch, a merge among them, local changes and a detached HEAD.
    [Fact]
    public async Task WhenTheCommitsCannotBeEdited_ShouldSayWhyAndOfferNoStart()
    {
        var h = Harness();
        h.Branches.Rewrites.Clear();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        Assert.Equal("Commit " + Older[..7] + " is not in the history of main. Only the current branch's own commits can be edited.", rewrite.Refusal);
        Assert.Empty(rewrite.Rows);

        rewrite.Question.CancelCommand.Execute(null);
        h.Branches.Rewrites[Older] = Range(merges: 1);
        await rewrite.RequestRewriteCommand.ExecuteAsync(null);
        Assert.Equal(RewriteText.HasMerges, rewrite.Refusal);

        rewrite.Question.CancelCommand.Execute(null);
        h.Branches.Rewrites[Older] = Range();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await model.RefreshCommand.ExecuteAsync(null);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await rewrite.RequestRewriteCommand.ExecuteAsync(null);
        Assert.Equal(RebaseText.NeedsCleanTree, rewrite.Refusal);

        rewrite.Question.CancelCommand.Execute(null);
        h.Reader.Snapshot = Snapshots.Create(branch: null);
        await model.RefreshCommand.ExecuteAsync(null);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await rewrite.RequestRewriteCommand.ExecuteAsync(null);
        Assert.Equal(RewriteText.NeedsBranch, rewrite.Refusal);
        Assert.False(rewrite.Question.ConfirmCommand.CanExecute(null));
        Assert.Empty(h.Writer.Requests);
    }

    // Git refusing to start the edit leaves the dialog open with Git's reason in it, and the plan as it was drafted.
    [Fact]
    public async Task WhenGitRefusesToStart_ShouldKeepTheDialogOpenWithTheReason()
    {
        var h = Harness();
        h.Writer.Result = GitWriteResult.Rejected("A pre-rebase hook refused the rewrite.");
        await using var model = await AskedAsync(h);
        model.Rewrite.MoveTo(model.Rewrite.Rows[1], 0);

        await model.Rewrite.Question.ConfirmCommand.ExecuteAsync(null);

        Assert.True(model.Rewrite.Question.IsOpen);
        Assert.Equal("A pre-rebase hook refused the rewrite.", model.Rewrite.Question.Message);
        Assert.Equal("Update image", model.Rewrite.Rows[0].Subject);
    }

    // The commits from an old one to the tip of a long history can be more than Git's output limit or the read's time
    // allows: the dialog says they could not be read and offers no start, instead of the failure ending the application.
    [Fact]
    public async Task WhenTheCommitsCannotBeRead_ShouldSaySoAndOfferNoStart()
    {
        var h = Harness();
        h.Branches.RewriteFailure = new TimeoutException("the read took longer than its limit");
        await using var model = await AskedAsync(h);

        Assert.Equal(RewriteText.Unreadable, model.Rewrite.Refusal);
        Assert.Empty(model.Rewrite.Rows);
        Assert.False(model.Rewrite.Question.ConfirmCommand.CanExecute(null));
    }

    // The working tree can change while the dialog is open; what keeps the edit back follows it, both ways.
    [Fact]
    public async Task WhenTheWorkingTreeChangesWhileTheDialogIsOpen_ShouldSayAgainWhetherTheEditCanStart()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.Rows[0].Action = RewriteAction.Drop;
        Assert.True(rewrite.Question.ConfirmCommand.CanExecute(null));
        var said = new List<string?>();
        rewrite.PropertyChanged += (_, e) => said.Add(e.PropertyName);

        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
        await model.RefreshCommand.ExecuteAsync(null);

        Assert.True(rewrite.Question.IsOpen);
        Assert.Contains(nameof(rewrite.Refusal), said);
        Assert.Equal(RebaseText.NeedsCleanTree, rewrite.Refusal);
        Assert.False(rewrite.Question.ConfirmCommand.CanExecute(null));
        h.Reader.Snapshot = h.Reader.Snapshot with { Files = [] };
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("", rewrite.Refusal);
        Assert.True(rewrite.Question.ConfirmCommand.CanExecute(null));
    }

    // The upstream has every commit of the branch, so both commits being edited are its.
    [Fact]
    public async Task WhenTheUpstreamHasTheCommits_ShouldWarnThatAPushReplacesThem()
    {
        var h = Harness();
        h.Reader.Snapshot = h.Synced();
        var model = h.Shell();
        await using var _ = model;
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;

        await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null);

        Assert.Equal(RewriteText.Pushed(2, "origin/main"), model.Rewrite.Warning);
    }

    // When Git stops at a conflict the rebase view takes over; when it refuses, the dialog stays and says why.
    [Fact]
    public async Task WhenTheRewriteStopsOrIsRefused_ShouldCloseForTheStopAndStayForTheRefusal()
    {
        var h = Harness();
        await using var model = await AskedAsync(h);
        var rewrite = model.Rewrite;
        rewrite.Rows[1].Action = RewriteAction.Drop;
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Git refused.");

        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal((true, "Git refused."), (rewrite.Question.IsOpen, rewrite.Question.Message));

        h.Writer.Result = new(GitWriteOutcome.NeedsResolution, "The rebase stopped.");
        await rewrite.Question.ConfirmCommand.ExecuteAsync(null);
        Assert.False(rewrite.Question.IsOpen);
    }
}
