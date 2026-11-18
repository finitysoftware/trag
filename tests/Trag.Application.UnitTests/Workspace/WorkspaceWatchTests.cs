using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

// RepositoryWorkspace.WatchAsync turns watcher signals into accepted snapshots; these tests drive it with a fake source.
public sealed class WorkspaceWatchTests
{
    [Fact]
    public async Task WhenTheWorkspaceHasNoWatcher_ShouldEndTheStreamAtOnce()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        await workspace.OpenAsync("fixture", CancellationToken.None);
        await using var updates = workspace.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
        Assert.False(await updates.MoveNextAsync());
    }

    [Fact]
    public async Task WhenAPathChanges_ShouldReadOnlyThatPathAndShowTheResult()
    {
        var (_, reader, watcher, workspace, opened) = await OpenedWithAnIncrementalReaderAsync();
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        source.Push("a.txt");
        Assert.True(await updates.MoveNextAsync());
        Assert.NotNull(updates.Current.Snapshot); Assert.False(updates.Current.IsPolling);
        Assert.Equal(["a.txt"], Assert.Single(reader.IncrementalPaths));
        Assert.True(workspace.IsCurrent(updates.Current.Snapshot!)); Assert.False(workspace.IsCurrent(opened!));
    }

    [Fact]
    public async Task WhenAPollingSourceAsksForAReconcile_ShouldReadTheWholeRepositoryAndSayItPolls()
    {
        var (full, reader, watcher, workspace, _) = await OpenedWithAnIncrementalReaderAsync();
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        source.Push("a.txt");
        await updates.MoveNextAsync();
        var calls = full.Calls;
        source.IsPolling = true;
        source.PushReconcile();
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(calls + 1, full.Calls); Assert.Single(reader.IncrementalPaths); Assert.True(updates.Current.IsPolling);
    }

    [Fact]
    public async Task WhenTheReaderCannotReadSinglePaths_ShouldReadTheWholeRepositoryForAPathChange()
    {
        var (reader, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var calls = reader.Calls;
        watcher.Latest.Push("a.txt");
        Assert.True(await updates.MoveNextAsync());
        Assert.Equal(calls + 1, reader.Calls); Assert.NotNull(updates.Current.Snapshot);
    }

    [Fact]
    public async Task WhenAWatchedReadFails_ShouldReportTheErrorAndAskTheSourceToReconcile()
    {
        var (reader, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        reader.Failure = new IOException("index.lock");
        source.Push("a.txt");
        Assert.True(await updates.MoveNextAsync());
        Assert.Null(updates.Current.Snapshot);
        Assert.True(source.Reconciles >= 1);
    }

    // The failure is reported once: the next update is the good read, not the error again.
    [Fact]
    public async Task WhenAWatchedReadSucceedsAfterAFailure_ShouldShowTheSnapshotWithoutAnError()
    {
        var (reader, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        reader.Failure = new IOException("index.lock");
        source.Push("a.txt");
        await updates.MoveNextAsync();
        reader.Failure = null;
        source.Push("b.txt");
        Assert.True(await updates.MoveNextAsync());
        Assert.NotNull(updates.Current.Snapshot);
    }

    [Fact]
    public async Task WhenTheConsumerCancels_ShouldEndTheStreamQuietlyButKeepTheSource()
    {
        var (_, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        using var consumer = new CancellationTokenSource();
        await using var updates = workspace.WatchAsync(consumer.Token).GetAsyncEnumerator();
        var next = updates.MoveNextAsync().AsTask();
        await watcher.Latest.Reading;
        Assert.False(next.IsCompleted);
        consumer.Cancel();
        Assert.False(await next);
        Assert.False(watcher.Latest.IsDisposed); // the source belongs to the session, not to the consumer
    }

    [Fact]
    public async Task WhenTheRepositoryClosesWhileTheStreamWaitsForAChange_ShouldEndItQuietlyAndDisposeTheSource()
    {
        var (_, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        await using var waiting = workspace.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
        var next = waiting.MoveNextAsync().AsTask();
        await watcher.Latest.Reading;
        await workspace.CloseAsync();
        Assert.False(await next);
        Assert.True(watcher.Latest.IsDisposed);
    }

    [Fact]
    public async Task WhenTheRepositoryClosesWhileTheStreamBacksOffAfterAFailure_ShouldEndItQuietly()
    {
        var (reader, watcher, workspace, _) = await OpenedAsync();
        await using var _ = workspace;
        await using var backingOff = workspace.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
        reader.Failure = new IOException("index.lock");
        watcher.Latest.Push("a.txt");
        var pending = backingOff.MoveNextAsync().AsTask();
        await reader.Gate.Arrival(2); // the failing read has run; the stream is now in its two second backoff
        await Task.Delay(100); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(pending.IsCompleted);
        await workspace.CloseAsync();
        Assert.False(await pending);
    }

    [Fact]
    public async Task WhenAChangeArrivesDuringAWrite_ShouldHoldTheUpdateBack()
    {
        var writer = new FakeGitWriter();
        var (_, watcher, workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        await writer.Gate.Started;
        var next = updates.MoveNextAsync().AsTask();
        source.Push("a.txt");
        await source.Reconciled.WaitAsync(lifetime.Token);
        await Task.Delay(200, lifetime.Token); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
        Assert.False(next.IsCompleted);
        writer.Gate.Open();
        await write;
        lifetime.Cancel();
        await next;
    }

    [Fact]
    public async Task WhenTheWriteThatHeldAChangeBackCompletes_ShouldReadOnlyForItsOwnVerification()
    {
        var writer = new FakeGitWriter();
        var (reader, watcher, workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        await writer.Gate.Started;
        var next = updates.MoveNextAsync().AsTask();
        source.Push("a.txt");
        await source.Reconciled.WaitAsync(lifetime.Token);
        var reads = reader.Calls;
        writer.Gate.Open();
        var result = await write;
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome);
        Assert.Equal(reads + 1, reader.Calls); // the write's own verification read, no watcher read in between
        lifetime.Cancel();
        await next;
    }

    [Fact]
    public async Task WhenTheNextChangeArrivesAfterTheWriteCompleted_ShouldShowACurrentSnapshot()
    {
        var writer = new FakeGitWriter();
        var (_, watcher, workspace, opened) = await OpenedAsync(writer);
        await using var _ = workspace;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var updates = workspace.WatchAsync(lifetime.Token).GetAsyncEnumerator();
        var source = watcher.Latest;
        writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        await writer.Gate.Started;
        var next = updates.MoveNextAsync().AsTask();
        source.Push("a.txt");
        await source.Reconciled.WaitAsync(lifetime.Token);
        writer.Gate.Open();
        await write;
        source.Push("b.txt");
        Assert.True(await next);
        Assert.NotNull(updates.Current.Snapshot); Assert.True(workspace.IsCurrent(updates.Current.Snapshot!));
    }

    // A workspace over a plain reader and a fake watcher, opened on the fixture.
    private static async Task<(FakeRepositoryReader Reader, FakeRepositoryWatcher Watcher, RepositoryWorkspace Workspace, RepositorySnapshot? Opened)> OpenedAsync(
        FakeGitWriter? writer = null)
    {
        var reader = new FakeRepositoryReader(); var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        return (reader, watcher, workspace, opened);
    }

    // A workspace over a reader that can read single paths, backed by a full reader, opened on the fixture.
    private static async Task<(FakeRepositoryReader Full, FakeIncrementalRepositoryReader Reader, FakeRepositoryWatcher Watcher, RepositoryWorkspace Workspace,
        RepositorySnapshot? Opened)> OpenedWithAnIncrementalReaderAsync()
    {
        var full = new FakeRepositoryReader(); var reader = new FakeIncrementalRepositoryReader(full); var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        return (full, reader, watcher, workspace, opened);
    }
}
