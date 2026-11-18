namespace Trag.Application.UnitTests.Workspace;

// The session's slot while a write holds it: closing waits for the write, so the repository the write began on stays
// the current one until the write lets it go.
public sealed class WorkspaceStateTests
{
    [Fact]
    public void WhenDetachIsAskedWhileAWriteHoldsTheSlot_ShouldThrowAndLeaveTheSlotCurrent()
    {
        var (state, slot, shown) = OpenedSlot.Create();
        Assert.True(state.TryBeginWrite(shown.Root, out var writing));

        Assert.Throws<InvalidOperationException>(() => state.Detach());

        Assert.Same(slot, state.Current);
        state.EndWrite(writing);
        Assert.Same(slot, state.Detach());
        Assert.Null(state.Current);
    }
}
