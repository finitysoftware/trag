using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;

namespace Trag.Infrastructure.Platform.Storage;

// The catalog of a launch that has nowhere to write: the list lives as long as the process does. A probe run gets
// this, so it can add and remove links without touching the owner's own repositories.json. Each save replaces the
// whole list in one step, so a reader sees the previous list or the new one and never a mixture.
public sealed class MemoryRepositoryCatalogStore : IRepositoryCatalogStore
{
    private volatile RepositoryEntry[] memory = [];

    public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<RepositoryEntry>>(memory);
    }

    public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        memory = RepositoryEntrySanitizer.Sanitize(change(memory));
        return Task.FromResult<IReadOnlyList<RepositoryEntry>>(memory);
    }
}
