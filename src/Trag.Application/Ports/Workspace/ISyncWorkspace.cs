using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.Application.Ports.Workspace;

public interface ISyncWorkspace
{
    Task<RepositorySyncState?> ReadSyncAsync(RepositorySnapshot expected, string remote, string? branch, CancellationToken ct);

    // A fetch nobody is waiting for: it runs beside the shown state rather than as a write, only while the snapshot is
    // still the shown one. Null when it could not start.
    Task<GitWriteResult?> FetchQuietlyAsync(RepositorySnapshot expected, RepositorySyncState target, string? accountId, CancellationToken ct);
}
