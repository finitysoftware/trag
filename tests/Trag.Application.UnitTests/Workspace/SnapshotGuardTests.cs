using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

// SnapshotGuard answers null whenever the caller could not apply the result; WorkspaceState is exercised through it.
public sealed class SnapshotGuardTests
{
    // A workspace without the backend answers nothing; an answer that is itself absent, or a value, comes through as it is.
    [Fact]
    public async Task WhenTheBackendIsMissingOrTheAnswerAbsentOrAValue_ShouldAnswerWhatTheReadSaid()
    {
        var (state, _, expected) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);

        Assert.Null(await guard.ReadAsync((string?)null, expected, (_, _) => Task.FromResult<string?>("never"), CancellationToken.None));
        Assert.Null(await guard.ReadAsync("backend", expected, (_, _) => Task.FromResult<string?>(null), CancellationToken.None));
        Assert.Equal("read", await guard.ReadAsync("backend", expected, (_, _) => Task.FromResult<string?>("read"), CancellationToken.None));
        Assert.Equal(3, await guard.ReadValueAsync("backend", expected, (_, _) => Task.FromResult<int?>(3), CancellationToken.None));
        Assert.Null(await guard.ReadValueAsync("backend", expected, (_, _) => Task.FromResult<int?>(null), CancellationToken.None));
        Assert.Null(await guard.ReadValueAsync((string?)null, expected, (_, _) => Task.FromResult<int?>(3), CancellationToken.None));
    }

    private static readonly string OtherHead = new('b', 40);

    [Fact]
    public async Task WhenTheReadIsForTheShownSnapshot_ShouldReturnItsResult()
    {
        var (state, _, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        Assert.Equal("ok", await guard.ReadAsync(shown, _ => Task.FromResult("ok"), CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheReadIsForAnotherHead_ShouldAnswerNullWithoutStartingIt()
    {
        var (state, _, _) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        var started = false;
        Assert.Null(await guard.ReadAsync(Snapshots.Create(head: OtherHead), _ => { started = true; return Task.FromResult("no"); }, CancellationToken.None));
        Assert.False(started);
    }

    [Fact]
    public async Task WhenTheReadIsAskedDuringAWrite_ShouldAnswerNull()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        state.TryBeginWrite(shown.Root, out _);
        Assert.Null(await guard.ReadAsync(shown, _ => Task.FromResult("no"), CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheReadIsAskedAfterAWriteEnded_ShouldReturnItsResult()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        state.TryBeginWrite(shown.Root, out _);
        state.EndWrite(slot);
        Assert.Equal("yes", await guard.ReadAsync(shown, _ => Task.FromResult("yes"), CancellationToken.None));
    }

    [Fact]
    public async Task WhenOnlyTheWorktreeChangesDuringTheRead_ShouldKeepTheResult()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = guard.ReadAsync(shown, async _ => { await gate.Task; return "ok"; }, CancellationToken.None);
        Assert.NotNull(state.Accept(slot, slot.Lease.Invalidate(), Snapshots.Create(files: [Snapshots.Modified("a")]), null));
        gate.SetResult();
        Assert.Equal("ok", await read);
    }

    [Fact]
    public async Task WhenANewHeadIsAcceptedDuringTheRead_ShouldCancelIt()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        var seen = CancellationToken.None;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = guard.ReadAsync(shown, async ct => { seen = ct; entered.SetResult(); await gate.Task; return "stale"; }, CancellationToken.None);
        await entered.Task;
        Assert.NotNull(state.Accept(slot, slot.Lease.Invalidate(), Snapshots.Create(head: OtherHead), null));
        Assert.True(seen.IsCancellationRequested);
        gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public async Task WhenAWriteStartsDuringTheRead_ShouldDropTheResult()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = guard.ReadAsync(shown, async _ => { await gate.Task; return "late"; }, CancellationToken.None);
        state.TryBeginWrite(shown.Root, out _);
        gate.SetResult();
        Assert.Null(await read);
    }

    [Fact]
    public async Task WhenTheSessionIsReplacedDuringTheRead_ShouldDropTheResult()
    {
        var (state, _, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = guard.ReadAsync(shown, async _ => { await gate.Task; return "late"; }, CancellationToken.None);
        state.Replace("other", out _);
        gate.SetResult();
        Assert.Null(await read);
    }

    [Fact]
    public async Task WhenTheCallerCancels_ShouldCancelTheRead()
    {
        var (state, _, shown) = OpenedSlot.Create();
        var guard = new SnapshotGuard(state);
        using var cancellation = new CancellationTokenSource();
        var read = guard.ReadAsync(shown, async ct => { await Task.Delay(Timeout.Infinite, ct); return "never"; }, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public void WhenAWriteIsForAnotherRoot_ShouldNotBegin()
    {
        var (state, _, _) = OpenedSlot.Create();
        Assert.False(state.TryBeginWrite("elsewhere", out _));
    }

    [Fact]
    public void WhenAWriteIsForTheShownRoot_ShouldBeginOnItsSlotAndHoldTheSession()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        Assert.True(state.TryBeginWrite(shown.Root, out var writing));
        Assert.Same(slot, writing); Assert.NotNull(slot.Write);
    }

    [Fact]
    public void WhenAWriteHoldsTheSession_ShouldNotBeginAnother()
    {
        var (state, _, shown) = Writing();
        Assert.False(state.TryBeginWrite(shown.Root, out _));
    }

    [Fact]
    public void WhenAWriteHoldsTheSession_ShouldRefuseToReplaceOrDetachIt()
    {
        var (state, _, _) = Writing();
        Assert.Throws<InvalidOperationException>(() => state.Replace("other", out _));
        Assert.Throws<InvalidOperationException>(() => state.Detach());
    }

    [Fact]
    public void WhenTheWriteEnds_ShouldHandBackTheChangeSourceAndReleaseTheSession()
    {
        var (state, slot, _) = Writing();
        var source = new FakeChangeSource();
        slot.Changes = source;
        Assert.Same(source, state.EndWrite(slot));
        Assert.Null(slot.Write);
    }

    [Fact]
    public void WhenTheSessionIsDetachedAfterTheWriteEnded_ShouldLetItGo()
    {
        var (state, slot, shown) = Writing();
        state.EndWrite(slot);
        Assert.Same(slot, state.Detach());
        Assert.False(state.Holds(shown));
    }

    // A write begun on the shown root, holding the session until it ends.
    private static (WorkspaceState State, RepositorySlot Slot, RepositorySnapshot Shown) Writing()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        state.TryBeginWrite(shown.Root, out _);
        return (state, slot, shown);
    }
}
