using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Ports.Workspace;

public interface IRepositoryWorkspace : IAsyncDisposable
{
    Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken ct);
    Task<RepositorySnapshot?> RefreshAsync(CancellationToken ct);
    Task CloseAsync();
    Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken ct);
}
