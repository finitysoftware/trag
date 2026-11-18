using Trag.Domain.Branches;
using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Branches;

namespace Trag.ViewModels.UnitTests.Branches;

public sealed class BranchViewModelTests
{
    [Fact]
    public async Task WhenTheBranchListReloadsUnderAnOpenDraft_ShouldKeepTheDraftAndItsExplicitCurrentHeadStart()
    {
        var h = Harness("main", "other"); var branches = h.Branches;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.History.SelectedCommit = Snapshots.Commit(Second, "Older commit");
        await model.Branches.CreateBranchFromCommitCommand.ExecuteAsync(null);
        model.Branches.Draft.Name = "my-draft";
        model.Branches.Draft.SwitchAfterCreate = false;
        model.Branches.Draft.SelectedStartPoint = model.Branches.Draft.StartPoints[0];
        branches.Local.Add("my-draft");
        await model.Branches.List.ReloadAsync();
        Assert.Equal("my-draft", model.Branches.Draft.Name);
        Assert.False(model.Branches.Draft.SwitchAfterCreate);
        Assert.Null(model.Branches.Draft.SelectedStartPoint!.Ref);
        Assert.Contains("already exists", model.Branches.Draft.NameError);
        Assert.False(model.Branches.CreateBranchCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenHeadMovesBeforeTheKeepCommitsAnswer_ShouldWriteNothingAndSayTheRepositoryChanged(bool keep)
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer;
        reader.Snapshot = Snapshots.Create(branch: null, head: Second, detachedCommits: 1); h.Branches.Current = null;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.ReloadAsync();
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.Single());
        Assert.True(model.GoTo.Keep.IsOpen);
        reader.Snapshot = reader.Snapshot with { Head = HeadId };
        await model.RefreshCommand.ExecuteAsync(null);
        if (keep) await model.GoTo.KeepCommitsCommand.ExecuteAsync(null);
        else await model.GoTo.DiscardCommitsCommand.ExecuteAsync(null);
        Assert.Empty(writer.Requests);
        Assert.True(model.GoTo.Keep.IsOpen);
        Assert.Contains("Repository changed", model.GoTo.Keep.Notice);
    }
    // The current branch is renamed and asked a request for, another local branch also merged and deleted, a remote
    // branch only merged and deleted.
    [Fact]
    public void WhenABranchRowIsOffered_ShouldOfferWhatItsKindAllows()
    {
        var local = new BranchInfo("topic", "refs/heads/topic", Second, false);
        var remote = new BranchInfo("origin/topic", "refs/remotes/origin/topic", Second, true, "origin");
        static (bool, bool, bool, bool) Offers(BranchRow row) => (row.CanRename, row.CanMerge, row.CanRequest, row.CanDelete);
        Assert.Equal((true, false, true, false), Offers(new BranchRow(local, IsCurrent: true)));
        Assert.Equal((true, true, true, true), Offers(new BranchRow(local, IsCurrent: false)));
        Assert.Equal((false, true, false, true), Offers(new BranchRow(remote, IsCurrent: false)));
    }

    private static readonly string HeadId = Snapshots.HeadId;
    private static readonly string Second = Snapshots.Id('b');

    // Switching away leaves a detached HEAD's own commits behind; a branch created at HEAD holds them and goes at once.
    [Fact]
    public async Task WhenABranchIsCreatedElsewhereAndSwitchedToFromADetachedHead_ShouldAskAboutTheDetachedCommitsFirst()
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer;
        reader.Snapshot = Snapshots.Create(branch: null, head: Second, detachedCommits: 1); h.Branches.Current = null;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        var draft = model.Branches.Draft;
        draft.Name = "elsewhere"; draft.SelectedStartPoint = draft.StartPoints.Single(option => option.Ref is not null); draft.SwitchAfterCreate = true;
        await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        Assert.Empty(writer.Requests); Assert.True(model.GoTo.Keep.IsOpen); Assert.True(draft.IsOpen);
        model.GoTo.CancelKeepCommitsCommand.Execute(null);
        Assert.False(model.GoTo.Keep.IsOpen); Assert.Empty(writer.Requests);

