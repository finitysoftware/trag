using Trag.Application.Live;

namespace Trag.Application.Ports.Watching;

public interface IRepositoryChangeSource : IDisposable
{
    // True while the source polls because the file system watcher is unavailable.
    bool IsPolling { get; }
    Task<RepositoryChange> ReadAsync(CancellationToken ct);
    void Reconcile();
}
