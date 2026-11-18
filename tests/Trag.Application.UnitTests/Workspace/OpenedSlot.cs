using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Workspace;

// A workspace state with one current slot that already shows a snapshot, the starting point of the service tests.
internal static class OpenedSlot
{
    public static (WorkspaceState State, RepositorySlot Slot, RepositorySnapshot Shown) Create(RepositorySnapshot? snapshot = null)
    {
        var state = new WorkspaceState();
        var slot = state.Replace("fixture", out _);
        var shown = state.Accept(slot, slot.Lease.Invalidate(), snapshot ?? Snapshots.Create(), null)!;
        return (state, slot, shown);
    }
}
