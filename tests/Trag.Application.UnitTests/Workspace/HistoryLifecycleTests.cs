using Trag.Application.Workspace;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

public sealed class HistoryLifecycleTests
{
    [Fact]
    public async Task WhenARefreshFindsANewHeadWhileHistoryIsRead_ShouldCancelTheRead()
    {
        const bool headChanged = true;
        var (workspace, history, pending) = await RefreshWhileHistoryIsReadAsync(headChanged);
        await using var _ = workspace;
        Assert.Equal(headChanged, history.Gate.Token.IsCancellationRequested);
        history.Gate.Open();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task WhenARefreshFindsOnlyWorktreeChangesWhileHistoryIsRead_ShouldLetTheReadFinish()
    {
        const bool headChanged = false;
        var (workspace, history, pending) = await RefreshWhileHistoryIsReadAsync(headChanged);
        await using var _ = workspace;
        Assert.Equal(headChanged, history.Gate.Token.IsCancellationRequested);
        history.Gate.Open();
        Assert.NotNull(await pending);
    }

    // The details reader is told what the snapshot knows: whether the branch has commits, and whether Git LFS files are there.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WhenTheDetailsAreRead_ShouldTellTheReaderWhatTheSnapshotKnows(bool unborn, bool lfsFiles)
    {
        FakeRepositoryReader reader = new() { Snapshot = Snapshots.Create(unborn: unborn) with { HasLfsFiles = lfsFiles } }; FakeHistoryReader history = new();
        await using var workspace = new RepositoryWorkspace(reader, historyReader: history);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;

        await workspace.ReadDetailsAsync(snapshot, default);

        Assert.Equal((unborn, lfsFiles), history.LastDetails);
    }

    [Fact]
    public async Task WhenTheBranchIsUnborn_ShouldShowAnEmptyHistoryWithoutStartingGit()
    {
        FakeRepositoryReader reader = new() { Snapshot = Snapshots.Create(unborn: true) }; FakeHistoryReader history = new();
        await using var workspace = new RepositoryWorkspace(reader, historyReader: history);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;
        Assert.Empty((await workspace.ReadHistoryAsync(snapshot, new(), default))!.Commits);
        Assert.False(history.Gate.Started.IsCompleted);
    }

    // A history read is held at its gate while the workspace refreshes, the head moved first when asked.
    private static async Task<(RepositoryWorkspace Workspace, FakeHistoryReader History, Task<HistoryPage?> Pending)> RefreshWhileHistoryIsReadAsync(bool headChanged)
    {
        FakeRepositoryReader reader = new(); FakeHistoryReader history = new();
        history.Gate.IsClosed = true;
        var workspace = new RepositoryWorkspace(reader, historyReader: history);
        var snapshot = (await workspace.OpenAsync("fixture", default))!;
        var pending = workspace.ReadHistoryAsync(snapshot, new(), default);
        await history.Gate.Started;
        if (headChanged) reader.Snapshot = reader.Snapshot with { Head = Snapshots.Id('b') };
        await workspace.RefreshAsync(default);
        return (workspace, history, pending);
    }
}
