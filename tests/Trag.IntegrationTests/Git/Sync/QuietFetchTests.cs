using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using static Trag.IntegrationTests.Git.Sync.SyncFixture;

namespace Trag.IntegrationTests.Git.Sync;

// The fetch Trag runs on its own, with real Git between two clones of one origin: it brings what the peer pushed into
// the remote-tracking ref beside the shown state, touches neither HEAD nor the files, and the next snapshot sees the
// moved ref; it does not start for a snapshot that is no longer the shown one.
public sealed class QuietFetchTests
{
    [Fact]
    public async Task WhenAQuietFetchBringsAPeersPush_ShouldMoveOnlyTheRemoteTrackingRef()
    {
        using var f = await SyncFixture.Create();
        await using var workspace = new RepositoryWorkspace(f.Reader, synchronizer: f.Sync);
        var (shown, _, head, peer, result) = await FetchQuietlyAfterAPeerPush(f, workspace);
        Assert.True(result is { Outcome: GitWriteOutcome.Succeeded }, result?.Message);
        Assert.Equal(peer, await f.Git(f.One, "rev-parse", "refs/remotes/origin/trunk"));
        Assert.Equal(head, await f.Head(f.One));
        var after = await f.Reader.ReadAsync(f.One, f.Token);
        Assert.Empty(after.Files); Assert.Equal(peer, after.UpstreamHead); Assert.False(after.HasSameRemotes(shown));
        Assert.Equal(1, (await f.Sync.ReadAsync(after, "origin", null, f.Token)).Behind);
    }

    [Fact]
    public async Task WhenTheSnapshotIsNoLongerTheShownOne_ShouldNotStartAQuietFetch()
    {
        using var f = await SyncFixture.Create();
        await using var workspace = new RepositoryWorkspace(f.Reader, synchronizer: f.Sync);
        var (shown, target, _, peer, _) = await FetchQuietlyAfterAPeerPush(f, workspace);

        Assert.Null(await workspace.FetchQuietlyAsync(shown with { Head = peer }, target, null, f.Token));
    }

    // The shell gives a quiet fetch way when the user's own write begins: it cancels the fetch and sends the write at once,
    // here while Git still holds the lock of the tracking ref the fetch moves. The pull waits for the fetch to end and finds
    // no lock to fail on, and none is left behind.
    [Fact]
    public async Task WhenTheUserPullsWhileAQuietFetchStillRuns_ShouldWaitOrRefuseWithoutARefLockError()
    {
        using var f = await SyncFixture.Create();
        await using var workspace = new RepositoryWorkspace(f.Reader, writer: new GitWriteRouter(new(), f.Sync), synchronizer: f.Sync);
        var held = Path.Combine(f.Root, "held");
        await f.Hook(f.One, "reference-transaction", SyncInterruption.Holding("refs/remotes/origin/trunk", held));
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        var shown = (await workspace.OpenAsync(f.One, f.Token))!;
        var target = await f.Sync.ReadAsync(shown, "origin", null, f.Token);
        using var quiet = CancellationTokenSource.CreateLinkedTokenSource(f.Token);
        var fetching = workspace.FetchQuietlyAsync(shown, target, null, quiet.Token);
        await WaitFor(held, f.Token);

        quiet.Cancel();
        var pull = await workspace.WriteAsync(new SyncRequest(shown, target, SyncAction.Pull), f.Token);

        Assert.True(pull.Outcome == GitWriteOutcome.Succeeded, pull.Message + pull.Details);
        Assert.Equal(await f.Head(f.Two), await f.Head(f.One));
        Assert.Equal(GitWriteOutcome.Cancelled, (await fetching)?.Outcome);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(f.One, ".git"), "*.lock", SearchOption.AllDirectories));
    }

    // Opens One, lets the peer push a commit behind its back and fetches quietly over the snapshot shown before the push.
    private static async Task<(RepositorySnapshot Shown, RepositorySyncState Target, string Head, string Peer, GitWriteResult? Result)>
        FetchQuietlyAfterAPeerPush(SyncFixture f, RepositoryWorkspace workspace)
    {
        var shown = (await workspace.OpenAsync(f.One, f.Token))!;
        var head = await f.Head(f.One);
        await f.Commit(f.Two, "from peer"); await f.Git(f.Two, "push", "origin", "trunk");
        var peer = await f.Head(f.Two);

        var target = await f.Sync.ReadAsync(shown, "origin", null, f.Token);
        var result = await workspace.FetchQuietlyAsync(shown, target, null, f.Token);
        return (shown, target, head, peer, result);
    }
}
