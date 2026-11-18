using Trag.Application.Workspace;
using Trag.Domain.Errors;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class WorkspaceLifecycleTests
{
    [Fact]
    public async Task WhenAnOpenIsAskedAfterDisposal_ShouldRefuseIt()
    {
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        await workspace.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => workspace.OpenAsync("fixture", CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheRepositoryIsClosedTwice_ShouldRefreshNothingAfterwards()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), new FakeRepositoryWatcher());
        Assert.NotNull(await workspace.OpenAsync("fixture", CancellationToken.None));
        await workspace.CloseAsync(); await workspace.CloseAsync();
        Assert.Null(await workspace.RefreshAsync(CancellationToken.None));
    }

    // The second disposal is the check: it must not throw.
    [Fact]
    public async Task WhenAClosedWorkspaceIsDisposedTwice_ShouldNotThrow()
    {
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), new FakeRepositoryWatcher());
        Assert.NotNull(await workspace.OpenAsync("fixture", CancellationToken.None));
        await workspace.CloseAsync();
        await workspace.DisposeAsync(); await workspace.DisposeAsync();
    }

    [Fact]
    public async Task WhenTheSamePathIsOpenedAgain_ShouldReplaceTheWatcherAndDisposeTheOldSource()
    {
        var (workspace, watcher) = await ReopenedAsync();
        await using var _ = workspace;
        Assert.Equal(2, watcher.Sources.Count);
        Assert.True(watcher.Sources[0].IsDisposed); Assert.False(watcher.Sources[1].IsDisposed);
    }

    [Fact]
    public async Task WhenTheReopenedRepositoryRefreshes_ShouldReuseItsWatcher()
    {
        var (workspace, watcher) = await ReopenedAsync();
        await using var _ = workspace;
        await workspace.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, watcher.Sources.Count);
    }

    [Fact]
    public async Task WhenTheReopenedRepositoryCloses_ShouldDisposeItsLiveSource()
    {
        var (workspace, watcher) = await ReopenedAsync();
        await using var _ = workspace;
        await workspace.CloseAsync();
        Assert.True(watcher.Sources[1].IsDisposed);
    }

    [Fact]
    public async Task WhenARepositoryOpens_ShouldTakeItsSnapshotAsCurrent()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        var first = await workspace.OpenAsync("fixture", CancellationToken.None);
        Assert.True(workspace.IsCurrent(first!));
    }

    [Fact]
    public async Task WhenTheRepositoryRefreshes_ShouldTakeOnlyTheNewSnapshotAsCurrent()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        var first = await workspace.OpenAsync("fixture", CancellationToken.None);
        var second = await workspace.RefreshAsync(CancellationToken.None);
        Assert.False(workspace.IsCurrent(first!)); Assert.True(workspace.IsCurrent(second!));
    }

    [Fact]
    public async Task WhenTheRepositoryCloses_ShouldTakeNoSnapshotAsCurrent()
    {
        await using var workspace = new RepositoryWorkspace(new FakeRepositoryReader());
        await workspace.OpenAsync("fixture", CancellationToken.None);
        var second = await workspace.RefreshAsync(CancellationToken.None);
        await workspace.CloseAsync();
        Assert.False(workspace.IsCurrent(second!));
    }

    [Fact]
    public async Task WhenAWriteStartsDuringARefresh_ShouldDropTheRefreshResult()
    {
        var (workspace, writer, _, refresh, write) = await WriteDuringARefreshAsync();
        await using var _ = workspace;
        Assert.Null(await refresh);
        writer.Gate.Open();
        await write;
    }

    // A refresh that began before a write and answers after it ended speaks for a state the write replaced: the write's
    // verified snapshot stays the shown one.
    [Fact]
    public async Task WhenARefreshThatBeganBeforeAWriteAnswersAfterIt_ShouldKeepTheWritesSnapshot()
    {
        var reader = new FakeRepositoryReader(); var writer = new FakeGitWriter();
        await using var workspace = new RepositoryWorkspace(reader, writer: writer);
        var opened = (await workspace.OpenAsync("fixture", CancellationToken.None))!;
        var (started, held) = (new TaskCompletionSource(), new TaskCompletionSource<RepositorySnapshot>());
        reader.Read = (_, _) => { started.TrySetResult(); return held.Task; };
        var refresh = workspace.RefreshAsync(CancellationToken.None);
        await started.Task;
        reader.Read = null;

        var written = await workspace.WriteAsync(new StageRequest(opened, [Snapshots.Path("a")]), CancellationToken.None);
        held.SetResult(opened with { Branch = "before the write" });

        Assert.Null(await refresh);
        Assert.True(workspace.IsCurrent(written.Snapshot!));
    }

    [Fact]
    public async Task WhenTheWriteThatOvertookARefreshFinishes_ShouldMakeItsVerifiedSnapshotCurrent()
    {
        var (workspace, writer, _, refresh, write) = await WriteDuringARefreshAsync();
        await using var _ = workspace;
        await refresh;
        writer.Gate.Open();
        var result = await write;
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome); Assert.NotNull(result.Snapshot); Assert.True(workspace.IsCurrent(result.Snapshot!));
    }

    [Fact]
    public async Task WhenTheWriteThatOvertookARefreshFinishes_ShouldKeepTheOneWatcherSourceLive()
    {
        var (workspace, writer, watcher, refresh, write) = await WriteDuringARefreshAsync();
        await using var _ = workspace;
        await refresh;
        writer.Gate.Open();
        await write;
        Assert.Single(watcher.Sources); Assert.False(watcher.Latest.IsDisposed);
    }

    [Fact]
    public async Task WhenARefreshOvertakesAnOpen_ShouldKeepOnlyTheRefreshResult()
    {
        var (workspace, _, results) = await OpenOvertakenByARefreshAsync();
        await using var _ = workspace;
        Assert.Null(results[0]); Assert.NotNull(results[1]);
        Assert.True(workspace.IsCurrent(results[1]!));
    }

    // The superseded read may or may not have created a source before losing; either way one live source remains.
    [Fact]
    public async Task WhenARefreshOvertakesAnOpen_ShouldLeaveOneLiveWatcherSource()
    {
        var (workspace, watcher, _) = await OpenOvertakenByARefreshAsync();
        await using var _ = workspace;
        Assert.InRange(watcher.Sources.Count, 1, 2);
        Assert.Equal(1, watcher.Sources.Count(source => !source.IsDisposed));
    }

    [Fact]
    public async Task WhenDisposedWhileAWriteRuns_ShouldWaitForItAndKeepTheWatcher()
    {
        var (_, writer, watcher, _, write, disposing) = await DisposingWhileAWriteRunsAsync();
        try
        {
            await Task.Delay(150); // A real pause: the assertion is that nothing happens meanwhile, and nothing can be awaited for that.
            Assert.False(disposing.IsCompleted); Assert.False(watcher.Latest.IsDisposed);
        }
        finally
        {
            writer.Gate.Open();
            await write;
            await disposing;
        }
    }

    [Fact]
    public async Task WhenAnOpenIsAskedWhileTheDisposalWaitsForAWrite_ShouldRefuseIt()
    {
        var (workspace, writer, _, _, write, disposing) = await DisposingWhileAWriteRunsAsync();
        try
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => workspace.OpenAsync("other", CancellationToken.None));
        }
        finally
        {
            writer.Gate.Open();
            await write;
            await disposing;
        }
    }

    [Fact]
    public async Task WhenTheWriteFinishesDuringTheDisposal_ShouldStillReportItsVerifiedOutcome()
    {
        var (_, writer, _, _, write, disposing) = await DisposingWhileAWriteRunsAsync();
        writer.Gate.Open();
        var result = await write;
        Assert.Equal(GitWriteOutcome.Succeeded, result.Outcome); Assert.NotNull(result.Snapshot);
        await disposing;
    }

    [Fact]
    public async Task WhenTheDisposalFinishesAfterTheWrite_ShouldReleaseTheSession()
    {
        var (workspace, writer, watcher, _, write, disposing) = await DisposingWhileAWriteRunsAsync();
        writer.Gate.Open();
        var result = await write;
        await disposing;
        Assert.True(watcher.Latest.IsDisposed); Assert.False(workspace.IsCurrent(result.Snapshot!));
    }

    [Fact]
    public async Task WhenAWriteArrivesWhileTheDisposalWaitsForAWrite_ShouldRejectItAtOnceWithoutRunningIt()
    {
        var (workspace, writer, _, opened, first, disposing) = await DisposingWhileAWriteRunsAsync();
        try
        {
            var unwanted = workspace.WriteAsync(new StageRequest(opened, []), default);
            Assert.True(unwanted.IsCompleted);
            Assert.Equal(GitWriteOutcome.Rejected, (await unwanted).Outcome);
            Assert.Single(writer.Requests);
        }
        finally
        {
            writer.Gate.Open();
            await first;
            await disposing;
        }
    }

    [Fact]
    public async Task WhenAReadFailsWhileTheSessionIsCurrent_ShouldSurfaceTheFailure()
    {
        var reader = new FakeRepositoryReader();
        await using var workspace = new RepositoryWorkspace(reader);
        await workspace.OpenAsync("fixture", CancellationToken.None);
        reader.Failure = new RepositoryException(RepositoryError.ChangedDuringRead);
        var error = await Assert.ThrowsAsync<RepositoryException>(() => workspace.RefreshAsync(CancellationToken.None));
        Assert.Equal(RepositoryError.ChangedDuringRead, error.Error);
    }

    // The same path opened twice, each open with a watcher source of its own.
    private static async Task<(RepositoryWorkspace Workspace, FakeRepositoryWatcher Watcher)> ReopenedAsync()
    {
        var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(new FakeRepositoryReader(), watcher);
        await workspace.OpenAsync("fixture", CancellationToken.None);
        await workspace.OpenAsync("fixture", CancellationToken.None);
        return (workspace, watcher);
    }

    // A write accepted while a refresh is held at the reader; the refresh is let go while the write is held at the writer.
    private static async Task<(RepositoryWorkspace Workspace, FakeGitWriter Writer, FakeRepositoryWatcher Watcher, Task<RepositorySnapshot?> Refresh,
        Task<GitWriteResult> Write)> WriteDuringARefreshAsync()
    {
        var reader = new FakeRepositoryReader(); var writer = new FakeGitWriter(); var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        reader.Gate.IsClosed = true;
        var refresh = workspace.RefreshAsync(CancellationToken.None);
        await reader.Gate.Started;
        writer.Gate.IsClosed = true;
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        reader.Gate.Open();
        return (workspace, writer, watcher, refresh, write);
    }

    // An open held at the reader and a refresh that arrives while it waits, both let go together.
    private static async Task<(RepositoryWorkspace Workspace, FakeRepositoryWatcher Watcher, RepositorySnapshot?[] Results)> OpenOvertakenByARefreshAsync()
    {
        var reader = new FakeRepositoryReader { Gate = { IsClosed = true } }; var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher);
        var open = workspace.OpenAsync("fixture", CancellationToken.None);
        await reader.Gate.Started;
        var refresh = workspace.RefreshAsync(CancellationToken.None);
        await reader.Gate.Arrival(2);
        reader.Gate.Open();
        var results = await Task.WhenAll(open, refresh);
        return (workspace, watcher, results);
    }

    // A write held at the writer when the workspace is disposed.
    private static async Task<(RepositoryWorkspace Workspace, FakeGitWriter Writer, FakeRepositoryWatcher Watcher, RepositorySnapshot Opened,
        Task<GitWriteResult> Write, Task Disposing)> DisposingWhileAWriteRunsAsync()
    {
        var reader = new FakeRepositoryReader(); var writer = new FakeGitWriter { Gate = { IsClosed = true } }; var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher, writer: writer);
        var opened = await workspace.OpenAsync("fixture", CancellationToken.None);
        var write = workspace.WriteAsync(new StageRequest(opened!, [Snapshots.Path("a")]), CancellationToken.None);
        await writer.Gate.Started;
        var disposing = workspace.DisposeAsync().AsTask();
        return (workspace, writer, watcher, opened!, write, disposing);
    }
}