        draft.SelectedStartPoint = draft.StartPoints.Single(option => option.Ref is null);
        await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        var create = Payload(Assert.Single(writer.Requests));
        Assert.Equal(("elsewhere", null, true), (create.Name, create.StartPoint, create.Switch)); Assert.False(model.GoTo.Keep.IsOpen);
    }

    // HEAD stays where it is, so the detached commits stay with it.
    [Fact]
    public async Task WhenABranchIsCreatedElsewhereWithoutSwitching_ShouldAskNothing()
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer;
        reader.Snapshot = Snapshots.Create(branch: null, head: Second, detachedCommits: 1); h.Branches.Current = null;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        var draft = model.Branches.Draft;
        draft.Name = "elsewhere"; draft.SelectedStartPoint = draft.StartPoints.Single(option => option.Ref is not null); draft.SwitchAfterCreate = false;
        await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        var create = Payload(Assert.Single(writer.Requests));
        Assert.Equal(("elsewhere", false), (create.Name, create.Switch)); Assert.NotNull(create.StartPoint); Assert.False(model.GoTo.Keep.IsOpen);
    }

    [Fact]
    public async Task WhenBranchesAreSwitchedFromThePicker_ShouldListCurrentLocalAndRemoteBranchesAndSwitchThroughVerifiedWrites()
    {
        var h = Harness("main", "fix/telephone", "feature/configurator"); var writer = h.Writer;
        h.Branches.Remote.AddRange(["origin/main", "origin/release"]);
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.Equal("main", model.Header.BranchText);
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        Assert.Equal("main", model.Branches.Rows.Current!.Name); Assert.True(model.Branches.Rows.Current.IsCurrent); Assert.True(model.Branches.Visible.HasCurrent);
        Assert.Equal(["feature/configurator", "fix/telephone"], model.Branches.Visible.Local.Select(row => row.Name));
        var group = Assert.Single(model.Branches.Visible.RemoteGroups);
        Assert.Equal("REMOTE · origin", group.Title); Assert.Equal(["origin/main", "origin/release"], group.Rows.Select(row => row.Name));
        Assert.Contains("tracks origin/main", model.Branches.Rows.Current.Tooltip);
        model.Branches.BranchSearch = "TELE";
        Assert.Single(model.Branches.Visible.Local); Assert.Empty(model.Branches.Visible.RemoteGroups); Assert.False(model.Branches.Visible.HasCurrent); Assert.True(model.Branches.Visible.HasAny);
        model.Branches.BranchSearch = "";
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.Local.Single(row => row.Name == "fix/telephone"));
        var request = Assert.Single(writer.Requests);
        Assert.Equal("fix/telephone", Branch(request, BranchAction.Switch).Branch.Name);
        Assert.Equal("fix/telephone", model.Header.BranchText); Assert.Contains("Switched", model.StatusBar.Status); Assert.False(model.Repository.IsWriting);
        await model.Branches.List.Completion;
        Assert.Equal("fix/telephone", model.Branches.Rows.Current!.Name);
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.RemoteGroups.Single().Rows.Single(row => row.Name == "origin/release"));
        Assert.Equal("release", Branch(writer.Requests[^1], BranchAction.Create).Branch.Name);
        Assert.Equal("refs/remotes/origin/release", Payload(writer.Requests[^1]).Track); Assert.True(Payload(writer.Requests[^1]).Switch);
        Assert.Equal("release", model.Header.BranchText);
        await model.Branches.List.Completion;
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.RemoteGroups.Single().Rows.Single(row => row.Name == "origin/main"));
        Assert.Equal("main", Branch(writer.Requests[^1], BranchAction.Switch).Branch.Name);
        Assert.Equal("main", model.Header.BranchText);
        await model.Branches.List.Completion;
        var count = writer.Requests.Count;
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Current!);
        Assert.Equal(count, writer.Requests.Count);
        // The remote counterpart of the current branch is not a new branch either.
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.RemoteGroups.Single().Rows.Single(row => row.Name == "origin/main"));
        Assert.Equal(count, writer.Requests.Count); Assert.Equal("You are already on main.", model.StatusBar.Status); Assert.False(model.StatusBar.HasError);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Null(model.Branches.Rows.Current); Assert.Empty(model.Branches.Visible.Local);
    }

    // A branch row stays enabled for its menu while the last outcome waits for review, so choosing it reaches the writer,
    // which refuses the switch and says why on the status line.
    [Fact]
    public async Task WhenABranchIsChosenWhileTheLastOutcomeWaitsForReview_ShouldSwitchNothingAndSayWhy()
    {
        var h = Harness("main", "topic"); var writer = h.Writer;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        model.Repository.NeedsReview = true;

        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Visible.Local.Single());

        Assert.Empty(writer.Requests);
        Assert.Equal(WriteText.ReviewFirst, model.StatusBar.Status);
    }

    [Fact]
    public async Task WhenBranchesAreCreatedInTheDialog_ShouldValidateTheNameAndStartWhereChosenOrAtTheSelectedCommit()
    {
        var h = Harness("main", "topic"); var writer = h.Writer;
        h.Branches.Remote.Add("origin/release");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        Assert.True(model.Branches.Draft.IsOpen);
        Assert.Equal("main · current HEAD", model.Branches.Draft.StartPoints[0].Display); Assert.Same(model.Branches.Draft.StartPoints[0], model.Branches.Draft.SelectedStartPoint);
        Assert.Contains(model.Branches.Draft.StartPoints, option => option.Ref == "refs/heads/topic"); Assert.Contains(model.Branches.Draft.StartPoints, option => option.Ref == "refs/remotes/origin/release");
        Assert.DoesNotContain(model.Branches.Draft.StartPoints, option => option.Ref == "refs/heads/main");
        Assert.False(model.Branches.CreateBranchCommand.CanExecute(null));
        model.Branches.Draft.Name = "bad name"; Assert.True(model.Branches.Draft.HasNameError); Assert.False(model.Branches.CreateBranchCommand.CanExecute(null));
        model.Branches.Draft.Name = "topic"; Assert.Contains("already exists", model.Branches.Draft.NameError);
        model.Branches.Draft.Name = "feature/new"; Assert.False(model.Branches.Draft.HasNameError); Assert.True(model.Branches.CreateBranchCommand.CanExecute(null));
        model.Branches.Draft.SelectedStartPoint = model.Branches.Draft.StartPoints.Single(option => option.Ref == "refs/remotes/origin/release"); model.Branches.Draft.SwitchAfterCreate = false;
        Assert.Contains("origin/release", model.Branches.Draft.Subtitle);
        await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        var request = Assert.Single(writer.Requests);
        Assert.Equal("feature/new", Branch(request, BranchAction.Create).Branch.Name);
        Assert.Equal("refs/remotes/origin/release", Payload(request).StartPoint); Assert.False(Payload(request).Switch); Assert.Null(Payload(request).Track);
        Assert.False(model.Branches.Draft.IsOpen); Assert.Equal("main", model.Header.BranchText);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.False(model.Branches.CreateBranchFromCommitCommand.CanExecute(null));
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion;
        Assert.True(model.Branches.CreateBranchFromCommitCommand.CanExecute(null));
        await model.Branches.CreateBranchFromCommitCommand.ExecuteAsync(null);
        Assert.True(model.Branches.Draft.IsOpen); Assert.Equal(model.History.Page.Commits[0].Id, model.Branches.Draft.SelectedStartPoint!.Ref);
        Assert.StartsWith(model.History.Page.Commits[0].ShortId, model.Branches.Draft.SelectedStartPoint.Display);
        model.Branches.Draft.Name = "hotfix"; await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        Assert.Equal(model.History.Page.Commits[0].Id, Payload(writer.Requests[^1]).StartPoint); Assert.True(Payload(writer.Requests[^1]).Switch);
        Assert.Equal("hotfix", model.Header.BranchText);
        var reject = "A branch named x already exists. Choose another name or switch to it.";
        writer.Handle = null; writer.Result = new(GitWriteOutcome.Rejected, reject);
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null);
        Assert.Same(model.Branches.Draft.StartPoints[0], model.Branches.Draft.SelectedStartPoint);
        model.Branches.Draft.Name = "x"; await model.Branches.CreateBranchCommand.ExecuteAsync(null);
        Assert.True(model.Branches.Draft.IsOpen); Assert.Equal(reject, model.Branches.Draft.Notice); Assert.Equal("hotfix", model.Header.BranchText);
        model.Branches.CancelCreateBranchCommand.Execute(null); Assert.False(model.Branches.Draft.IsOpen);
    }

    [Fact]
    public async Task WhenHistoryOpensAndItsScopeIsToggled_ShouldWalkAllBranchesThenOnlyTheCurrentBranch()
    {
        var h = Harness("main"); var history = h.History;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.True(history.LastAllBranches); Assert.StartsWith("All branches", model.History.Page.Message);
        Assert.Equal(2, model.History.Page.Commits.Count); Assert.Equal(1, model.History.Page.Graph.LaneCount);
        Assert.NotNull(model.History.Page.Graph.For(model.History.Page.Commits[0].Id));
        history.Gate.IsClosed = true;
        model.History.ToggleHistoryScopeCommand.Execute(null);
        Assert.True(model.History.IsHistoryBusy); Assert.False(model.History.ShowHistoryCancel); Assert.False(model.History.HistoryAllBranches);
        history.Gate.Open(); await model.History.HistoryCompletion;
        Assert.False(model.History.IsHistoryBusy); Assert.False(history.LastAllBranches); Assert.StartsWith("Current branch", model.History.Page.Message);
        history.Gate.IsClosed = true;
        model.Navigator.ShowSearchCommand.Execute(null);
        Assert.True(model.History.IsHistoryBusy); Assert.True(model.History.ShowHistoryCancel);
        history.Gate.Open(); await model.History.HistoryCompletion;
        Assert.False(model.History.ShowHistoryCancel);
    }

    [Fact]
    public async Task WhenBranchesAreDeleted_ShouldAskForALocalOneOnlyWhenGitRefusesAndAlwaysForARemoteOne()
    {
        var h = Harness("main", "fix/telephone", "feature/x"); var writer = h.Writer;
        h.Branches.Remote.AddRange(["origin/main", "origin/release"]);
        writer.Handle = BranchWrites(h.Reader, h.Branches, "feature/x");
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        Assert.False(model.Branches.Rows.Current!.CanDelete); Assert.All(model.Branches.Visible.Local, row => Assert.True(row.CanDelete));
        await model.Branches.RequestDeleteCommand.ExecuteAsync(model.Branches.Visible.Local.Single(row => row.Name == "fix/telephone")); await model.Branches.List.Completion;
        Assert.False(model.Branches.Delete.IsOpen); Assert.Null(Branch(writer.Requests[^1], BranchAction.Delete).Branch.ForceAt);
        Assert.DoesNotContain(model.Branches.Rows.Local, row => row.Name == "fix/telephone");
        var feature = model.Branches.Visible.Local.Single(row => row.Name == "feature/x");
        await model.Branches.RequestDeleteCommand.ExecuteAsync(feature); await model.Branches.List.Completion;
        Assert.True(model.Branches.Delete.IsOpen); Assert.Same(feature, model.Branches.Delete.Subject); Assert.Equal("Delete branch", model.Branches.Delete.Subject!.DeleteTitle); Assert.Contains("not fully merged", model.Branches.Delete.Subject!.DeleteText);
        Assert.Contains(model.Branches.Rows.Local, row => row.Name == "feature/x");
        model.Branches.Delete.CancelCommand.Execute(null); Assert.False(model.Branches.Delete.IsOpen); Assert.Contains(model.Branches.Rows.Local, row => row.Name == "feature/x");
        await model.Branches.RequestDeleteCommand.ExecuteAsync(feature); await model.Branches.List.Completion;
        await model.Branches.Delete.ConfirmCommand.ExecuteAsync(null); await model.Branches.List.Completion;
        Assert.False(model.Branches.Delete.IsOpen); Assert.NotNull(Payload(writer.Requests[^1]).ForceAt); Assert.DoesNotContain(model.Branches.Rows.Local, row => row.Name == "feature/x");
        var release = model.Branches.Visible.RemoteGroups.Single().Rows.Single(row => row.Name == "origin/release");
        await model.Branches.RequestDeleteCommand.ExecuteAsync(release);
        Assert.True(model.Branches.Delete.IsOpen); Assert.Equal("Delete remote branch", model.Branches.Delete.Subject!.DeleteTitle); Assert.Contains("everyone", model.Branches.Delete.Subject!.DeleteText);
        Assert.DoesNotContain(writer.Requests, request => request is RemoteBranchDeleteRequest);
        await model.Branches.Delete.ConfirmCommand.ExecuteAsync(null); await model.Branches.List.Completion;
        var remote = Assert.IsType<RemoteBranchDeleteRequest>(writer.Requests[^1]); Assert.Equal("release", remote.Name); Assert.Equal("origin", remote.Remote);
        Assert.False(model.Branches.Delete.IsOpen); Assert.DoesNotContain(model.Branches.Rows.Remote, row => row.Name == "origin/release");
        writer.Handle = null; writer.Result = new(GitWriteOutcome.Rejected, "Git could not delete the branch.");
        model.Branches.Delete.Request(model.Branches.Visible.RemoteGroups.Single().Rows.Single());
        await model.Branches.Delete.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.Branches.Delete.IsOpen); Assert.Equal("Git could not delete the branch.", model.Branches.Delete.Message);
    }

    // A double-click never switches by itself: only the dialog's switch writes.
    [Fact]
    public async Task WhenACommitIsDoubleClicked_ShouldReportTheCurrentOneOrAskToSwitchToTheBranchAtItsTip()
    {
        var h = Harness("main", "old"); var writer = h.Writer; var second = Second;
        h.Branches.Heads["old"] = second;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        Assert.Equal(HeadId, model.GoTo.CurrentHeadId);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.Empty(writer.Requests); Assert.Equal("You are already on this commit.", model.StatusBar.Status); Assert.False(model.GoTo.Target.IsOpen);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.Empty(writer.Requests);
        Assert.True(model.GoTo.Target.IsOpen); Assert.True(model.GoTo.Dialog.IsTip); Assert.Equal("Switch to old", model.GoTo.Dialog.Title);
        await model.GoTo.SwitchToTipCommand.ExecuteAsync(null);
        var request = Assert.Single(writer.Requests);
        Assert.Equal("old", Branch(request, BranchAction.Switch).Branch.Name);
        Assert.Equal("old", model.Header.BranchText); Assert.False(model.GoTo.Target.IsOpen); Assert.Equal(second, model.GoTo.CurrentHeadId);
    }

    [Fact]
    public async Task WhenAnUnnamedCommitIsDoubleClicked_ShouldOfferABranchThereWithASuggestedName()
    {
        var h = Harness("main", "initial"); var writer = h.Writer; var second = Second;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.Target.IsOpen); Assert.Equal("Go to " + second[..7], model.GoTo.Dialog.Title); Assert.Equal("Initial", model.GoTo.Dialog.Subtitle);
        Assert.Equal("initial-2", model.GoTo.Target.Name); Assert.True(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null)); Assert.True(model.GoTo.DetachAtCommitCommand.CanExecute(null));
        model.GoTo.Target.Name = "bad name"; Assert.False(model.GoTo.CreateBranchAtCommitCommand.CanExecute(null)); Assert.Contains("spaces", model.GoTo.Target.NameError);
        model.GoTo.Target.Name = "inspect";
        await model.GoTo.CreateBranchAtCommitCommand.ExecuteAsync(null);
        var created = Assert.Single(writer.Requests);
        var inspect = Branch(created, BranchAction.Create).Branch; Assert.Equal("inspect", inspect.Name); Assert.Equal(second, inspect.StartPoint); Assert.True(inspect.Switch);
        Assert.False(model.GoTo.Target.IsOpen); Assert.Equal("inspect", model.Header.BranchText); Assert.Equal(second, model.GoTo.CurrentHeadId);
    }

    [Fact]
    public async Task WhenHeadIsDetachedToLookAroundAndLeftWithOwnCommits_ShouldAskToKeepThem()
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer; var second = Second;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.Target.IsOpen);
        await model.GoTo.DetachAtCommitCommand.ExecuteAsync(null);
        var detach = Assert.Single(writer.Requests); Assert.Equal(second, Branch(detach, BranchAction.Checkout).Branch.Name);
        Assert.False(model.GoTo.Target.IsOpen); Assert.True(model.GoTo.IsDetachedHead); Assert.Equal("Detached at " + second[..7], model.Header.BranchText);
        Assert.Equal("Not on a branch", model.GoTo.DetachedCardText); Assert.False(model.Publish.Context.ShowSyncSelectors); Assert.False(model.Sync.FetchCommand.CanExecute(null));
        Assert.True(model.Repository.CanWrite); Assert.Contains("Detached HEAD at " + second[..7], model.StatusBar.RepositoryWarning);

        // Two commits made while detached: switching away asks, Cancel changes nothing, Keep creates the branch first and then switches.
        reader.Snapshot = reader.Snapshot with { DetachedCommits = 2 }; await model.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("2 commits are not on a branch", model.GoTo.DetachedCardText);
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        var main = model.Branches.Rows.Local.Single(row => row.Name == "main");
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(main);
        Assert.True(model.GoTo.Keep.IsOpen); Assert.Equal("Keep 2 commits?", model.GoTo.KeepCommitsTitle); Assert.Equal("kept-from-" + second[..7], model.GoTo.Keep.Name); Assert.Single(writer.Requests);
        model.GoTo.CancelKeepCommitsCommand.Execute(null); Assert.False(model.GoTo.Keep.IsOpen); Assert.Single(writer.Requests); Assert.True(model.GoTo.IsDetachedHead);
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(main); Assert.True(model.GoTo.Keep.IsOpen);
        model.GoTo.Keep.Name = "rescued"; await model.GoTo.KeepCommitsCommand.ExecuteAsync(null);
        Assert.Equal([BranchAction.Checkout, BranchAction.Create, BranchAction.Switch], writer.Requests.Cast<BranchRequest>().Select(request => request.Action));
        Assert.Equal("rescued", Payload(writer.Requests[1]).Name); Assert.Null(Payload(writer.Requests[1]).StartPoint); Assert.False(Payload(writer.Requests[1]).Switch);
        Assert.Equal("main", Payload(writer.Requests[2]).Name); Assert.False(model.GoTo.Keep.IsOpen); Assert.Equal("main", model.Header.BranchText); Assert.False(model.GoTo.IsDetachedHead);
    }

    [Fact]
    public async Task WhenTheDetachedCommitsAreDiscarded_ShouldSwitchAndLeaveThemBehind()
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer;
        reader.Snapshot = Snapshots.Create(branch: null, head: Second, detachedCommits: 1); h.Branches.Current = null;
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        Assert.True(model.GoTo.IsDetachedHead); Assert.Equal("1 commit is not on a branch", model.GoTo.DetachedCardText);
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.Single());
        Assert.True(model.GoTo.Keep.IsOpen); Assert.Equal("Keep 1 commit?", model.GoTo.KeepCommitsTitle);
        await model.GoTo.DiscardCommitsCommand.ExecuteAsync(null);
        var request = Assert.Single(writer.Requests); Assert.Equal(BranchAction.Switch, Assert.IsType<BranchRequest>(request).Action); Assert.Equal("main", model.Header.BranchText); Assert.False(model.GoTo.Keep.IsOpen);
    }

    // The dialog opens blocked and says why (GoToBlockedTests has the rest). The jump follows a discard of detached
    // commits, so the one write is that discard's switch.
    [Fact]
    public async Task WhenTheTreeHasLocalChanges_ShouldOpenTheGoToDialogBlockedWithoutWriting()
    {
        var h = Harness("main"); var reader = h.Reader; var writer = h.Writer;
        reader.Snapshot = Snapshots.Create(branch: null, head: Second, detachedCommits: 1); h.Branches.Current = null;
        await using var model = h.Shell();
        await OpenThenDiscardTheDetachedCommitsAsync(model);

        reader.Snapshot = reader.Snapshot with { Files = [Snapshots.Modified("a.txt")] };
        await model.RefreshCommand.ExecuteAsync(null); model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        Assert.True(model.GoTo.Target.IsOpen); Assert.True(model.GoTo.Dialog.IsBlocked); Assert.Single(writer.Requests);
    }

    // Opens on a detached HEAD with one commit of its own, switches to main from the picker and discards that commit.
    private static async Task OpenThenDiscardTheDetachedCommitsAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        model.Branches.OpenBranchPickerCommand.Execute(null); await model.Branches.List.Completion;
        await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.Single());
        await model.GoTo.DiscardCommitsCommand.ExecuteAsync(null);
    }

    // The given local branches, two commits in history (the newer one on top of the older, enough for a graph with one
    // lane) and a writer that keeps the branch list and the status reader in step.
    private static ShellHarness Harness(params string[] local)
    {
        var h = new ShellHarness();
        h.Branches.Local.Clear(); h.Branches.Local.AddRange(local);
        h.History.Pages = (_, page) => new([Snapshots.Commit(HeadId, "Update image", Second), Snapshots.Commit(Second, "Initial")], false, page);
        h.History.Details = id => new(Snapshots.Commit(id, "Update image"), "Body", []);
        h.History.Diff = (_, _, _) => new FileDiff([], [], DiffContentKind.Text, "");
        h.Writer.Handle = BranchWrites(h.Reader, h.Branches);
        return h;
    }

    // Git's own bookkeeping for the branch writes the picker issues: the branch list and the status reader have to
    // agree on what exists and where HEAD is after every one of them, and a ref that moved moves the snapshot's
    // fingerprint of the local or of the remote-tracking refs, which is what makes the list read again.
    private static Func<GitWriteRequest, CancellationToken, Task<GitWriteResult>> BranchWrites(FakeRepositoryReader reader, FakeBranchReader branches, params string[] unmerged) =>
        (request, _) =>
        {
            var result = BranchWrite(reader, branches, unmerged, request);
            reader.Snapshot = reader.Snapshot with
            {
                LocalRefs = string.Join(" ", branches.Local.Select(name => name + "=" + branches.Heads.GetValueOrDefault(name, HeadId))),
                RemoteRefs = string.Join(" ", branches.Remote)
            };
            return Task.FromResult(result);
        };

    private static GitWriteResult BranchWrite(FakeRepositoryReader reader, FakeBranchReader branches, string[] unmerged, GitWriteRequest request)
    {
        if (request is RemoteBranchDeleteRequest { Remote: var remote, Name: var deleted })
        {
            branches.Remote.Remove(remote + "/" + deleted);
            return new(GitWriteOutcome.Succeeded, remote + "/" + deleted + " was deleted on " + remote + ".");
        }
        if (request is LfsDownloadRequest) return new(GitWriteOutcome.Succeeded, "");
        var name = Payload(request).Name;
        if (request is BranchRequest { Action: BranchAction.Delete })
        {
            if (unmerged.Contains(name) && Payload(request).ForceAt is null) return new(GitWriteOutcome.NeedsResolution, "Branch " + name + " is not fully merged.");
            branches.Local.Remove(name); return new(GitWriteOutcome.Succeeded, "Branch " + name + " deleted.");
        }
        if (request is BranchRequest { Action: BranchAction.Checkout }) { Checkout(reader, branches, null, name); return new(GitWriteOutcome.Succeeded, "Now at " + name[..7] + ", not on a branch."); }
        if (request is BranchRequest { Action: BranchAction.Switch }) { Checkout(reader, branches, name, branches.Heads.GetValueOrDefault(name, HeadId)); return new(GitWriteOutcome.Succeeded, "Switched to " + name + "."); }
        if (!branches.Local.Contains(name)) branches.Local.Add(name);
        branches.Heads[name] = Payload(request).StartPoint ?? reader.Snapshot.Head!;
        if (Payload(request).StartPoint is null) reader.Snapshot = reader.Snapshot with { DetachedCommits = 0 }; // a branch at HEAD now reaches the detached commits
        if (Payload(request).Switch) Checkout(reader, branches, name, branches.Heads[name]);
        return new(GitWriteOutcome.Succeeded, "Branch " + name + " created.");
    }

    private static void Checkout(FakeRepositoryReader reader, FakeBranchReader branches, string? branch, string head)
    {
        reader.Snapshot = reader.Snapshot with { Branch = branch, Head = head, DetachedCommits = 0 };
        branches.Current = branch;
    }

    private static BranchOperation Payload(GitWriteRequest request) => Assert.IsType<BranchRequest>(request).Branch;

    // Every branch command builds one request; this says which one, and hands its payload to the assertion.
    private static BranchRequest Branch(GitWriteRequest request, BranchAction action)
    {
        var branch = Assert.IsType<BranchRequest>(request);
        Assert.Equal(action, branch.Action);
        return branch;
    }
}
