using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Sync;

// The repository's remotes over the real workspace and fake Git: when the target is read, what a choice keeps, and what
// each write asks Git for.
public sealed class RemotesModelTests
{
    private static RepositorySnapshot Tracking() => Snapshots.Create(branch: "trunk", trackingRemote: "origin", upstreamBranchRef: "refs/heads/trunk",
        remotes: [new("origin", "fetch", "push"), new("backup", "backup-fetch", "backup-push")]) with { RemoteRefs = "r1" };

    // A snapshot that changed only files moved no ref and left the target as it was: the target is not read again.
    [Fact]
    public async Task WhenOnlyFilesChanged_ShouldNotReadTheTargetAgain()
    {
        var sync = new FakeGitSync();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader { Snapshot = Tracking() }, synchronizer: sync);
        var repository = new Repository(Services(workspace));
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        repository.Apply(opened); await repository.Remotes.Completion;
        repository.Apply(opened with { Files = [Snapshots.Modified("a.txt")] }); await repository.Remotes.Completion;
        Assert.Single(sync.Reads);
    }

    // The snapshot that read the target made the next refresh redundant; the refresh after that reads again.
    [Fact]
    public async Task WhenARefreshFollowsTheOneThatWasSkipped_ShouldReadTheTargetAgain()
    {
        var (workspace, repository, reader, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        reader.Snapshot = Tracking() with { RemoteRefs = "r2" };
        await RefreshAsync(workspace, repository);

        await RefreshAsync(workspace, repository);

        Assert.Equal(3, sync.Reads.Count);
    }

    // The read of the target also reads the accounts: a settings file that cannot be read is named, instead of sending
    // the user to the remote's configuration.
    [Fact]
    public async Task WhenTheAccountSettingsCannotBeRead_ShouldSaySoInsteadOfBlamingTheRemoteConfiguration()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        sync.State = (_, _, _) => throw new AccountException(AccountFailureText.SettingsNotRead);

        repository.Remotes.Reread(); await repository.Remotes.Completion;

        Assert.Equal(AccountFailureText.SettingsNotRead, repository.Remotes.Overview.Message);
    }

    // Git keeps no record of a fetch that brought nothing; the remotes remember when it was, and a later read shows it.
    [Fact]
    public async Task WhenTheTargetIsReadAgainAfterAFetch_ShouldStillSayWhenItWasFetched()
    {
        var writer = new FakeGitWriter { Result = new(GitWriteOutcome.Succeeded, "Fetched.") };
        var (workspace, repository, _, _) = await OpenTrackingAsync(writer);
        await using var _ = workspace;
        await repository.Remotes.FetchAsync(); await repository.Remotes.Completion;

        repository.Remotes.Reread(); await repository.Remotes.Completion;

        Assert.True(repository.Remotes.Overview.Counts.HasFetchedText);
    }

    // Another account chosen changes what Save and the sync commands may do: the commands are asked again, once.
    [Fact]
    public async Task WhenAnotherAccountIsChosen_ShouldAskTheCommandsAgainOnce()
    {
        var (workspace, repository, _, _) = await OpenTrackingAsync();
        await using var _ = workspace;
        var asked = 0;
        repository.Requery.Suggested += () => asked++;

        repository.Remotes.Overview.Accounts.Selected = new("account-2", "bob");

        Assert.Equal(1, asked);
    }

    // The sync panel follows the choice at once, before the read of the new target answers.
    [Fact]
    public async Task WhenTheUserChoosesAnotherRemote_ShouldSayTheChoiceChangedWithTheNewTarget()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        string? seen = null;
        remotes.ChoiceChanged += () => seen ??= remotes.Target.RemoteName;
        sync.ReadGate.IsClosed = true;

        remotes.Choose("backup", null);

        Assert.Equal("backup", seen);
        sync.ReadGate.Open(); await remotes.Completion;
    }

    // A target without a remote reads nothing, and the read of the target before it no longer speaks for the panel.
    [Fact]
    public async Task WhenTheChosenTargetHasNoRemoteWhileTheLastOneIsRead_ShouldNotShowThatRead()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        sync.ReadGate.IsClosed = true;
        remotes.Choose("backup", null);

        remotes.Choose(null, null);
        sync.ReadGate.Open(); await remotes.Completion;

        Assert.Null(remotes.Overview.State);
    }

    // The application goes: the panel empties, the target goes, and the status bar keeps nothing of the last sync.
    [Fact]
    public async Task WhenTheApplicationGoes_ShouldEmptyThePanelTheTargetAndTheSyncTexts()
    {
        var bar = new StatusBarState();
        var (workspace, repository, _, _) = await OpenTrackingAsync(bar: bar);
        await using var _ = workspace;
        var remotes = repository.Remotes;
        (bar.SyncOperationText, bar.SyncDetails) = ("Fetched.", "git fetch");
        var followed = 0;
        remotes.TargetFollowed += () => followed++;

        remotes.Clear();

        Assert.Equal(("", ""), (bar.SyncOperationText, bar.SyncDetails));
        Assert.Null(remotes.Overview.State); Assert.Null(remotes.Target.Remote); Assert.Equal(1, followed);
    }

    [Fact]
    public async Task WhenARepositoryOpens_ShouldReadItsTrackingRemoteWithTheUpstreamChosen()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        Assert.Equal(("origin", null), Assert.Single(sync.Reads));
        Assert.Equal("refs/heads/trunk", remotes.Upstream.Chosen!.SourceRef); Assert.True(remotes.CanPush);
    }

    // Git configuration can change without moving the snapshot, so a refresh the user asked for confirms it once.
    [Fact]
    public async Task WhenTheUserRefreshesAndNothingChanged_ShouldReadTheTargetOnce()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        await RefreshAsync(workspace, repository); Assert.Equal(2, sync.Reads.Count);
    }

    // The snapshot that moved the remote-tracking ref reads the same target again, and the explicit refresh that brought
    // it does not read a second time. The refresh before it confirmed the configuration.
    [Fact]
    public async Task WhenARefreshMovedARemoteRef_ShouldReadTheTargetOnlyOnce()
    {
        var (workspace, repository, reader, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        await RefreshAsync(workspace, repository);
        reader.Snapshot = Tracking() with { RemoteRefs = "r2" };
        await RefreshAsync(workspace, repository); Assert.Equal(3, sync.Reads.Count);
    }

    // A new target starts over with one read, and the explicit refresh that brought it does not read a second time. The
    // refreshes before it confirmed the configuration and moved a remote ref.
    [Fact]
    public async Task WhenARefreshMovedTheHead_ShouldReadTheNewTargetOnlyOnce()
    {
        var (workspace, repository, reader, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        await RefreshAsync(workspace, repository);
        reader.Snapshot = Tracking() with { RemoteRefs = "r2" };
        await RefreshAsync(workspace, repository);
        reader.Snapshot = Tracking() with { Head = Snapshots.Id('c') };
        await RefreshAsync(workspace, repository); Assert.Equal(4, sync.Reads.Count);
    }

    [Fact]
    public async Task WhenTheUserChoosesTheSameRemote_ShouldReadItWithTheUpstreamTheUserChose()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        var upstream = remotes.Upstream.Chosen;
        remotes.Choose("origin", upstream); await remotes.Completion;
        Assert.Equal(("origin", "refs/heads/trunk"), sync.Reads[^1]);
    }

    [Fact]
    public async Task WhenTheUserChoosesAnotherRemote_ShouldReadItWithoutAnUpstream()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        var upstream = remotes.Upstream.Chosen;
        remotes.Choose("backup", upstream); await remotes.Completion;
        Assert.Equal(("backup", null), sync.Reads[^1]); Assert.Equal("backup", remotes.Target.RemoteName);
    }

    [Fact]
    public async Task WhenNoTargetWasReadYet_ShouldNotFetch()
    {
        var writer = new FakeGitWriter();
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader { Snapshot = Tracking() }, writer: writer, synchronizer: new FakeGitSync());
        var remotes = new Repository(Services(workspace)).Remotes;
        Assert.Null(await remotes.FetchAsync()); Assert.Empty(writer.Requests);
    }

    // A remote outside the target, as one just added, is fetched by its own state, read for it.
    [Fact]
    public async Task WhenAnotherRemoteIsFetched_ShouldReadItsStateAndFetchItByThatState()
    {
        var writer = new FakeGitWriter();
        var (workspace, repository, _, sync) = await OpenTrackingAsync(writer);
        await using var _ = workspace;

        await repository.Remotes.FetchRemoteAsync("backup");

        Assert.Contains(("backup", (string?)null), sync.Reads);
        var request = Assert.IsType<SyncRequest>(Assert.Single(writer.Requests));
        Assert.Equal((SyncAction.Fetch, "backup"), (request.Action, request.Sync.Remote.Name));
    }

    // A remote Trag cannot exchange with is not fetched.
    [Fact]
    public async Task WhenTheOtherRemoteCannotBeSynchronized_ShouldNotFetchIt()
    {
        var writer = new FakeGitWriter();
        var (workspace, repository, _, sync) = await OpenTrackingAsync(writer);
        await using var _ = workspace;
        sync.State = (snapshot, remote, branch) =>
        {
            var state = FakeGitSync.DefaultState(snapshot, remote, branch);
            return state with { Remote = state.Remote with { Limitation = "Not supported" } };
        };

        Assert.Null(await repository.Remotes.FetchRemoteAsync("backup"));
        Assert.Empty(writer.Requests);
    }

    [Fact]
    public async Task WhenTheUserFetchesAndPushes_ShouldSendBothToTheTargetThatWasRead()
    {
        var writer = new FakeGitWriter();
        var (workspace, repository, _, _) = await OpenTrackingAsync(writer);
        await using var _ = workspace;
        var remotes = repository.Remotes;

        await remotes.FetchAsync();
        await remotes.PushAsync();

        Assert.Equal([SyncAction.Fetch, SyncAction.Push], writer.Requests.Cast<SyncRequest>().Select(request => request.Action));
        Assert.All(writer.Requests.Cast<SyncRequest>(), request => Assert.Equal("origin", request.Sync.Remote.Name));
    }

    // Publishing also records the branch's upstream.
    [Fact]
    public async Task WhenTheRemoteDoesNotHaveTheBranchYet_ShouldPublishItInsteadOfPushing()
    {
        var writer = new FakeGitWriter();
        var (workspace, repository, _, sync) = await OpenTrackingAsync(writer);
        await using var _ = workspace;
        var remotes = repository.Remotes;
        sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) is var state && state.Branch is { } chosen
            ? state with { Branch = chosen with { IsNew = true }, Branches = [chosen with { IsNew = true }] } : state;
        remotes.Reread(); await remotes.Completion;

        await remotes.PushAsync();

        var publish = Assert.IsType<PublishRequest>(writer.Requests[^1]);
        Assert.Equal((PublishKind.ExistingRemote, "origin", "trunk"), (publish.Publish.Kind, publish.Publish.RemoteName, publish.Publish.Branch));
    }

    [Fact]
    public async Task WhenNothingIsOpen_ShouldNotFetchQuietly()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader { Snapshot = Tracking() }, writer: new FakeGitWriter(), synchronizer: new FakeGitSync());
        var remotes = new Repository(Services(workspace)).Remotes;
        Assert.Null(await remotes.FetchQuietlyAsync(CancellationToken.None));
    }

    // The fetch nobody waits for.
    [Fact]
    public async Task WhenTheRemoteHasTheUpstream_ShouldFetchQuietly()
    {
        var (workspace, repository, _, _) = await OpenTrackingAsync(new FakeGitWriter());
        await using var _ = workspace;
        var remotes = repository.Remotes;
        Assert.Equal("Synced", (await remotes.FetchQuietlyAsync(CancellationToken.None))!.Message);
    }

    [Fact]
    public async Task WhenTheRemoteDoesNotHaveTheUpstreamYet_ShouldNotFetchQuietly()
    {
        var (workspace, repository, _, sync) = await OpenTrackingAsync(new FakeGitWriter());
        await using var _ = workspace;
        var remotes = repository.Remotes;
        sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) is var state && state.Branch is { } chosen
            ? state with { Branch = chosen with { IsNew = true } } : state;
        remotes.Reread(); await remotes.Completion;
        Assert.False(remotes.CanFetchQuietly);
    }

    [Fact]
    public async Task WhenAnOperationIsUnderway_ShouldNotFetchQuietly()
    {
        var (workspace, repository, reader, _) = await OpenTrackingAsync(new FakeGitWriter());
        await using var _ = workspace;
        var remotes = repository.Remotes;
        Assert.True(remotes.CanFetchQuietly);
        reader.Snapshot = Tracking() with { Operation = RepositoryOperation.Merge, Merge = new("incoming", Snapshots.HeadId, "Merge", "fp", "") };
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        Assert.False(remotes.CanFetchQuietly); Assert.Null(await remotes.FetchQuietlyAsync(CancellationToken.None));
    }

    // What publishing and the force push banner start from: the repository shown, named after its folder, and the target
    // that was read for it.
    [Fact]
    public async Task WhenARepositoryIsShown_ShouldGivePublishItsNameAndTheBannerItsTarget()
    {
        var (workspace, repository, _, _) = await OpenTrackingAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        Assert.Equal(("fixture", repository.Snapshot), (remotes.PublishContext.RepositoryName, remotes.PublishContext.Snapshot));
        Assert.Equal((remotes.Overview.State, repository.Snapshot), (remotes.ForcePush.State, remotes.ForcePush.Snapshot));
    }

    [Fact]
    public async Task WhenNothingIsOpen_ShouldConfirmNoPublish()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: "trunk") },
            writer: new FakeGitWriter(), synchronizer: new FakeGitSync());
        var remotes = new Repository(Services(workspace)).Remotes;
        var input = CreateRepository();
        Assert.Null(remotes.ConfirmPublish(input, "account", "team"));
    }

    // The publish the dialog confirmed is built when the user confirms and runs later as it was built, though the shown
    // snapshot moved on meanwhile.
    [Fact]
    public async Task WhenTheUserConfirmsAPublish_ShouldBuildItOverTheSnapshotShownThenWithTheChosenAccountAndOwner()
    {
        var (workspace, repository, writer) = await OpenOnTrunkAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        var input = CreateRepository();
        var shown = repository.Snapshot!;

        var publish = remotes.ConfirmPublish(input, "account", "team")!;
        repository.Apply(shown with { Branch = "later" });
        await publish.RunAsync();

        var request = Assert.IsType<PublishRequest>(Assert.Single(writer.Requests));
        Assert.Same(shown, request.Expected);
        Assert.Equal((PublishKind.CreateRepository, "trunk", "account", "team", "trag"),
            (request.Publish.Kind, request.Publish.Branch, request.Publish.AccountId, request.Publish.OwnerId, request.Publish.RepositoryName));
    }

    // The dialog says what keeps a publish from being done, and that it did not start when nothing ran.
    [Fact]
    public async Task WhenAPublishFailsBeforeItConfiguredTheRemote_ShouldSayWhy()
    {
        var (workspace, repository, writer) = await OpenOnTrunkAsync();
        await using var _ = workspace;
        writer.Result = GitWriteResult.Rejected("Push failed.");
        var publish = repository.Remotes.ConfirmPublish(CreateRepository(), "account", "team")!;
        Assert.Equal("Push failed.", (await publish.RunAsync())?.Message);
    }

    // A result that configured the remote leaves the dialog nothing to say.
    [Fact]
    public async Task WhenAPublishConfiguredTheRemoteButThePushFailed_ShouldBeDone()
    {
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: "trunk") };
        var writer = new FakeGitWriter { Result = GitWriteResult.Rejected("Push failed.") };
        await using var workspace = new RepositoryWorkspace(reader, writer: writer, synchronizer: new FakeGitSync());
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!);
        var publish = repository.Remotes.ConfirmPublish(CreateRepository(), "account", "team")!;
        writer.Handle = (_, _) =>
        {
            reader.Snapshot = reader.Snapshot with { Remotes = [new("origin", "fetch", "push")] };
            return Task.FromResult(writer.Result);
        };

        Assert.Null(await publish.RunAsync());
    }

    [Fact]
    public async Task WhenNoBranchIsCheckedOut_ShouldConfirmNoPublish()
    {
        var (workspace, repository, _) = await OpenOnTrunkAsync();
        await using var _ = workspace;
        var remotes = repository.Remotes;
        var input = CreateRepository();
        var shown = repository.Snapshot!;
        repository.Apply(shown with { Branch = null });
        Assert.Null(remotes.ConfirmPublish(input, "account", "team"));
    }

    private static RepositoryServices Services(RepositoryWorkspace workspace, StatusBarState? bar = null) =>
        new(workspace, bar ?? new StatusBarState(), new ImmediateViewDispatcher());

    // The tracking repository opened over the real workspace and fake Git, its target read once.
    private static async Task<(RepositoryWorkspace Workspace, Repository Repository, FakeRepositoryReader Reader, FakeGitSync Sync)> OpenTrackingAsync(
        FakeGitWriter? writer = null, StatusBarState? bar = null)
    {
        var sync = new FakeGitSync();
        var reader = new FakeRepositoryReader { Snapshot = Tracking() };
        var workspace = new RepositoryWorkspace(reader, writer: writer, synchronizer: sync);
        var repository = new Repository(Services(workspace, bar));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Remotes.Completion;
        return (workspace, repository, reader, sync);
    }

    // As the session refreshes: the snapshot read again, then the refresh completed.
    private static async Task RefreshAsync(RepositoryWorkspace workspace, Repository repository)
    {
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        repository.CompleteRefresh();
        await repository.Remotes.Completion;
    }

    // A repository on trunk without a remote, opened.
    private static async Task<(RepositoryWorkspace Workspace, Repository Repository, FakeGitWriter Writer)> OpenOnTrunkAsync()
    {
        var writer = new FakeGitWriter();
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: "trunk") };
        var workspace = new RepositoryWorkspace(reader, writer: writer, synchronizer: new FakeGitSync());
        var repository = new Repository(Services(workspace));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Remotes.Completion;
        return (workspace, repository, writer);
    }

    // What the dialog confirms: a new repository on the account's host, pushed to at once.
    private static PublishInput CreateRepository() => new(PublishKind.CreateRepository, "trag", "origin", "", null, null, Push: true);
}
