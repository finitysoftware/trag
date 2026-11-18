using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class WorkspaceTests
{
    [Fact]
    public async Task WhenAnotherRepositoryOpensDuringAnOpen_ShouldCancelTheOldReadAndOpenTheNewOne()
    {
        var (workspace, reader, _, _) = await OpeningFirstAsync();
        await using var _ = workspace;
        var second = await workspace.OpenAsync("second", CancellationToken.None);
        Assert.True(reader.Gate.FirstToken.IsCancellationRequested);
        Assert.Equal("second", second!.Root);
    }

    [Fact]
    public async Task WhenAnOpenOvertakenByAnotherRepositorySucceedsLate_ShouldAnswerNull()
    {
        var (workspace, _, completion, first) = await OpeningFirstAsync();
        await using var _ = workspace;
        await workspace.OpenAsync("second", CancellationToken.None);
        completion.SetResult(Snapshots.Create(root: "first"));
        Assert.Null(await first);
    }

    [Fact]
    public async Task WhenAnOpenFailsLateAfterTheRepositoryClosed_ShouldAnswerNullInsteadOfTheFailure()
    {
        var (workspace, _, completion, first) = await OpeningFirstAsync();
        await using var _ = workspace;
        await workspace.CloseAsync();
        completion.SetException(new IOException("old failure"));
        Assert.Null(await first);
    }

    [Fact]
    public async Task WhenTheRepositoryClosedDuringAnOpen_ShouldHaveNothingToRefresh()
    {
        var (workspace, _, completion, first) = await OpeningFirstAsync();
        await using var _ = workspace;
        await workspace.CloseAsync();
        completion.SetException(new IOException("old failure"));
        await first;
        Assert.Null(await workspace.RefreshAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenARefreshOvertakesAnOpenWhoseReadHangs_ShouldAnswerWithItsOwnRead()
    {
        var (workspace, _, _, _) = await OpeningFirstAsync();
        await using var _ = workspace;
        var current = await workspace.RefreshAsync(CancellationToken.None);
        Assert.Equal("first", current!.Root);
    }

    [Fact]
    public async Task WhenAnOpenOvertakenByARefreshSucceedsLate_ShouldAnswerNull()
    {
        var (workspace, _, completion, first) = await OpeningFirstAsync();
        await using var _ = workspace;
        await workspace.RefreshAsync(CancellationToken.None);
        completion.SetResult(Snapshots.Create(root: "old"));
        Assert.Null(await first);
    }

    [Fact]
    public async Task WhenAnOpenOvertakenByARefreshFailsLate_ShouldAnswerNull()
    {
        var (workspace, _, completion, first) = await OpeningFirstAsync();
        await using var _ = workspace;
        await workspace.RefreshAsync(CancellationToken.None);
        completion.SetException(new IOException("late"));
        Assert.Null(await first);
    }

    [Fact]
    public async Task WhenTheCallerCancelsAnOpenThatSucceedsAnyway_ShouldReportTheCancel()
    {
        using var cancellation = new CancellationTokenSource();
        var (workspace, _, completion, first) = await OpeningFirstAsync(cancellation.Token);
        await using var _ = workspace;
        cancellation.Cancel();
        completion.SetResult(Snapshots.Create(root: "first"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
    }

    // An open of "first" whose read hangs until the test answers it through the completion.
    private static async Task<(RepositoryWorkspace Workspace, FakeRepositoryReader Reader, TaskCompletionSource<RepositorySnapshot> Completion,
        Task<RepositorySnapshot?> First)> OpeningFirstAsync(CancellationToken ct = default)
    {
        TaskCompletionSource<RepositorySnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Delayed(completion);
        var workspace = new RepositoryWorkspace(reader);
        var first = workspace.OpenAsync("first", ct);
        await reader.Gate.Started;
        return (workspace, reader, completion, first);
    }

    // The first read hangs until the test answers it, so a second open or a refresh can overtake it; every later read
    // answers at once. The gate keeps the first read's token, which is how a test proves that read was cancelled.
    private static FakeRepositoryReader Delayed(TaskCompletionSource<RepositorySnapshot> completion)
    {
        FakeRepositoryReader reader = new();
        reader.Read = (path, _) => reader.Calls == 1 ? completion.Task : Task.FromResult(Snapshots.Create(root: path));
        return reader;
    }
}
