using Trag.Application.Ports.Watching;
using Trag.Domain.Status;

namespace Trag.Application.Workspace;

// Accepts a read result into its slot and, the first time, starts watching the repository it describes.
public sealed class WatcherAttachment(IRepositoryWatcher? watcher, WorkspaceState state)
{
    public async Task<RepositorySnapshot?> AcceptAsync(RepositorySlot slot, long version, RepositorySnapshot snapshot)
    {
        IRepositoryChangeSource? source = null;
        if (watcher is not null && slot.Changes is null)
            source = await Task.Run(() => watcher.Watch(snapshot)).ConfigureAwait(false);
        return state.Accept(slot, version, snapshot, source);
    }
}
