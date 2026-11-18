using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class RepositoryWriteLifecycleTests
{
    [Fact]
    public async Task WhenDisposedWhileACancelledWriteIsVerified_ShouldWaitForTheVerificationWithoutCancellingIt()
    {
        using var cancellation = new CancellationTokenSource();
        var (reader, watcher, _, write, disposing) = await DisposingWhileACancelledWriteIsVerifiedAsync(cancellation);
        try
        {
            Assert.False(write.IsCompleted);
            Assert.False(disposing.IsCompleted);
            Assert.False(reader.Gate.Token.IsCancellationRequested);
            Assert.False(watcher.Latest.IsDisposed);
        }
        finally
        {
            reader.Gate.Open();
            await disposing;
        }
    }

    [Fact]
    public async Task WhenACloseIsAskedWhileTheDisposalWaitsForAVerification_ShouldRefuseIt()
    {
        using var cancellation = new CancellationTokenSource();
        var (reader, _, workspace, _, disposing) = await DisposingWhileACancelledWriteIsVerifiedAsync(cancellation);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CloseAsync());
        }
        finally
        {
            reader.Gate.Open();
            await disposing;
        }
    }

    [Fact]
    public async Task WhenTheVerificationOfACancelledWriteFinishes_ShouldReportTheWriteUncertainWithTheSnapshotItRead()
    {
        using var cancellation = new CancellationTokenSource();
        var (reader, _, _, write, disposing) = await DisposingWhileACancelledWriteIsVerifiedAsync(cancellation);
        reader.Gate.Open();
        await disposing;

        var result = await write;
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public async Task WhenTheVerificationOfACancelledWriteFinishes_ShouldThenReleaseTheSession()
    {
        using var cancellation = new CancellationTokenSource();
        var (reader, watcher, workspace, write, disposing) = await DisposingWhileACancelledWriteIsVerifiedAsync(cancellation);
        reader.Gate.Open();
        await disposing;

        var result = await write;
        Assert.NotNull(result.Snapshot);
        Assert.True(watcher.Latest.IsDisposed);
        Assert.False(workspace.IsCurrent(result.Snapshot));
    }

    // A local write and a transfer, because the workspace holds the session the same way whatever they are.
    public static TheoryData<Func<RepositorySnapshot, GitWriteRequest>> Writes => new()
    {
        expected => new StageRequest(expected, []),
        expected => new SyncRequest(expected, FakeGitSync.DefaultState(expected, "origin", null), SyncAction.Fetch),
        expected => new SyncRequest(expected, FakeGitSync.DefaultState(expected, "origin", null), SyncAction.Push)
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task WhenAWriteIsRunning_ShouldRefuseTheNextOneWithoutRunningIt(Func<RepositorySnapshot, GitWriteRequest> build)
    {
        var (workspace, writer, _, snapshot) = await RunningAsync(build);
        await using var _ = workspace;
        var next = await workspace.WriteAsync(new UnstageRequest(snapshot, []), default);
        Assert.Equal(GitWriteOutcome.Rejected, next.Outcome);
        Assert.Single(writer.Requests);
        writer.Gate.Open();
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task WhenAWriteIsRunning_ShouldRefuseToCloseOrOpenAndRefreshNothing(Func<RepositorySnapshot, GitWriteRequest> build)
    {
        var (workspace, writer, _, _) = await RunningAsync(build);
        await using var _ = workspace;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CloseAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.OpenAsync("other", default));
        Assert.Null(await workspace.RefreshAsync(default));
        writer.Gate.Open();
    }

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task WhenTheRunningWriteFinishes_ShouldShowTheSnapshotItsVerificationRead(Func<RepositorySnapshot, GitWriteRequest> build)
    {
        var (workspace, writer, first, _) = await RunningAsync(build);
        await using var _ = workspace;
        var result = await FinishFirstAsync(writer, first);
        Assert.NotNull(result.Snapshot);
        Assert.True(workspace.IsCurrent(result.Snapshot));
    }

    // The close is the check: it throws while a write still holds the session.
    [Theory]
    [MemberData(nameof(Writes))]
    public async Task WhenTheWriteIsDone_ShouldLetTheRepositoryClose(Func<RepositorySnapshot, GitWriteRequest> build)
    {
        var (workspace, writer, first, _) = await RunningAsync(build);
        await using var _ = workspace;
        await FinishFirstAsync(writer, first);
        await workspace.CloseAsync();
    }

    [Fact]
    public async Task WhenTheFinalStatusCannotBeReadAfterASuccessfulWrite_ShouldReportItUncertainButKeepTheCommit()
    {
        FakeRepositoryReader reader = new(); FakeGitWriter writer = new();
        writer.Gate.IsClosed = true;
        await using var workspace = new RepositoryWorkspace(reader, writer: writer);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;
        var pending = workspace.WriteAsync(new CommitRequest(snapshot, "message"), default);
        await writer.Gate.Started;
        reader.Failure = new IOException();
        writer.Result = new(GitWriteOutcome.Succeeded, "Created", CommitId: "commit");
        writer.Gate.Open();
        var result = await pending;
        Assert.Equal(GitWriteOutcome.Uncertain, result.Outcome);
        Assert.Equal("commit", result.CommitId);
        Assert.Null(result.Snapshot);
    }

    // A write whose caller cancelled it while it ran; its verification read is held at the reader when the workspace is
    // disposed.
    private static async Task<(FakeRepositoryReader Reader, FakeRepositoryWatcher Watcher, RepositoryWorkspace Workspace, Task<GitWriteResult> Write,
        Task Disposing)> DisposingWhileACancelledWriteIsVerifiedAsync(CancellationTokenSource cancellation)
    {
        var reader = new FakeRepositoryReader();
        var writer = new FakeGitWriter { Gate = { IsClosed = true }, Failure = new OperationCanceledException() };
        var watcher = new FakeRepositoryWatcher();
        var workspace = new RepositoryWorkspace(reader, watcher, writer: writer);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;
        var write = workspace.WriteAsync(new StageRequest(snapshot, []), cancellation.Token);
        await writer.Gate.Started;
        reader.Gate.IsClosed = true;
        cancellation.Cancel();
        writer.Gate.Open();
        await reader.Gate.Arrival(2);

        var disposing = workspace.DisposeAsync().AsTask();
        return (reader, watcher, workspace, write, disposing);
    }

    // A write held at the writer while it runs, over the snapshot it was asked for.
    private static async Task<(RepositoryWorkspace Workspace, FakeGitWriter Writer, Task<GitWriteResult> First, RepositorySnapshot Snapshot)>
        RunningAsync(Func<RepositorySnapshot, GitWriteRequest> build)
    {
        FakeRepositoryReader reader = new(); FakeGitWriter writer = new();
        writer.Gate.IsClosed = true;
        var workspace = new RepositoryWorkspace(reader, writer: writer);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;
        var first = workspace.WriteAsync(build(snapshot), default);
        await writer.Gate.Started;
        return (workspace, writer, first, snapshot);
    }

    // The held write finishes as a success.
    private static Task<GitWriteResult> FinishFirstAsync(FakeGitWriter writer, Task<GitWriteResult> first)
    {
        writer.Result = new(GitWriteOutcome.Succeeded, "Finished");
        writer.Gate.Open();
        return first;
    }
}
