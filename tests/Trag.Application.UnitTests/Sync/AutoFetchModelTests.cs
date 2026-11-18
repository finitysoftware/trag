using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Sync;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Sync;

// The fetch Trag runs on its own over the real workspace, fake Git and a hand-moved clock: the interval it follows and
// saves, what a refused sign-in stops and what resumes it, and that a write or another repository drops the fetch
// running without counting it as failed.
public sealed class AutoFetchModelTests
{
    private static RepositorySnapshot Tracking(string root) => Snapshots.Create(root: root, branch: "trunk", trackingRemote: "origin",
        upstreamBranchRef: "refs/heads/trunk", remotes: [new("origin", "fetch", "push")]);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly RepositoryWorkspace workspace;
        public FakeGitSync Sync { get; } = new();
        public FakeGitWriter Writer { get; } = new();
        public FakeRepositoryReader Reader { get; } = new() { Snapshot = Tracking("fixture") };
        public StatusBarState StatusBar { get; } = new();
        public ManualTime Time { get; } = new();
        public LayoutPreferences Layout { get; }
        public AutoFetchModel AutoFetch { get; }

        // The automatic fetch reads the hand-moved clock, or one whose timers fire for a test of its running schedule.
        public Fixture(TimeProvider? clock = null)
        {
            workspace = new(Reader, writer: Writer, synchronizer: Sync);
            Layout = new(null, StatusBar);
            AutoFetch = new(Layout, StatusBar, clock ?? Time);
        }

        // As the session does: a new repository per open, which the automatic fetch follows before it is read.
        public async Task<Repository> OpenAsync(string root = "fixture")
        {
            Reader.Snapshot = Tracking(root);
            var repository = new Repository(new RepositoryServices(workspace, StatusBar, new ImmediateViewDispatcher()) { Time = Time });
            AutoFetch.Follow(repository);
            repository.Apply((await workspace.OpenAsync(root, CancellationToken.None))!);
            await repository.Remotes.Completion;
            return repository;
        }

