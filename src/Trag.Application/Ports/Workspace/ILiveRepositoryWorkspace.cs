using Trag.Application.Live;
using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Status;

namespace Trag.Application.Ports.Workspace;

public interface ILiveRepositoryWorkspace
{
    IAsyncEnumerable<RepositoryUpdate> WatchAsync(CancellationToken ct);
    bool IsCurrent(RepositorySnapshot snapshot);
    Task<FileDiff?> ReadDiffAsync(FileStatus file, bool fromIndex, bool preview, CancellationToken ct);
    Task<ConflictText?> ReadConflictAsync(FileStatus file, CancellationToken ct);
}
