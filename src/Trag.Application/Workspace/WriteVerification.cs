using Trag.Application.Ports.Git;
using Trag.Domain.Writes;

namespace Trag.Application.Workspace;

// After a write, the repository is read again so the result carries the state the write left behind. Verification is
// owned by the operation, even after its cancel button was pressed; it has its own budget of 15 seconds.
public sealed class WriteVerification(IRepositoryReader reader, WorkspaceState state, TimeSpan? budget = null)
{
    public async Task<GitWriteResult> VerifyAsync(RepositorySlot slot, GitWriteResult result)
    {
        using var recovery = new CancellationTokenSource(budget ?? TimeSpan.FromSeconds(15));
        try
        {
            var snapshot = await reader.ReadAsync(slot.Lease.Path, recovery.Token).ConfigureAwait(false);
            state.SetIfCurrent(slot, snapshot);
            return result with { Snapshot = snapshot };
        }
        catch (Exception)
        {
            return result.WithoutFinalStatus();
        }
    }
}
