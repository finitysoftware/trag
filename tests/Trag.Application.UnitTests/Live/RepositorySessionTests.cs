using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Workspace;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class RepositorySessionTests
{
    [Fact]
    public void WhenNothingIsOpen_ShouldInviteAnOpen()
    {
        var (_, _, _, bar, _) = Session();
        Assert.Equal("Ready · Open a local repository to begin", bar.Status);
    }

    // The lists are ready before the snapshot is published, so whatever follows the snapshot finds them.
    [Fact]
    public async Task WhenARepositoryOpens_ShouldShowItInANewRepositoryWithItsListsReadyBeforeItsSnapshot()
    {
        var (session, workspace, _, bar, recorders) = Session();
        var initial = session.Current;
        workspace.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt"), Snapshots.Modified("b.txt"), Snapshots.Modified("c.txt")]);

        await session.OpenRepositoryAsync("fixture");

        var repository = session.Current;
        Assert.NotSame(initial, repository); Assert.False(initial.HasRepository); Assert.True(repository.HasWriter);
        Assert.True(session.HasSession); Assert.False(repository.IsReading);
        Assert.Empty(recorders[0].Shown);
        var shown = Assert.Single(recorders[1].Shown);
        Assert.Equal((false, null, 1, 2), (shown.Delta, shown.Published, shown.Staged, shown.Unstaged));
        Assert.Same(workspace.Snapshot, repository.Snapshot);
        Assert.Equal("1 staged · 2 unstaged", bar.Status);
    }

    [Fact]
    public async Task WhenARepositoryOpens_ShouldRememberAndRecordItsLocation()
    {
        var (session, _, store, _, _) = Session();
        var recorded = new List<string>();
        session.RecordOpened = (snapshot, _) => { recorded.Add(snapshot.Root); return Task.CompletedTask; };

        await session.OpenRepositoryAsync("fixture");

        Assert.Equal(1, store.Saves); Assert.Equal("fixture", Assert.Single(recorded));
    }

    // An open canceled before it showed anything leaves its path to refresh: F5 then opens the repository, as the open
    // would have, and the catalog records it.
    [Fact]
    public async Task WhenARefreshFollowsACanceledOpen_ShouldOpenTheRepositoryAndRecordItInTheCatalog()
    {
        var (session, _, _, _, _) = Session();
        var recorded = new List<string>();
        session.RecordOpened = (snapshot, _) => { recorded.Add(snapshot.Root); return Task.CompletedTask; };
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await session.OpenRepositoryAsync("fixture", canceled.Token);
        var shownAfterCancel = session.Current.HasRepository;

        await session.RefreshAsync();

        Assert.False(shownAfterCancel);
        Assert.True(session.Current.HasRepository);
        Assert.Equal(["fixture"], recorded);
    }

    [Fact]
    public async Task WhenAnOpenFails_ShouldNameTheReasonAndLeaveAnEmptyRepository()
    {
        var (session, workspace, _, bar, recorders) = Session();
        var initial = session.Current;
        workspace.Read = () => throw new RepositoryException(RepositoryError.NotRepository);

        await session.OpenRepositoryAsync("plain");

        Assert.Contains("not a Git repository", bar.ErrorMessage); Assert.Equal("Repository could not be opened", bar.Status);
        Assert.NotSame(initial, session.Current);
        Assert.False(session.Current.HasRepository); Assert.False(session.Current.IsReading); Assert.Empty(recorders[^1].Shown);
    }

    // A failure that is not one of the repository's own reasons is named only as a read that failed.
    [Fact]
    public async Task WhenTheWorkspaceRefusesTheOpenWithAnInvalidOperation_ShouldShowTheGenericReadError()
    {
        var (session, workspace, _, bar, _) = Session();
        workspace.Read = () => throw new InvalidOperationException("fixture detail");

        await session.OpenRepositoryAsync("fixture");

        Assert.Equal(SessionText.Error(null), bar.ErrorMessage); Assert.DoesNotContain("fixture detail", bar.ErrorMessage);
        Assert.Equal("Repository could not be opened", bar.Status);
        Assert.False(session.Current.HasRepository); Assert.False(session.Current.IsReading);
    }

    // The repository a write runs on stays shown until the write ends; an open asked meanwhile does nothing at all.
    [Fact]
    public async Task WhenAnOpenIsAskedWhileAWriteRuns_ShouldDoNothingAndLeaveTheStatusBarAlone()
    {
        var (session, workspace, _, bar, _) = Session();
        workspace.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);
        await session.OpenRepositoryAsync("fixture");
        var repository = session.Current;
        workspace.Gate.IsClosed = true;
        var writing = repository.Writer.RunAsync(snapshot => new StageRequest(snapshot, [Snapshots.Path("a")]));
        await workspace.Gate.Started;
        var status = bar.Status;

        await session.OpenRepositoryAsync("other");

        Assert.Same(repository, session.Current); Assert.True(repository.IsWriting); Assert.False(repository.IsReading);
        Assert.Equal(status, bar.Status); Assert.Equal("", bar.ErrorMessage);
        workspace.Gate.Open();
        await writing;
    }

    // A write whose outcome is unknown still ends: it waits for a review, nothing runs any more, and the watcher resumes
    // for the repository the write belonged to.
    [Fact]
    public async Task WhenTheWriterThrowsAfterGitRan_ShouldMarkForReviewClearRunningAndRestartTheWatcher()
    {
        var (reader, watcher, bar) = (new FakeRepositoryReader { Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]) },
            new FakeRepositoryWatcher(), new StatusBarState());
        var writer = new FakeGitWriter { Failure = new InvalidOperationException("after Git ran") };
        var session = new RepositorySession(new(new RepositoryWorkspace(reader, watcher, writer: writer), bar, new ImmediateViewDispatcher()),
            new FakeLastRepositoryStore(), _ => { }, new CancelRegistry());
        await using var _ = session;
        await session.OpenRepositoryAsync("fixture");
        var repository = session.Current;

        await repository.Writer.RunAsync(snapshot => new StageRequest(snapshot, [Snapshots.Path("a")]));

        Assert.True(repository.NeedsReview); Assert.Null(repository.Writer.Running); Assert.False(repository.IsWriting);
        Assert.Equal("Watching for changes", session.Watcher.Status);
    }

    // An open has nothing to keep showing.
    [Fact]
    public async Task WhenAnOpenIsCanceled_ShouldSayItCanBeRetried()
    {
        var (session, workspace, _, bar, _) = Session();
        workspace.Read = () => throw new OperationCanceledException();

        await session.OpenRepositoryAsync("fixture");

        Assert.Equal("Opening canceled · choose Refresh to retry", bar.Status);
    }

    // An open that finishes after the session moved on releases its cancellation, so Cancel and Stop reach nothing
    // disposed and the next open still works.
    [Fact]
    public async Task WhenASupersededOpenFinishes_ShouldLeaveNothingDisposedForCancelOrTheNextOpen()
    {
        var reader = new FakeRepositoryReader(); reader.Gate.IsClosed = true;
        await using var workspace = new RepositoryWorkspace(reader);
        var cancels = new CancelRegistry();
        var session = new RepositorySession(new(workspace, new StatusBarState(), new ImmediateViewDispatcher()), new FakeLastRepositoryStore(), _ => { }, cancels);
        var opening = session.OpenRepositoryAsync("old");
        await reader.Gate.Started;
        var next = session.OpenRepositoryAsync("next");
        reader.Gate.Open();
        await opening; await next;
        cancels.CancelAll(); session.Current.Cancels.CancelAll();
        session.Stop();
        await session.OpenRepositoryAsync("current");
        Assert.True(session.Current.HasRepository); Assert.False(session.Current.IsBusy);
    }

    // The first open's read ends canceled, as the workspace's does once another open replaced it, while the second one
    // still reads: what the first leaves behind is not the second's.
    [Fact]
    public async Task WhenAReplacedOpenEndsWhileTheNextOneReads_ShouldLeaveTheNextOneReadingAndSayingSo()
    {
        var (session, workspace, _, bar, _) = Session();
        var (first, second) = (Held(), Held());
        var reads = new Queue<TaskCompletionSource>([first, second]);
        workspace.Reading = _ => reads.Dequeue().Task;
        var opening = session.OpenRepositoryAsync("first");
        var next = session.OpenRepositoryAsync("second");
        var reading = bar.Status;

        first.SetCanceled();
        await opening;

        Assert.True(session.Current.IsReading); Assert.Equal(reading, bar.Status);
        second.SetResult();
        await next;
    }

    // The first open's read answered just before the second open replaced it, so the workspace still hands its snapshot
    // back.
    [Fact]
    public async Task WhenAnOpenIsReplacedAfterItsReadAnswered_ShouldShowNothingOfIt()
    {
        var (session, workspace, _, _, recorders) = Session();
        var (first, second) = (Held(), Held());
        var reads = new Queue<TaskCompletionSource>([first, second]);
        workspace.Reading = _ => reads.Dequeue().Task;
        var opening = session.OpenRepositoryAsync("first");
        var next = session.OpenRepositoryAsync("second");

        first.SetResult();
        await opening;

        Assert.False(session.Current.HasRepository); Assert.Empty(recorders[^1].Shown);
        second.SetResult();
        await next;
    }

    [Fact]
    public async Task WhenAnOpenIsReplacedWhileItIsRemembered_ShouldNotRecordIt()
    {
        var (session, workspace, store, _, _) = Session();
        var recorded = 0;
        session.RecordOpened = (_, _) => { recorded++; return Task.CompletedTask; };
        var (entered, saving) = (Held(), Held());
        store.Saving = () => { entered.SetResult(); return saving.Task; };
        var opening = session.OpenRepositoryAsync("first");
        await entered.Task;
        store.Saving = null;
        var held = Held();
        workspace.Reading = _ => held.Task;
        var next = session.OpenRepositoryAsync("second");

        saving.SetResult();
        await opening;

        Assert.Equal(0, recorded);
        held.SetResult();
        await next;
    }

    // The repository list records the first open only once the second one began; that record failing is no news about
    // the second.
    [Fact]
    public async Task WhenAReplacedOpenFailsWhileTheNextOneReads_ShouldShowNoError()
    {
        var (session, workspace, _, bar, _) = Session();
        var (entered, recording) = (Held(), Held());
        session.RecordOpened = (_, _) => { entered.SetResult(); return recording.Task; };
        var opening = session.OpenRepositoryAsync("first");
        await entered.Task;
        var held = Held();
        workspace.Reading = _ => held.Task;
        var next = session.OpenRepositoryAsync("second");
        var reading = bar.Status;

        recording.SetException(new IOException("catalog"));
        await opening;

        Assert.Empty(bar.ErrorMessage); Assert.Equal(reading, bar.Status);
        held.SetResult();
        await next;
    }

    [Fact]
    public async Task WhenAReplacedOpenCannotBeRememberedWhileTheNextOneReads_ShouldShowNoNotice()
    {
        var (session, workspace, store, bar, _) = Session();
        var (entered, saving) = (Held(), Held());
        store.Saving = () => { entered.SetResult(); return saving.Task; };
        var opening = session.OpenRepositoryAsync("first");
        await entered.Task;
        store.Saving = null;
        var held = Held();
        workspace.Reading = _ => held.Task;
        var next = session.OpenRepositoryAsync("second");

        store.SaveFailure = new IOException("settings");
        saving.SetResult();
        await opening;

        Assert.False(bar.HasNotice);
        held.SetResult();
        await next;
    }

    // Esc while the last repository is restored at startup opens nothing and says the open was canceled, with no notice;
    // Refresh then opens it, as an open would.
    [Fact]
    public async Task WhenTheStartupRestoreIsCanceledByEsc_ShouldOpenNothingAndOfferRefresh()
    {
        var (session, _, store, bar, _) = Session();
        store.Path = "fixture";
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await session.RestoreAsync(canceled.Token);

        Assert.False(session.Current.HasRepository);
        Assert.Equal((SessionText.Canceled(false), ""), (bar.Status, bar.Notice));
        await session.RefreshAsync();
        Assert.True(session.Current.HasRepository);
    }

    // A last-repository file that cannot be read restores nothing and says so; with nothing open the shell shows the picker.
    [Fact]
    public async Task WhenTheLastRepositoryFileIsCorrupt_ShouldSayNotRestoredAndOpenNothing()
    {
        var (session, _, store, bar, _) = Session();
        store.Load = _ => Task.FromException<string?>(new System.Text.Json.JsonException("corrupt"));

        await session.RestoreAsync(CancellationToken.None);

        Assert.Equal(SessionText.NotRestored, bar.Notice);
        Assert.False(session.HasSession);
    }

    [Fact]
    public async Task WhenTheStartupRestoreFailsAfterTheUserOpenedARepository_ShouldShowNoNotice()
    {
        var (session, _, store, bar, _) = Session();
        var loading = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Load = _ => loading.Task;
        var restoring = session.RestoreAsync(CancellationToken.None);
        await session.OpenRepositoryAsync("chosen");

        loading.SetException(new IOException("settings"));
        await restoring;

        Assert.False(bar.HasNotice);
    }

    [Fact]
    public async Task WhenARefreshSucceeds_ShouldKeepTheRepositoryAndEditItsShownLists()
    {
        var (session, workspace, _, bar, recorders) = Session();
        await session.OpenRepositoryAsync("fixture");
        var (repository, recorder) = (session.Current, recorders[^1]);
        var opened = repository.Snapshot;
        workspace.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);

        await session.RefreshAsync();

        Assert.Equal(2, recorder.Shown.Count); Assert.True(recorder.Shown[1].Delta); Assert.Same(opened, recorder.Shown[1].Published);
        Assert.Equal((0, 1), (recorder.Shown[1].Staged, recorder.Shown[1].Unstaged));
        Assert.Same(repository, session.Current); Assert.Equal(2, recorders.Count);
        Assert.Same(workspace.Snapshot, repository.Snapshot); Assert.Equal("0 staged · 1 unstaged", bar.Status);
    }

    // The refresh read the repository, which answers whatever the last write left unverified.
    [Fact]
    public async Task WhenARefreshSucceeds_ShouldClearWhatTheLastWriteLeftOpen()
    {
        var (session, _, _, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        var repository = session.Current;
        repository.NeedsReview = true; bar.WriteDetails = "details";

        await session.RefreshAsync();

        Assert.False(repository.NeedsReview); Assert.Equal("", bar.WriteDetails);
    }

    // The banner names no detail of the failure, which may carry what the user should not see.
    [Fact]
    public async Task WhenARefreshFails_ShouldKeepTheRepositoryAndShowNoDetailOfTheFailure()
    {
        var (session, workspace, _, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        workspace.Read = () => throw new IOException("credential=private fixture detail");
        await session.RefreshAsync();
        Assert.True(session.Current.HasRepository); Assert.False(session.Current.IsBusy);
        Assert.True(bar.HasError); Assert.Contains("previous status", bar.Status); Assert.DoesNotContain("private", bar.ErrorMessage);
    }

    [Fact]
    public async Task WhenARefreshIsCanceled_ShouldKeepThePreviousStatusShown()
    {
        var (session, workspace, _, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        workspace.Read = () => throw new OperationCanceledException();

        await session.RefreshAsync();

        Assert.Equal("Refresh canceled · showing the previous status", bar.Status); Assert.True(session.Current.HasRepository);
    }

    // A read the workspace drops for a newer one, as the watcher's read begun as F5 stopped it, answers no snapshot: the
    // refresh ends as one that was stopped, and the status line no longer says it is reading.
    [Fact]
    public async Task WhenTheLatestLoadReturnsNoSnapshot_ShouldNotLeaveRefreshingOnTheStatusLine()
    {
        var (session, workspace, _, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        workspace.Read = () => null!;

        await session.RefreshAsync();

        Assert.Equal(SessionText.Canceled(true), bar.Status);
        Assert.True(session.Current.HasRepository); Assert.False(session.Current.IsReading);
    }

    // The open remembered the location; a refresh reads the same repository and has nothing to remember or to say about it.
    [Fact]
    public async Task WhenARefreshFollowsTheOpen_ShouldNotSaveTheLocationAgainNorSayTheRepositoryWasOpened()
    {
        var (session, _, store, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        store.SaveFailure = new IOException("settings");

        await session.RefreshAsync();

        Assert.Equal(1, store.Saves); Assert.False(bar.HasNotice);
    }

    // Esc that reaches an open after its repository is shown stops only the remembering: the open stands and its count stays.
    [Fact]
    public async Task WhenEscapeHitsTheSaveAfterTheRepositoryIsShown_ShouldNotSayRefreshCanceledButThatTheLocationWasNotRemembered()
    {
        var (session, _, store, bar, _) = Session();
        using var escape = new CancellationTokenSource();
        store.Saving = () => { escape.Cancel(); return Task.FromCanceled(escape.Token); };

        await session.OpenRepositoryAsync("fixture", escape.Token);

        Assert.True(session.Current.HasRepository);
        Assert.Equal((StatusText.Line(session.Current.Snapshot!), SessionText.NotRemembered), (bar.Status, bar.Notice));
    }

    // A write's result stays readable on the status line until the count it would replace changes.
    [Fact]
    public async Task WhenALiveUpdateKeepsTheCount_ShouldLeaveTheStatusLineAlone()
    {
        var (session, bar) = await OpenWithOneChangeAsync();
        await session.UpdateSnapshotAsync(Snapshots.Create(files: [Snapshots.Modified("a")]), CancellationToken.None);
        Assert.Equal("a is resolved.", bar.Status);
    }

    [Fact]
    public async Task WhenALiveUpdateChangesTheCount_ShouldCountAgain()
    {
        var (session, bar) = await OpenWithOneChangeAsync();
        await session.UpdateSnapshotAsync(Snapshots.Create(files: [Snapshots.Modified("a"), Snapshots.Modified("b")]), CancellationToken.None);
        Assert.Equal("0 staged · 2 unstaged", bar.Status);
    }

    [Fact]
    public async Task WhenTheUserRefreshes_ShouldCountAgainThoughTheCountIsTheSame()
    {
        var (session, bar) = await OpenWithOneChangeAsync();
        await session.RefreshAsync();
        Assert.Equal("0 staged · 1 unstaged", bar.Status);
    }

    // The application goes while a refresh reads: the refresh ends quietly and does not resume the watcher.
    [Fact]
    public async Task WhenTheSessionStopsDuringARefresh_ShouldLeaveTheWatcherStopped()
    {
        var (session, reader, _, _) = await WatchedAsync();
        reader.Gate.IsClosed = true;
        var refreshing = session.RefreshAsync();
        await reader.Gate.Arrival(2);

        session.Stop();
        reader.Gate.Open();
        await refreshing;

        Assert.Empty(session.Watcher.Status);
    }

    // The watcher's read hangs, as a Git stuck on a network drive does: the user's refresh stops that read before it reads
    // the repository again.
    [Fact]
    public async Task WhenTheUserRefreshesWhileTheWatcherReadHangs_ShouldStopThatReadBeforeReadingAgain()
    {
        var (session, reader, watcher, _) = await WatchedAsync();
        await using var _ = session;
        reader.Gate.IsClosed = true;
        watcher.Latest.Push("a.txt");
        await reader.Gate.Arrival(2);

        var refreshing = session.RefreshAsync();
        await reader.Gate.Arrival(3);
        var stopped = reader.Gate.TokenAt(2).IsCancellationRequested;
        reader.Gate.Open();
        await refreshing;

        Assert.True(stopped);
        Assert.True(session.Current.HasRepository);
    }

    [Fact]
    public async Task WhenTheWatcherCannotReadAChange_ShouldShowANotice()
    {
        var (session, reader, watcher, bar) = await WatchedAsync();
        await using var _ = session;
        reader.Failure = new IOException("index.lock");
        watcher.Latest.Push("a.txt");
        await Until(() => bar.HasNotice);
        Assert.Contains("could not be checked", bar.Notice); Assert.Contains("retrying", session.Watcher.Status); Assert.True(session.Current.HasRepository);
    }

    [Fact]
    public async Task WhenTheWatcherReadsAGoodChangeAfterAFailure_ShouldTakeTheNoticeDown()
    {
        var (session, reader, watcher, bar) = await WatchedAsync();
        await using var _ = session;
        reader.Failure = new IOException("index.lock");
        watcher.Latest.Push("a.txt");
        await Until(() => bar.HasNotice);
        reader.Failure = null;
        watcher.Latest.Push("a.txt");
        await Until(() => !bar.HasNotice);
        Assert.Equal("Watching for changes", session.Watcher.Status);
    }

    [Fact]
    public async Task WhenTheRepositoryCloses_ShouldStartAnEmptyOneAndSaySo()
    {
        var (session, _, _, bar, recorders) = Session();
        await session.OpenRepositoryAsync("fixture");
        var opened = session.Current;
        var closed = 0; session.Closed += () => closed++;

        await session.CloseRepositoryAsync();

        Assert.False(session.HasSession); Assert.NotSame(opened, session.Current); Assert.False(session.Current.HasRepository);
        Assert.Equal(3, recorders.Count); Assert.Equal(1, closed);
        Assert.Equal("Repository closed", bar.Status);
    }

    [Fact]
    public async Task WhenTheRepositoryCloses_ShouldForgetItsLocation()
    {
        var (session, _, store, _, _) = Session();
        await session.OpenRepositoryAsync("fixture");

        await session.CloseRepositoryAsync();

        Assert.Null(store.Path);
    }

    // What the status bar said about the repository, its last write's report included, goes with it.
    [Fact]
    public async Task WhenForgettingAClosedRepositoryFailsAfterTheNextOneBegan_ShouldShowNoNotice()
    {
        var (session, workspace, store, bar, _) = Session();
        await session.OpenRepositoryAsync("first");
        var (entered, saving) = (Held(), Held());
        store.Saving = () => { entered.SetResult(); return saving.Task; };
        var closing = session.CloseRepositoryAsync();
        await entered.Task;
        store.Saving = null;
        var held = Held();
        workspace.Reading = _ => held.Task;
        var next = session.OpenRepositoryAsync("second");

        store.SaveFailure = new IOException("settings");
        saving.SetResult();
        await closing;

        Assert.False(bar.HasNotice);
        held.SetResult();
        await next;
    }

    [Fact]
    public async Task WhenTheRepositoryCloses_ShouldClearWhatTheStatusBarSaidAboutIt()
    {
        var (session, _, _, bar, _) = Session();
        await session.OpenRepositoryAsync("fixture");
        bar.ErrorMessage = "refused"; bar.WriteDetails = "git output"; bar.SyncOperationText = "Fetched."; bar.SyncDetails = "git fetch";

        await session.CloseRepositoryAsync();

        Assert.Equal("", bar.SyncOperationText); Assert.Equal("", bar.SyncDetails);
        Assert.Equal("", bar.ErrorMessage); Assert.Equal("", bar.WriteDetails);
    }

    // One unstaged change is open and a write's result is on the status line.
    private static async Task<(RepositorySession Session, StatusBarState Bar)> OpenWithOneChangeAsync()
    {
        var (session, workspace, _, bar, _) = Session();
        workspace.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);
        await session.OpenRepositoryAsync("fixture");
        bar.Status = "a is resolved.";
        return (session, bar);
    }

    // A session over the real workspace with a watcher, opened.
    private static async Task<(RepositorySession Session, FakeRepositoryReader Reader, FakeRepositoryWatcher Watcher, StatusBarState Bar)> WatchedAsync()
    {
        var (reader, watcher, bar) = (new FakeRepositoryReader(), new FakeRepositoryWatcher(), new StatusBarState());
        var session = new RepositorySession(new(new RepositoryWorkspace(reader, watcher), bar, new ImmediateViewDispatcher()),
            new FakeLastRepositoryStore(), _ => { }, new CancelRegistry());
        await session.OpenRepositoryAsync("fixture");
        return (session, reader, watcher, bar);
    }

    private static TaskCompletionSource Held() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    // One recorder per repository the session started, the first empty one included.
    private static (RepositorySession Session, FakeRepositoryWorkspace Workspace, FakeLastRepositoryStore Store, StatusBarState Bar, List<StatusRecorder> Recorders) Session()
    {
        var workspace = new FakeRepositoryWorkspace();
        var store = new FakeLastRepositoryStore();
        var bar = new StatusBarState();
        var recorders = new List<StatusRecorder>();
        var session = new RepositorySession(new(workspace, bar, new ImmediateViewDispatcher()), store,
            repository => recorders.Add(new(repository)), new CancelRegistry());
        return (session, workspace, store, bar, recorders);
    }

    // Records each time a repository's status had its lists ready for a snapshot: new lists or a delta, the snapshot the
    // repository had published by then (the previous one, never this one), and the lists' sizes.
    private sealed class StatusRecorder
    {
        public StatusRecorder(Repository repository)
        {
            repository.Status.Replaced += () => Record(repository, delta: false);
            repository.Status.Edited += () => Record(repository, delta: true);
        }

        public List<(bool Delta, RepositorySnapshot? Published, int Staged, int Unstaged)> Shown { get; } = [];

        private void Record(Repository repository, bool delta) =>
            Shown.Add((delta, repository.Snapshot, repository.Status.Staged.Count, repository.Status.Unstaged.Count));
    }
}
