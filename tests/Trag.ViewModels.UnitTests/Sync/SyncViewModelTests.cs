using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Sync;

namespace Trag.ViewModels.UnitTests.Sync;

public sealed class SyncViewModelTests
{
    [Fact]
    public async Task WhenAnotherAccountIsChosenButNotSaved_ShouldNotSyncWithTheSavedOneUntilTheChoiceIsSaved()
    {
        var backend = Backend(accounts: new([new("alice", "Alice"), new("bob", "Bob")], "alice", "Saved"));
        await using var model = Create(backend); await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.FetchCommand.CanExecute(null)); Assert.Equal("alice", model.Sync.Panel.Accounts.Selected!.Id);
        Assert.True(model.Sync.Panel.Accounts.HasChoices);
        model.Sync.Panel.Accounts.Selected = model.Sync.Panel.Accounts.Items[1];
        Assert.False(model.Sync.FetchCommand.CanExecute(null)); Assert.False(model.Sync.PushCommand.CanExecute(null));
        Assert.True(model.Sync.SaveSyncAccountCommand.CanExecute(null)); Assert.Null(backend.LastRequest);
        await model.Sync.SaveSyncAccountCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        var select = Assert.IsType<SyncRequest>(backend.LastRequest); Assert.Equal(SyncAction.SelectAccount, select.Action); Assert.Equal("bob", select.AccountId);
        Assert.Equal("bob", model.Sync.Panel.Accounts.Selected!.Id); Assert.True(model.Sync.FetchCommand.CanExecute(null)); Assert.False(model.Sync.Panel.Accounts.IsUnsaved);
    }

    // Two accounts match and the repository saved neither: Trag picks neither, Git's own credentials are used as with no
    // account, and fetch, pull and push stay available while both accounts are offered.
    [Fact]
    public async Task WhenTwoAccountsMatchAndNoneIsSaved_ShouldSyncWithGitsOwnCredentialsAndOfferBoth()
    {
        // What RemoteAccountSelection.Choose gives for two matching accounts and nothing saved.
        var backend = Backend(accounts: new([new(AccountChoice.External, AccountText.GitCredentials), new("alice", "Alice"), new("bob", "Bob")],
            AccountChoice.External, AccountText.RemoteUsesGit), ahead: 1, behind: 1);
        await using var model = Create(backend); await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;

        Assert.Equal((AccountChoice.External, 3), (model.Sync.Panel.Accounts.Selected!.Id, model.Sync.Panel.Accounts.Items.Count));
        Assert.True(model.Sync.FetchCommand.CanExecute(null)); Assert.True(model.Sync.PullCommand.CanExecute(null)); Assert.True(model.Sync.PushCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheBranchHasNoUpstream_ShouldGuessNoTargetAndShowWhereTheChosenOnePushes()
    {
        var backend = Backend(tracking: false); await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Null(model.Sync.Selection.Remote); Assert.False(model.Sync.FetchCommand.CanExecute(null));
        Assert.Contains("unknown", model.Sync.Panel.Counts.Text);
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[0]; await model.Sync.SyncCompletion;
        Assert.True(model.Sync.FetchCommand.CanExecute(null)); Assert.False(model.Sync.PushCommand.CanExecute(null));
        Assert.Null(model.Sync.Selection.SelectedBranch);
        model.Sync.Selection.SelectedBranch = model.Sync.Selection.Branches[0]; await model.Sync.SyncCompletion;
        Assert.True(model.Sync.PushCommand.CanExecute(null)); Assert.Contains("refs/heads/trunk", model.Sync.Panel.Target.PushDestination);
        Assert.Contains("publish.example.invalid", model.Sync.Panel.Target.PushDestination);
        Assert.Contains("fetch.example.invalid", model.Sync.Panel.Target.FetchAddress);
        Assert.Null(backend.LastRequest);
    }
    [Fact]
    public async Task WhenTheUserSwitchesBackWhileTheOtherRemoteIsRead_ShouldDiscardItsLateAnswer()
    {
        var backend = Backend(); await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        backend.SyncGate.IsClosed = true;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[1]; var pending = model.Sync.SyncCompletion; await backend.SyncGate.Arrival(2);
        Assert.Contains("unknown", model.Sync.Panel.Counts.Text);
        backend.SyncGate.IsClosed = false; // the read that overtakes it answers at once
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[0]; await model.Sync.SyncCompletion;
        Assert.True(backend.SyncGate.TokenAt(2).IsCancellationRequested);
        backend.SyncGate.Open(); await pending;
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name); Assert.DoesNotContain("backup", model.Sync.Panel.Target.PushDestination);
    }
    [Fact]
    public async Task WhenTheRepositoryClosesAfterALateAnswerWasDiscarded_ShouldReleaseTheSyncSelection()
    {
        var backend = Backend(); await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await DiscardALateAnswerAsync(model, backend);
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Null(model.Sync.Selection.Remote); Assert.Empty(model.Sync.Selection.Branches);
    }
    [Fact]
    public async Task WhenAFetchRunsAndTheUserChangesScreens_ShouldKeepItRunningAsAWriteUntilCancelled()
    {
        var backend = Backend(); backend.Gate.IsClosed = true; await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        model.Commit.CommitMessage = "Keep my draft";
        var pending = model.Sync.FetchCommand.ExecuteAsync(null); await backend.Gate.Started;
        Assert.Equal(SyncAction.Fetch, Assert.IsType<SyncRequest>(backend.LastRequest).Action); Assert.True(model.Repository.IsWriting);
        Assert.False(model.Sync.CanChooseSyncTarget); Assert.False(model.CloseRepositoryCommand.CanExecute(null)); Assert.False(model.Sync.PullCommand.CanExecute(null));
        model.Navigator.ShowHistoryCommand.Execute(null); Assert.False(backend.Gate.Token.IsCancellationRequested);
        model.CancelCommand.Execute(null); Assert.True(backend.Gate.Token.IsCancellationRequested); Assert.True(model.Repository.IsWriting);
        backend.Result = new(GitWriteOutcome.Cancelled, "Verified fetch cancellation"); backend.Gate.Open(); await pending; await model.Sync.SyncCompletion;
        Assert.False(model.Repository.IsWriting); Assert.Equal("Keep my draft", model.Commit.CommitMessage);
        Assert.Contains("Verified", model.StatusBar.SyncOperationText); Assert.True(model.Sync.FetchCommand.CanExecute(null));
    }
    [Fact]
    public async Task WhenAPushEndsUncertain_ShouldRequireAReviewAndKeepTheCommitDraft()
    {
        var backend = Backend(); backend.Result = new(GitWriteOutcome.Uncertain, "Verify destination", "bounded details");
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; model.Commit.CommitMessage = "Keep this";
        await model.Sync.PushCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.True(model.Repository.NeedsReview); Assert.False(model.Sync.PushCommand.CanExecute(null)); Assert.False(model.Sync.FetchCommand.CanExecute(null));
        Assert.Equal("Keep this", model.Commit.CommitMessage); Assert.Equal("bounded details", model.StatusBar.SyncDetails);
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.False(model.Repository.NeedsReview); Assert.True(model.Sync.PushCommand.CanExecute(null));
    }
    [Fact]
    public async Task WhenTheBranchIsInStepOrAheadAndBehind_ShouldShowOnlyRealCountsOnTheToolbar()
    {
        await using var zero = Create(Backend()); await zero.Session.OpenRepositoryAsync("fixture"); await zero.Sync.SyncCompletion;
        Assert.Equal("Pull", zero.Sync.Panel.Counts.PullLabel); Assert.Equal("Push", zero.Sync.Panel.Counts.PushLabel); Assert.False(zero.Sync.Panel.Counts.HasPushCount); Assert.False(zero.Sync.Panel.Counts.HasPullCount);
        await using var model = Create(Backend(ahead: 2, behind: 1)); await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Equal("Push · 2", model.Sync.Panel.Counts.PushLabel); Assert.True(model.Sync.Panel.Counts.HasPushCount);
        Assert.Equal("Pull · 1", model.Sync.Panel.Counts.PullLabel); Assert.True(model.Sync.Panel.Counts.HasPullCount);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        Assert.Equal("Push", model.Sync.Panel.Counts.PushLabel); Assert.False(model.Sync.Panel.Counts.HasPushCount);
    }
    [Fact]
    public async Task WhenTheUserRefreshes_ShouldReadTheRemoteConfigurationAgainWithoutBlankingTheCounts()
    {
        // A remote's Git configuration — its addresses, the SSH command, a proxy, an uploadpack — is not part of the
        // snapshot, so an unchanged snapshot is no reason to keep what the panel read before. The shown counts stay
        // until the new ones arrive, because the watcher's own re-reads must not make them blink.
        var backend = Backend(ahead: 2, behind: 1); await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Single(backend.SyncReads); Assert.Equal("Push · 2", model.Sync.Panel.Counts.PushLabel);
        var counts = model.Sync.Panel.Counts.Text; var blanks = 0;
        model.Sync.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SyncViewModel.Panel) && model.Sync.Panel.Counts.Text.Contains("unknown")) blanks++; };
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count); Assert.Equal(0, blanks);
        Assert.Equal(counts, model.Sync.Panel.Counts.Text); Assert.True(model.Sync.PushCommand.CanExecute(null));
        // A changed address moves the snapshot itself, and that read replaces the target rather than confirming it.
        backend.Snapshot = Snapshot(true, "https://publish.example.invalid/moved");
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.Equal(3, backend.SyncReads.Count); Assert.Equal(counts, model.Sync.Panel.Counts.Text);
    }

    // Every write pauses the sync read and its end reads again; the shown counts stay until the new ones arrive, as they
    // do on a refresh.
    [Fact]
    public async Task WhenAWriteEnds_ShouldReadTheSyncStateAgainWithoutBlankingTheCounts()
    {
        var backend = Backend(ahead: 2, behind: 1); await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        var counts = model.Sync.Panel.Counts.Text; var blanks = 0;
        model.Sync.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SyncViewModel.Panel) && model.Sync.Panel.Counts.Text.Contains("unknown")) blanks++; };
        await model.Sync.FetchCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count); Assert.Equal(0, blanks);
        Assert.Equal(counts, model.Sync.Panel.Counts.Text);
    }

    [Fact]
    public async Task WhenTheSettingsDialogCloses_ShouldReadTheSyncStateAgainOneReadAtATime()
    {
        // The workbench reads the sync state again when the Settings dialog closes: an account added or removed there
        // changes what the sync read answers, and nothing about the repository moved to say so.
        var backend = Backend(); await using var model = Create(backend);
        Assert.False(model.Repository.Remotes.CanReread); // no repository yet
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.True(model.Repository.Remotes.CanReread); Assert.Single(backend.SyncReads);
        backend.SyncGate.IsClosed = true;
        model.Repository.Remotes.Reread(); await backend.SyncGate.Arrival(2);
        Assert.False(model.Repository.Remotes.CanReread);
        backend.SyncGate.Open(); await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count); Assert.True(model.Repository.Remotes.CanReread);
    }

    [Fact]
    public async Task WhenASyncWriteIsRefused_ShouldReadAgainSoTheNextAttemptCarriesTheCurrentConfiguration()
    {
        var backend = Backend();
        backend.Result = new(GitWriteOutcome.Rejected, "The remote configuration changed since it was last read. Review the synchronization target and retry.");
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Single(backend.SyncReads);
        await model.Sync.FetchCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.Contains("configuration changed", model.StatusBar.SyncOperationText);
        Assert.True(model.Sync.FetchCommand.CanExecute(null));
    }
    // The repository's remotes record the user's fetch at the clock's time; the line stays through the read that follows the write.
    [Fact]
    public async Task WhenTheRemoteHasBeenFetched_ShouldShowAShortFetchLineThatIsMissingBefore()
    {
        var fetched = new DateTimeOffset(2026, 9, 14, 16, 43, 12, TimeSpan.Zero);
        await using var model = ShellHarness.Compose(Backend(), time: new ManualTime { Now = fetched });
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.Equal("", model.Sync.Panel.Counts.FetchedText); Assert.False(model.Sync.Panel.Counts.HasFetchedText);
        await model.Sync.FetchCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.Panel.Counts.HasFetchedText); Assert.StartsWith("Fetched ", model.Sync.Panel.Counts.FetchedText); Assert.DoesNotContain("session", model.Sync.Panel.Counts.FetchedText);
        var clock = fetched.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(clock, RelativeTime.Fetched(fetched, fetched));
        Assert.Equal(fetched.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), RelativeTime.Fetched(fetched, fetched.AddDays(3)));
    }
    [Fact]
    public async Task WhenTheUserCancelsASyncRead_ShouldDiscardItEvenIfTheReaderStillAnswers()
    {
        var backend = Backend(ahead: 2, behind: 1);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        backend.SyncGate.IsClosed = true;
        model.Repository.Remotes.Reread();
        await backend.SyncGate.Arrival(2);
        model.CancelCommand.Execute(null);
        Assert.True(backend.SyncGate.Token.IsCancellationRequested);
        backend.SyncGate.Open();
        await model.Sync.SyncCompletion;
        Assert.Contains("canceled", model.Sync.Panel.Message);
        Assert.Null(model.Sync.Panel.State);
        Assert.False(model.Sync.IsSyncBusy);
        Assert.False(model.Sync.FetchCommand.CanExecute(null));
        Assert.True(model.Repository.Remotes.CanReread);

        model.Repository.Remotes.Reread();
        await model.Sync.SyncCompletion;
        Assert.Equal("Push · 2", model.Sync.Panel.Counts.PushLabel);
        Assert.True(model.Sync.FetchCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheSyncStateIsReadAgain_ShouldKeepTheCountsAndSendOneRequestUntilTheNewAnswerArrives()
    {
        var backend = Backend(ahead: 2, behind: 1);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        var counts = model.Sync.Panel.Counts;
        backend.SyncGate.IsClosed = true;
        model.Repository.Remotes.Reread();
        await backend.SyncGate.Arrival(2);
        Assert.Same(counts, model.Sync.Panel.Counts);
        Assert.True(model.Sync.IsSyncBusy);
        Assert.False(model.Sync.PushCommand.CanExecute(null));
        model.Repository.Remotes.Reread();
        Assert.Equal(2, backend.SyncReads.Count);
        backend.ReadSync = (_, remote, branch) => State(remote, branch, ahead: 5, behind: 0);
        backend.SyncGate.Open();
        await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.Equal("Push · 5", model.Sync.Panel.Counts.PushLabel);
        Assert.Equal("Pull", model.Sync.Panel.Counts.PullLabel);
        Assert.True(model.Sync.PushCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheSnapshotChangesAndTheUserRefreshesBeforeTheReadEnds_ShouldReadTheSyncStateOnce()
    {
        var backend = Backend();
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        backend.Snapshot = backend.Snapshot with { Head = new string('d', 40) };
        backend.SyncGate.IsClosed = true;
        await model.RefreshCommand.ExecuteAsync(null);
        await backend.SyncGate.Arrival(2);
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.True(model.Sync.IsSyncBusy);
        backend.SyncGate.Open();
        await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count);

        await model.RefreshCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        Assert.Equal(3, backend.SyncReads.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenASyncReadFailsOrIsInvalidated_ShouldRemoveTheOldCountsAndAccounts(bool fail)
    {
        var backend = Backend(accounts: new([new("alice", "Alice")], "alice", "Saved"), ahead: 2);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        backend.ReadSync = (_, _, _) => fail ? throw new InvalidOperationException("internal detail") : null;
        model.Repository.Remotes.Reread();
        await model.Sync.SyncCompletion;
        Assert.Null(model.Sync.Panel.State);
        Assert.Empty(model.Sync.Panel.Accounts.Items);
        Assert.Contains("unknown", model.Sync.Panel.Counts.Text);
        Assert.Contains(fail ? "could not be read" : "Repository changed", model.Sync.Panel.Message);
        Assert.DoesNotContain("internal detail", model.Sync.Panel.Message);
        Assert.False(model.Sync.FetchCommand.CanExecute(null));
        Assert.False(model.Sync.SaveSyncAccountCommand.CanExecute(null));
        Assert.False(model.Sync.IsSyncBusy);
    }

    [Fact]
    public async Task WhenAnOlderReadFailsLate_ShouldKeepTheNewerTargetAndItsMessage()
    {
        var backend = Backend(ahead: 2);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        backend.ReadSync = (_, remote, branch) => remote == "backup"
            ? throw new InvalidOperationException("old failure") : State(remote, branch, ahead: 2);
        backend.SyncGate.IsClosed = true;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[1];
        var oldRead = model.Sync.SyncCompletion;
        await backend.SyncGate.Arrival(2);
        backend.SyncGate.IsClosed = false;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[0];
        await model.Sync.SyncCompletion;
        var current = model.Sync.Panel;
        backend.SyncGate.Open();
        await oldRead;
        Assert.Same(current, model.Sync.Panel);
        Assert.DoesNotContain("failure", model.Sync.Panel.Message);
        Assert.False(model.Sync.IsSyncBusy);
    }

    [Fact]
    public async Task WhenTheTrackedRemoteIsRemoved_ShouldDropItsBranchWithoutReadingAnything()
    {
        var backend = Backend();
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await RemoveTheTrackedRemoteAsync(model, backend);
        Assert.Null(model.Sync.Selection.Remote);
        Assert.Empty(model.Sync.Selection.Branches);
        Assert.Null(model.Sync.Panel.State);
        Assert.Single(backend.SyncReads);
    }

    [Fact]
    public async Task WhenTheRepositoryClosesWhileTheRemainingRemoteIsRead_ShouldNotLetTheReadRestoreTheSyncState()
    {
        var backend = Backend();
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await RemoveTheTrackedRemoteAsync(model, backend);

        backend.SyncGate.IsClosed = true;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[0];
        var read = model.Sync.SyncCompletion;
        await backend.SyncGate.Arrival(2);
        await model.CloseRepositoryCommand.ExecuteAsync(null);
        backend.SyncGate.Open();
        await read;
        Assert.Empty(model.Sync.Selection.RemoteNames);
        Assert.Empty(model.Sync.Selection.Branches);
        Assert.Null(model.Sync.Panel.State);
        Assert.False(model.Sync.IsSyncBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheAccountChoiceIsUnsaved_ShouldRefuseSyncCommandsRunDirectlyForExistingAndNewBranches(bool isNew)
    {
        var backend = Backend();
        backend.ReadSync = (_, remote, _) =>
        {
            var branch = new SyncBranch("refs/heads/trunk", "refs/remotes/" + remote + "/trunk") { IsNew = isNew };
            return State(remote, branch.SourceRef) with
            {
                Branch = branch, Branches = [branch],
                Accounts = new([new("alice", "Alice"), new("bob", "Bob")], "alice", "Saved")
            };
        };
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Sync.Panel.Accounts.Selected = model.Sync.Panel.Accounts.Items[1];
        Assert.False(model.Sync.PushCommand.CanExecute(null));
        await model.Sync.PushCommand.ExecuteAsync(null);
        await model.Sync.FetchCommand.ExecuteAsync(null);
        await model.Sync.PullCommand.ExecuteAsync(null);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task WhenARefreshReplacesTheSelectionAndAccounts_ShouldIgnoreChangesToTheOldOnes()
    {
        var backend = Backend(accounts: new([new("alice", "Alice"), new("bob", "Bob")], "alice", "Saved"));
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        var oldSelection = model.Sync.Selection;
        var oldAccounts = model.Sync.Panel.Accounts;
        await model.RefreshCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        var reads = backend.SyncReads.Count;
        var notifications = 0;
        model.Sync.FetchCommand.CanExecuteChanged += (_, _) => notifications++;
        oldSelection.RemoteName = oldSelection.RemoteNames[1];
        oldAccounts.Selected = oldAccounts.Items[1];
        Assert.Equal(reads, backend.SyncReads.Count);
        Assert.Equal(0, notifications);
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name);
        Assert.True(model.Sync.FetchCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenASnapshotRestoresTheTrackingRemoteTheUserCleared_ShouldReadThatTargetAgain()
    {
        var backend = Backend(ahead: 2);
        await using var model = Create(backend);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Sync.Selection.RemoteName = null;
        Assert.Null(model.Sync.Panel.State);
        model.Repository.Apply(backend.Snapshot);
        await model.Sync.SyncCompletion;
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name);
        Assert.Equal("origin", model.Sync.Panel.State!.Remote.Name);
        Assert.Equal("Push · 2", model.Sync.Panel.Counts.PushLabel);
    }

    private static ShellViewModel Create(FakeSyncWorkspace backend) => ShellHarness.Compose(backend);

    // The user chooses the other remote, and while it is read switches back to origin, whose read overtakes it.
    private static async Task DiscardALateAnswerAsync(ShellViewModel model, FakeSyncWorkspace backend)
    {
        backend.SyncGate.IsClosed = true;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[1]; var pending = model.Sync.SyncCompletion; await backend.SyncGate.Arrival(2);
        backend.SyncGate.IsClosed = false;
        model.Sync.Selection.RemoteName = model.Sync.Selection.RemoteNames[0]; await model.Sync.SyncCompletion;
        backend.SyncGate.Open(); await pending;
    }

    // Origin, the remote trunk tracks, disappears from the configuration and the user refreshes.
    private static async Task RemoveTheTrackedRemoteAsync(ShellViewModel model, FakeSyncWorkspace backend)
    {
        backend.Snapshot = backend.Snapshot with { Remotes = [backend.Snapshot.Remotes[1]] };
        await model.RefreshCommand.ExecuteAsync(null);
    }

    // Two remotes, "trunk" on the first one; the second has nothing to do with the branch, so choosing it has to
    // leave the counts unknown.
    private static RepositorySnapshot Snapshot(bool tracking, string push = "https://publish.example.invalid/repo") =>
        new("fixture", "2.43.0", "trunk", Snapshots.HeadId, tracking ? "origin/trunk" : null, tracking ? "origin" : null,
            [new("origin", "https://fetch.example.invalid/repo", push), new("backup", "backup-fetch", "backup-push")], RepositoryOperation.None, [], [])
        { UpstreamBranchRef = tracking ? "refs/heads/trunk" : null };

    private static FakeSyncWorkspace Backend(bool tracking = true, int ahead = 0, int behind = 0, RemoteAccountState? accounts = null)
    {
        var backend = new FakeSyncWorkspace { Snapshot = Snapshot(tracking), Result = new(GitWriteOutcome.Succeeded, "Synced") };
        backend.ReadSync = (expected, remote, branch) =>
            State(remote, branch ?? (expected.TrackingRemote == remote ? expected.UpstreamBranchRef : null), ahead, behind) with { Accounts = accounts };
        // Choosing an account is the one write that changes what the next sync read reports.
        backend.Handle = request =>
        {
            if (request is SyncRequest { Action: SyncAction.SelectAccount } select && accounts is not null) accounts = accounts with { SelectedId = select.AccountId };
            return backend.Result;
        };
        return backend;
    }

    public static RepositorySyncState State(string remote, string? branch, int ahead = 0, int behind = 0)
    {
        var item = new SyncBranch("refs/heads/trunk", "refs/remotes/" + remote + "/trunk");
        return new(new(remote, "https://fetch.example.invalid/" + remote, "https://publish.example.invalid/" + remote, "config"), [item], branch is null ? null : item,
            branch is null ? null : ahead, branch is null ? null : behind, "Local comparison");
    }
}