        // A fetch a test still holds is let go, so a test that fails does not leave the workspace waiting for it.
        public ValueTask DisposeAsync()
        {
            Sync.WriteGate.Open();
            Writer.Gate.Open();
            return workspace.DisposeAsync();
        }
    }

    [Fact]
    public void WhenNoIntervalIsSaved_ShouldFetchEveryMinute()
    {
        var (_, autoFetch) = Preferences();
        Assert.Equal(AutoFetchInterval.OneMinute, autoFetch.Interval);
    }

    [Fact]
    public void WhenAnIntervalWasSavedBeforeTheStart_ShouldFollowIt()
    {
        var statusBar = new StatusBarState();
        var layout = new LayoutPreferences(null, statusBar) { AutoFetchMinutes = 5 };
        Assert.Equal(AutoFetchInterval.FiveMinutes, new AutoFetchModel(layout, statusBar, TimeProvider.System).Interval);
    }

    [Fact]
    public void WhenTheSavedIntervalChanges_ShouldFollowIt()
    {
        var (layout, autoFetch) = Preferences();
        layout.AutoFetchMinutes = 5;
        Assert.Equal(AutoFetchInterval.FiveMinutes, autoFetch.Interval);
    }

    // Five minutes are saved first, so falling back is a change.
    [Fact]
    public void WhenTheSavedIntervalIsNoChoice_ShouldFallBackToEveryMinute()
    {
        var (layout, autoFetch) = Preferences();
        layout.AutoFetchMinutes = 5;
        layout.AutoFetchMinutes = 7;
        Assert.Equal(AutoFetchInterval.OneMinute, autoFetch.Interval);
    }

    // Three changes: the two saved values the preferences go through first, and the user's choice.
    [Fact]
    public void WhenTheUserChoosesAnInterval_ShouldSaveItAndAnnounceEachChange()
    {
        var (layout, autoFetch) = Preferences();
        var changes = 0;
        autoFetch.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AutoFetchModel.Interval)) changes++; };
        layout.AutoFetchMinutes = 5;
        layout.AutoFetchMinutes = 7;

        autoFetch.Interval = AutoFetchInterval.Off;

        Assert.Equal((0, AutoFetchInterval.Off, 3), (layout.AutoFetchMinutes, autoFetch.Interval, changes));
    }

    // Quietly: through the synchronizer beside the shown state, not as one of the user's writes.
    [Fact]
    public async Task WhenTheFirstTickComes_ShouldFetchTheShownUpstream()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch) = (fixture.Sync, fixture.AutoFetch);
        await fixture.OpenAsync();

        await autoFetch.TickAsync();

        var fetch = Assert.IsType<SyncRequest>(Assert.Single(sync.Writes));
        Assert.Equal((SyncAction.Fetch, "refs/heads/trunk"), (fetch.Action, fetch.Sync.Branch!.SourceRef));
    }

    // The composition starts the schedule; its loop ticks on the automatic fetch's own clock.
    [Fact]
    public async Task WhenTheScheduleRuns_ShouldFetchAtItsNextTick()
    {
        var clock = new SteppedTime();
        await using var fixture = new Fixture(clock);
        await fixture.OpenAsync();
        fixture.AutoFetch.Run();

        clock.Advance(AutoFetchModel.Tick);

        await ShellHarness.Until(() => fixture.Sync.Writes.Count == 1, "the scheduled fetch");
        fixture.AutoFetch.Stop();
    }

    [Fact]
    public async Task WhenTheScheduleStops_ShouldFetchNoMore()
    {
        var clock = new SteppedTime();
        await using var fixture = new Fixture(clock);
        await fixture.OpenAsync();
        fixture.AutoFetch.Run();
        fixture.AutoFetch.Stop();

        clock.Advance(AutoFetchModel.Tick);

        await Task.Delay(200);
        Assert.Empty(fixture.Sync.Writes);
    }

    // The application starts it once; a second start keeps the one loop, which one stop ends.
    [Fact]
    public async Task WhenTheScheduleIsStartedAgain_ShouldStillStopWithOneStop()
    {
        var clock = new SteppedTime();
        await using var fixture = new Fixture(clock);
        await fixture.OpenAsync();
        fixture.AutoFetch.Run();
        fixture.AutoFetch.Run();
        fixture.AutoFetch.Stop();

        clock.Advance(AutoFetchModel.Tick);

        await Task.Delay(200);
        Assert.Empty(fixture.Sync.Writes);
    }

    [Fact]
    public async Task WhenAFetchStartsAndEnds_ShouldSaySoEachTime()
    {
        await using var fixture = new Fixture();
        await fixture.OpenAsync();
        var fetching = new List<bool>();
        fixture.AutoFetch.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AutoFetchModel.IsFetching)) fetching.Add(fixture.AutoFetch.IsFetching); };

        await fixture.AutoFetch.TickAsync();

        Assert.Equal([true, false], fetching);
    }

    [Fact]
    public async Task WhenTheIntervalHasNotPassedYet_ShouldNotFetchAgain()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        await fixture.OpenAsync();
        await autoFetch.TickAsync();
        time.Now = time.Now.AddSeconds(59);

        await autoFetch.TickAsync();

        Assert.Single(sync.Writes);
    }

    [Fact]
    public async Task WhenTheRemoteRefusesTheSignIn_ShouldStopTheScheduleWithANotice()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch) = (fixture.Sync, fixture.AutoFetch);
        await StoppedAsync(fixture);
        Assert.Equal((2, true, AutoFetchText.Stopped), (sync.Writes.Count, autoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    // A wrong sign-in is not repeated, not even an hour later or when the window comes back.
    [Fact]
    public async Task WhenTheScheduleIsStopped_ShouldNotFetchOnATickOrAnActivation()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        await StoppedAsync(fixture);
        time.Now = time.Now.AddHours(1);

        await autoFetch.TickAsync(); await autoFetch.ActivatedAsync();

        Assert.Equal(2, sync.Writes.Count);
    }

    // Here the user's own fetch, through the writer.
    [Fact]
    public async Task WhenAFetchSucceedsWhileStopped_ShouldResumeTheScheduleAndTakeTheNoticeDown()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var repository = await StoppedAsync(fixture);

        await FetchedByTheUserAsync(fixture, repository);

        Assert.Equal((false, ""), (autoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    // The stop's notice is the automatic fetch's own: a notice another part of the application raised since stays.
    [Fact]
    public async Task WhenAFetchSucceedsWhileStoppedAfterAnotherNotice_ShouldResumeAndLeaveThatNotice()
    {
        await using var fixture = new Fixture();
        var repository = await StoppedAsync(fixture);
        fixture.StatusBar.Notice = LayoutText.NotSaved;

        await FetchedByTheUserAsync(fixture, repository);

        Assert.Equal((false, LayoutText.NotSaved), (fixture.AutoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    // The target is read again for an unrelated reason (a write ended, a branch switch, a refresh) and still says when
    // the user's fetch was: no fetch succeeded since, so the stop stays and the refused sign-in is not tried again.
    [Fact]
    public async Task WhenTheTargetIsReadAgainWhileStopped_ShouldKeepTheScheduleStopped()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        var repository = await fixture.OpenAsync();
        await FetchedByTheUserAsync(fixture, repository);
        time.Now = time.Now.AddMinutes(1);
        sync.Result = GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        await autoFetch.TickAsync();
        Assert.True(autoFetch.IsStopped, "the refused fetch stops the schedule");

        repository.Remotes.Reread(); await repository.Remotes.Completion;
        time.Now = time.Now.AddMinutes(1);
        await autoFetch.TickAsync();

        Assert.Equal((true, AutoFetchText.Stopped, 1), (autoFetch.IsStopped, fixture.StatusBar.Notice, sync.Writes.Count));
    }

    [Fact]
    public async Task WhenASucceededFetchResumedTheSchedule_ShouldFetchAgainAfterTheInterval()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        var repository = await StoppedAsync(fixture);
        await FetchedByTheUserAsync(fixture, repository);
        sync.Result = new(GitWriteOutcome.Succeeded, "Fetched.");
        time.Now = time.Now.AddMinutes(1);

        await autoFetch.TickAsync();

        Assert.Equal(3, sync.Writes.Count);
    }

    // A fetch the network refused waits longer each time, one, two, then five minutes, and says nothing: the remote may
    // only be out of reach for a while.
    [Fact]
    public async Task WhenTheNetworkRefusesAFetch_ShouldWaitLongerBeforeEachNextOne()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        await fixture.OpenAsync();
        sync.Result = GitWriteResult.Rejected("Could not resolve host.");
        await autoFetch.TickAsync();
        foreach (var (wait, count) in new[] { (60, 2), (120, 3), (300, 4) })
        {
            time.Now = time.Now.AddSeconds(wait - 1);
            await autoFetch.TickAsync();
            Assert.Equal(count - 1, sync.Writes.Count);
            time.Now = time.Now.AddSeconds(1);
            await autoFetch.TickAsync();
            Assert.Equal(count, sync.Writes.Count);
        }
        Assert.Equal("", fixture.StatusBar.Notice);
    }

    // A fetch that ends in a way nothing expected counts as a failed one: the schedule goes on instead of dying quietly.
    [Fact]
    public async Task WhenAFetchFailsUnexpectedly_ShouldCountItAsFailedAndFetchAgainLater()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        await fixture.OpenAsync();
        sync.Failure = new InvalidOperationException("Something nobody expected.");

        await autoFetch.TickAsync();
        sync.Failure = null;
        time.Now = time.Now.AddSeconds(60);
        await autoFetch.TickAsync();

        Assert.Equal(2, sync.Writes.Count);
    }

    // A push that the remote accepted proves the sign-in as well as a fetch would, so the notice does not outlive what it
    // asked for.
    [Fact]
    public async Task WhenTheRemoteAcceptsAPushWhileStopped_ShouldResumeTheScheduleAndTakeTheNoticeDown()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var repository = await StoppedAsync(fixture);
        Assert.True(autoFetch.IsStopped, "the first refused fetch stops the schedule");
        Assert.Equal(AutoFetchText.Stopped, fixture.StatusBar.Notice);

        await PushedAsync(fixture, repository);

        Assert.Equal((false, ""), (autoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    [Fact]
    public async Task WhenAnAcceptedPushResumedTheSchedule_ShouldFetchAgainAfterTheInterval()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch, time) = (fixture.Sync, fixture.AutoFetch, fixture.Time);
        var repository = await StoppedAsync(fixture);
        await PushedAsync(fixture, repository);
        time.Now = time.Now.AddMinutes(1);
        var writes = sync.Writes.Count;

        await autoFetch.TickAsync();

        // The refused fetch that stopped the schedule is also a fetch, so the tick must have added a write of its own.
        Assert.Equal(writes + 1, sync.Writes.Count);
        Assert.Equal(SyncAction.Fetch, Assert.IsType<SyncRequest>(sync.Writes[^1]).Action);
    }

    // A refused push is no proof of a sign-in. The user's own writes run through the writer. An accepted push ran before
    // the schedule stopped again, so until the refused one reports, the last write is a push the remote accepted.
    [Fact]
    public async Task WhenTheRemoteRefusesAPushWhileStopped_ShouldKeepTheScheduleStopped()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var repository = await StoppedAsync(fixture);
        await PushedAsync(fixture, repository);
        fixture.Sync.Result = GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        await autoFetch.TickAsync();
        Assert.True(autoFetch.IsStopped);
        fixture.Writer.Result = GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };

        await repository.Remotes.PushToAsync(repository.Remotes.Overview.State!);

        Assert.True(autoFetch.IsStopped, "a refused push keeps the schedule stopped");
        Assert.Equal(AutoFetchText.Stopped, fixture.StatusBar.Notice);
    }

    // A changed account for the remote is a new sign-in to try, so the notice does not outlive what it asked for.
    [Fact]
    public async Task WhenTheAccountForTheRemoteChangesWhileStopped_ShouldResumeTheScheduleAndTakeTheNoticeDown()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var repository = await StoppedAsync(fixture);
        fixture.Writer.Result = new(GitWriteOutcome.Succeeded, "Saved.");

        await repository.Writer.RunAsync(expected => new SyncRequest(expected, repository.Remotes.Overview.State!, SyncAction.SelectAccount, "alice"));

        Assert.Equal((false, ""), (autoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    [Fact]
    public async Task WhenAWriteStartsDuringAFetch_ShouldDropTheFetch()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var (repository, fetching) = await FetchingAsync(fixture);
        Assert.True(autoFetch.IsFetching);
        fixture.Writer.Gate.IsClosed = true;

        var writing = repository.Remotes.PushAsync();
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(autoFetch.IsFetching);
        fixture.Writer.Gate.Open(); await writing;
    }

    // A watcher's update or a refresh applies a new snapshot while the fetch runs; only a write takes precedence.
    [Fact]
    public async Task WhenTheShownStateChangesDuringAFetch_ShouldKeepFetching()
    {
        await using var fixture = new Fixture();
        var (repository, fetching) = await FetchingAsync(fixture);

        repository.Apply(Tracking("fixture") with { Files = [Snapshots.Modified("a.txt")] });

        Assert.False(fixture.Sync.WriteGate.Token.IsCancellationRequested);
        fixture.Sync.WriteGate.Open(); await fetching;
    }

    // Only the fetch that gave way is not counted: the next one that fails counts as failed.
    [Fact]
    public async Task WhenAFetchFailsAfterAnEarlierOneGaveWay_ShouldCountTheFailure()
    {
        await using var fixture = new Fixture();
        await DroppedForAWriteAsync(fixture);
        fixture.Sync.Result = GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        fixture.Sync.WriteGate.Open();

        await fixture.AutoFetch.TickAsync();

        Assert.Equal((true, AutoFetchText.Stopped), (fixture.AutoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    // The dropped fetch does not count as a failure, so the next tick that is due fetches again at once instead of
    // waiting a failure's minute.
    [Fact]
    public async Task WhenAWriteDroppedTheFetch_ShouldFetchAgainAtTheNextTick()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch) = (fixture.Sync, fixture.AutoFetch);
        await DroppedForAWriteAsync(fixture);

        var fetching = autoFetch.TickAsync();

        Assert.Equal(2, sync.Writes.Count);
        sync.WriteGate.Open(); await fetching;
    }

    [Fact]
    public async Task WhenAnotherRepositoryOpensDuringAFetch_ShouldDropTheFetch()
    {
        await using var fixture = new Fixture();
        var autoFetch = fixture.AutoFetch;
        var (_, fetching) = await FetchingAsync(fixture);

        await fixture.OpenAsync("second");
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.False(autoFetch.IsFetching);
    }

    // A write dropped the first fetch and another repository the second; neither counts as a failure, so the next tick
    // that is due fetches the new repository at once and nothing is stopped.
    [Fact]
    public async Task WhenAnotherRepositoryDroppedTheFetch_ShouldFetchTheNewRepositoryAtOnceWithoutAFailure()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch) = (fixture.Sync, fixture.AutoFetch);
        await DroppedForAWriteAsync(fixture);
        var fetching = autoFetch.TickAsync();
        await fixture.OpenAsync("second");
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));
        sync.WriteGate.Open();

        await autoFetch.TickAsync();

        Assert.Equal("second", Assert.IsType<SyncRequest>(sync.Writes[^1]).Expected.Root);
        Assert.Equal((3, false, ""), (sync.Writes.Count, autoFetch.IsStopped, fixture.StatusBar.Notice));
    }

    // The stop belongs to the repository whose remote refused the sign-in; another one has remotes and sign-ins of its
    // own, so it is fetched at once, and the fetch tip hears that nothing is stopped.
    [Fact]
    public async Task WhenAnotherRepositoryOpensAfterTheScheduleStopped_ShouldFetchItAtOnce()
    {
        await using var fixture = new Fixture();
        var (sync, autoFetch) = (fixture.Sync, fixture.AutoFetch);
        await StoppedAsync(fixture);
        var announced = 0;
        autoFetch.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AutoFetchModel.IsStopped)) announced++; };
        sync.Result = new(GitWriteOutcome.Succeeded, "Fetched.");
        await fixture.OpenAsync("second");

        await autoFetch.TickAsync();

        Assert.Equal((false, 1), (autoFetch.IsStopped, announced));
        Assert.Equal("second", Assert.IsType<SyncRequest>(sync.Writes[^1]).Expected.Root);
    }

    // Preferences with nothing saved, and the automatic fetch that follows them.
    private static (LayoutPreferences Layout, AutoFetchModel AutoFetch) Preferences()
    {
        var statusBar = new StatusBarState();
        var layout = new LayoutPreferences(null, statusBar);
        return (layout, new AutoFetchModel(layout, statusBar, TimeProvider.System));
    }

    // The first fetch succeeded; the next one, a minute later, had its sign-in refused.
    private static async Task<Repository> StoppedAsync(Fixture fixture)
    {
        var repository = await fixture.OpenAsync();
        await fixture.AutoFetch.TickAsync();
        fixture.Time.Now = fixture.Time.Now.AddMinutes(1);
        fixture.Sync.Result = GitWriteResult.Rejected("Git authentication failed.") with { RefusedCredentials = true };
        await fixture.AutoFetch.TickAsync();
        return repository;
    }

    // The user's own fetch, through the writer: the remotes record it at the clock's time.
    private static async Task FetchedByTheUserAsync(Fixture fixture, Repository repository)
    {
        fixture.Writer.Result = new(GitWriteOutcome.Succeeded, "Fetched.");
        await repository.Remotes.FetchAsync(); await repository.Remotes.Completion;
    }

    // The user pushed and the remote accepted it; the fetches that follow succeed too.
    private static async Task PushedAsync(Fixture fixture, Repository repository)
    {
        fixture.Sync.Result = new(GitWriteOutcome.Succeeded, "Pushed.");
        await repository.Remotes.PushToAsync(repository.Remotes.Overview.State!);
    }

    // The first fetch is held at the remote, so it is still running when the test acts.
    private static async Task<(Repository Repository, Task Fetching)> FetchingAsync(Fixture fixture)
    {
        var repository = await fixture.OpenAsync();
        fixture.Sync.WriteGate.IsClosed = true;
        return (repository, fixture.AutoFetch.TickAsync());
    }

    // The held fetch was dropped for the user's own push, which then finished; a push is no fetch, so the last fetch
    // stays the one before.
    private static async Task DroppedForAWriteAsync(Fixture fixture)
    {
        var (repository, fetching) = await FetchingAsync(fixture);
        fixture.Writer.Gate.IsClosed = true;
        var writing = repository.Remotes.PushAsync();
        await fetching.WaitAsync(TimeSpan.FromSeconds(15));
        fixture.Writer.Gate.Open(); await writing;
    }
}
