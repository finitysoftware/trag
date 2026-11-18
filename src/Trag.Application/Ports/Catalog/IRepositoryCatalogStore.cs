using Trag.Domain.Catalog;

namespace Trag.Application.Ports.Catalog;

public interface IRepositoryCatalogStore
{
    Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct);
    // A change made to the list as it is stored now, which another instance of Trag may have changed; the answer is the
    // list as saved.
    Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct);
}
