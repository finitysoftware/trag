using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.Application.Ports.Git;

public interface IGitSyncReader
{
    Task<RepositorySyncState> ReadAsync(RepositorySnapshot snapshot, string remote, string? branch, CancellationToken ct);
}
