using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class LiveRefreshTests
{
    [Fact]
    public async Task WhenTheWatchStarts_ShouldSayItIsWatchingForChanges()
    {
        var (refresh, _, _, _) = Watching();
        Assert.Equal("Watching for changes", refresh.Status);
        refresh.Stop();
        await refresh.Completion;
    }

    [Fact]
    public async Task WhenAnUpdateArrives_ShouldApplyItsSnapshot()
    {
        var (refresh, workspace, _, applied) = Watching();
        var snapshot = Snapshots.Create(files: [Snapshots.Modified("a")]);
        workspace.Stream.Writer.TryWrite(new(snapshot));
        await Until(() => applied.Count == 1);
        Assert.Same(snapshot, applied[0]);
        refresh.Stop();
        await refresh.Completion;
    }

    [Fact]
    public async Task WhenAnUpdateComesFromPolling_ShouldApplyItAndSayHowOftenTheRepositoryIsChecked()
    {
        var (refresh, workspace, _, applied) = Watching();
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create(files: [Snapshots.Modified("a")])));
        await Until(() => applied.Count == 1);
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create(), IsPolling: true));
        await Until(() => refresh.Status.Contains("5 seconds"));
        Assert.Equal(2, applied.Count);
        refresh.Stop();
        await refresh.Completion;
    }

    [Fact]
    public async Task WhenTheWatchStops_ShouldClearItsStatus()
    {
        var (refresh, _, _, _) = Watching();
        refresh.Stop();
        await refresh.Completion;
        Assert.Equal("", refresh.Status);
    }

    [Fact]
    public async Task WhenACheckFails_ShouldShowARetryNoticeAndApplyNothing()
    {
        var (refresh, workspace, bar, applied) = Watching();
        workspace.Stream.Writer.TryWrite(new(null));
        await Until(() => refresh.Status.Contains("retrying"));
        Assert.Contains("could not be checked", bar.Notice);
        Assert.Empty(applied);
        refresh.Stop();
        await refresh.Completion;
    }

    [Fact]
    public async Task WhenAGoodUpdateFollowsAFailedCheck_ShouldApplyItAndTakeTheNoticeDown()
    {
        var (refresh, workspace, bar, applied) = Watching();
        workspace.Stream.Writer.TryWrite(new(null));
        await Until(() => refresh.Status.Contains("retrying"));
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create()));
        await Until(() => refresh.Status == "Watching for changes");
        Assert.Equal("", bar.Notice);
        Assert.Single(applied);
        refresh.Stop();
        await refresh.Completion;
    }

    // The watch takes down only its own notice: one another part of the application raised after the failure stays.
    [Fact]
    public async Task WhenAnotherComponentRaisedANoticeAfterTheFailure_ShouldNotClearIt()
    {
        var (refresh, workspace, bar, _) = Watching();
        workspace.Stream.Writer.TryWrite(new(null));
        await Until(() => refresh.Status.Contains("retrying"));
        bar.Notice = LayoutText.NotSaved;
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create()));
        await Until(() => refresh.Status == "Watching for changes");
        Assert.Equal(LayoutText.NotSaved, bar.Notice);
        refresh.Stop();
        await refresh.Completion;
    }

    // Every write stops and restarts the watch; a recovery after that still takes the watch's own notice down.
    [Fact]
    public async Task WhenAWritePausesTheWatchBetweenTheFailureAndTheRecovery_ShouldTakeTheNoticeDown()
    {
        var (refresh, workspace, bar, applied) = Watching();
        workspace.Stream.Writer.TryWrite(new(null));
        await Until(() => refresh.Status.Contains("retrying"));
        refresh.Stop();
        await refresh.Completion;
        refresh.Start();
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create()));
        await Until(() => applied.Count == 1 && bar.Notice == "");
        refresh.Stop();
        await refresh.Completion;
    }

    [Fact]
    public async Task WhenTheWatcherBreaksForGood_ShouldStopTheLiveRefreshAndSayHowToReconnect()
    {
        var (refresh, workspace, bar, _) = Watching();
        workspace.Stream.Writer.TryComplete(new IOException("watcher gone"));
        await Until(() => refresh.Status == "Live refresh stopped");
        Assert.Equal("Automatic refresh is unavailable. Choose Refresh to reconnect.", bar.Notice);
        refresh.Stop();
        await refresh.Completion;
    }

    // Stopped while it applies an update, the watch ends once that update is done: it reads no later update, even from a
    // source that still delivers after cancellation, and the status line is empty.
    [Fact]
    public async Task WhenStoppedWhileApplyingAnUpdate_ShouldEndWithoutReadingAnother()
    {
        var workspace = new ChannelWorkspace { IgnoresCancel = true };
        var (entered, release) = (new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var refresh = new LiveRefresh(workspace, new StatusBarState(), async (_, _, _) =>
        {
            entered.SetResult();
            await release.Task;
        });
        refresh.Start();
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create()));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        refresh.Stop();
        release.SetResult();
        await refresh.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(refresh.Status);
        workspace.Stream.Writer.TryComplete();
    }

    [Fact]
    public async Task WhenAnUpdateCarriesASnapshotTheWorkspaceNoLongerStandsBehind_ShouldSkipIt()
    {
        var (refresh, workspace, _, applied) = Watching();
        var stale = Snapshots.Create();
        workspace.Stale.Add(stale);
        workspace.Stream.Writer.TryWrite(new(stale));
        var seen = Snapshots.Create(files: [Snapshots.Modified("seen")]);
        workspace.Stream.Writer.TryWrite(new(seen));
        await Until(() => applied.Count == 1);
        Assert.Same(seen, applied[0]);
        refresh.Stop();
        await refresh.Completion;
    }

    // A source that still delivers after the watch stopped, as an external one may.
    [Fact]
    public async Task WhenAnUpdateArrivesAfterTheWatchStopped_ShouldEndTheWatchWithoutApplyingIt()
    {
        var (refresh, workspace, _, applied) = Watching(new ChannelWorkspace { IgnoresCancel = true });
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create(files: [Snapshots.Modified("seen")])));
        await Until(() => applied.Count == 1);
        refresh.Stop();
        workspace.Stream.Writer.TryWrite(new(Snapshots.Create(files: [Snapshots.Modified("late")])));
        workspace.Stream.Writer.TryComplete();
        await refresh.Completion;
        Assert.Single(applied);
    }

    private static (LiveRefresh Refresh, ChannelWorkspace Workspace, StatusBarState Bar, List<RepositorySnapshot> Applied) Watching(ChannelWorkspace? source = null)
    {
        var workspace = source ?? new ChannelWorkspace();
        var bar = new StatusBarState();
        var applied = new List<RepositorySnapshot>();
        var refresh = new LiveRefresh(workspace, bar, (snapshot, _, _) => { applied.Add(snapshot); return Task.CompletedTask; });
        refresh.Start();
        return (refresh, workspace, bar, applied);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    private sealed class ChannelWorkspace : ILiveRepositoryWorkspace
    {
        public Channel<RepositoryUpdate> Stream { get; } = Channel.CreateUnbounded<RepositoryUpdate>();
        // Snapshots the workspace no longer stands behind; every other one is current.
        public HashSet<RepositorySnapshot> Stale { get; } = new(ReferenceEqualityComparer.Instance);
        // A source that keeps delivering after the watch was cancelled, as an external one may.
        public bool IgnoresCancel { get; init; }
        public async IAsyncEnumerable<RepositoryUpdate> WatchAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var update in Stream.Reader.ReadAllAsync(IgnoresCancel ? CancellationToken.None : ct)) yield return update;
        }
        public bool IsCurrent(RepositorySnapshot snapshot) => !Stale.Contains(snapshot);
        public Task<FileDiff?> ReadDiffAsync(FileStatus file, bool fromIndex, bool preview, CancellationToken ct) => Task.FromResult<FileDiff?>(null);
        public Task<ConflictText?> ReadConflictAsync(FileStatus file, CancellationToken ct) => Task.FromResult<ConflictText?>(null);
    }
}
