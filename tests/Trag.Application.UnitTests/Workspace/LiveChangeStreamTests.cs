using Trag.Application.Live;
using Trag.Application.Ports.Watching;
using Trag.Application.Workspace;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

// The states the facade cannot reach directly; the main loop is covered through RepositoryWorkspace in WorkspaceWatchTests.
public sealed class LiveChangeStreamTests
{
    private static LiveChangeStream Stream(WorkspaceState state, FakeRepositoryReader reader) => new(reader, state, new WatcherAttachment(null, state));

    [Fact]
    public async Task WhenNoRepositoryIsOpen_ShouldEndTheStreamAtOnce()
    {
        var state = new WorkspaceState();
        var stream = Stream(state, new FakeRepositoryReader());
        await using var none = stream.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.False(await none.MoveNextAsync());
    }

    [Fact]
    public async Task WhenTheOpenRepositoryHasNoChangeSource_ShouldEndTheStreamAtOnce()
    {
        var (state, _, _) = OpenedSlot.Create();
        var stream = Stream(state, new FakeRepositoryReader());
        await using var noSource = stream.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.False(await noSource.MoveNextAsync());
    }

    [Fact]
    public async Task WhenAChangeArrivesDuringAWrite_ShouldAskTheSourceToReconcileAndReadNothing()
    {
        var reader = new FakeRepositoryReader();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var (state, slot, source, updates) = Watched(reader, lifetime.Token);
        await using var _ = updates;
        state.TryBeginWrite(slot.Snapshots.Snapshot!.Root, out var writing);
        source.Push("a.txt");
        var next = updates.MoveNextAsync().AsTask();
        await source.Reconciled.WaitAsync(lifetime.Token);
        await Task.Delay(150, lifetime.Token); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(next.IsCompleted); Assert.Equal(0, reader.Calls);
        lifetime.Cancel();
        await next;
    }

    [Fact]
    public async Task WhenAChangeArrivesAfterTheWriteEnds_ShouldReadOnceAndYieldTheAcceptedSnapshot()
    {
        var reader = new FakeRepositoryReader();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var (state, slot, source, updates) = Watched(reader, lifetime.Token);
        await using var _ = updates;
        state.TryBeginWrite(slot.Snapshots.Snapshot!.Root, out var writing);
        source.Push("a.txt");
        var next = updates.MoveNextAsync().AsTask();
        await source.Reconciled.WaitAsync(lifetime.Token);
        state.EndWrite(slot);
        source.Push("b.txt");
        Assert.True(await next);
        Assert.NotNull(updates.Current.Snapshot); Assert.Equal(1, reader.Calls); Assert.True(state.Holds(updates.Current.Snapshot!));
    }

    [Fact]
    public async Task WhenANewerSnapshotIsAcceptedDuringARead_ShouldDiscardTheReadAndAskTheSourceToReconcile()
    {
        var reader = new FakeRepositoryReader { Gate = { IsClosed = true } };
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var (state, slot, source, updates) = Watched(reader, lifetime.Token);
        await using var _ = updates;
        source.Push("a.txt");
        var next = updates.MoveNextAsync().AsTask();
        await reader.Gate.Started;
        var newer = state.Accept(slot, slot.Lease.Invalidate(), Snapshots.Create(files: [Snapshots.Modified("b")]), null);
        reader.Gate.Open();
        await source.Reconciled.WaitAsync(lifetime.Token);
        await Task.Delay(150, lifetime.Token); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(next.IsCompleted); Assert.True(state.Holds(newer!));
        lifetime.Cancel();
        Assert.False(await next);
    }

    [Fact]
    public async Task WhenANewerSnapshotIsAcceptedWhileAFailedReadBacksOff_ShouldNotReportTheOldFailure()
    {
        var reader = new FakeRepositoryReader { Failure = new IOException("old read failed") };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (state, slot, source, updates) = Watched(reader, cancellation.Token);
        await using var _ = updates;
        source.Push("a.txt");
        var pending = updates.MoveNextAsync().AsTask();
        Assert.Equal(1, reader.Calls);
        Assert.False(pending.IsCompleted);

        var newer = state.Accept(slot, slot.Lease.Invalidate(), Snapshots.Create(files: [Snapshots.Modified("b")]), null);
        await source.Reconciled.WaitAsync(cancellation.Token);
        cancellation.Cancel();

        Assert.False(await pending);
        Assert.True(state.Holds(newer!));
    }

    [Fact]
    public async Task WhenTheChangeSourceIsDisposed_ShouldEndTheStreamQuietly()
    {
        var (_, _, source, updates) = Watched(new FakeRepositoryReader(), CancellationToken.None);
        await using var _ = updates;
        var next = updates.MoveNextAsync().AsTask();
        await source.Reading;
        source.Dispose();
        Assert.False(await next);
    }

    // F5 stops the watch, then begins its read. A change the watch took just before the stop must not begin a read after
    // F5's, which would make F5's result stale and leave the repository saying it is being refreshed.
    [Fact]
    public async Task WhenALiveReadBeginsAfterF5_ShouldNotDropTheF5Result()
    {
        var reader = new FakeRepositoryReader { Gate = { IsClosed = true } };
        var (state, slot, _) = OpenedSlot.Create();
        var source = new HeldChangeSource();
        slot.Changes = source;
        using var watch = new CancellationTokenSource();
        await using var updates = Stream(state, reader).WatchAsync(watch.Token).GetAsyncEnumerator();
        var next = updates.MoveNextAsync().AsTask();
        await source.Reading.Task;

        watch.Cancel();
        var refresh = new RepositoryOpener(reader, state, new WatcherAttachment(null, state)).RefreshAsync(CancellationToken.None);
        source.Change.SetResult(new(["a.txt"], false));
        Assert.False(await next);
        reader.Gate.Open();

        Assert.NotNull(await refresh);
    }

    // A source whose change was taken before the watch stopped: it hands it over when the test says.
    private sealed class HeldChangeSource : IRepositoryChangeSource
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<RepositoryChange> Change { get; } = new();
        public bool IsPolling => false;

        public Task<RepositoryChange> ReadAsync(CancellationToken cancellationToken)
        {
            Reading.TrySetResult();
            return Change.Task;
        }

        public void Reconcile() { }
        public void Dispose() { }
    }

    // An opened slot whose changes come from a fake source, watched through the given reader.
    private static (WorkspaceState State, RepositorySlot Slot, FakeChangeSource Source, IAsyncEnumerator<RepositoryUpdate> Updates) Watched(
        FakeRepositoryReader reader, CancellationToken token)
    {
        var (state, slot, _) = OpenedSlot.Create();
        var source = new FakeChangeSource();
        slot.Changes = source;
        return (state, slot, source, Stream(state, reader).WatchAsync(token).GetAsyncEnumerator());
    }
}
