using Trag.Application.Ports.Catalog;
using Trag.Domain.Catalog;

namespace Trag.Infrastructure.Platform.Storage;

// Trag/repositories.json next to settings.json. Entries are links only; nothing here can delete a working copy. The
// file is replaced rather than edited, and one save at a time, so a crash never leaves half a list.
public sealed class RepositoryCatalogStore(string path) : IRepositoryCatalogStore
{
    private readonly JsonFileStore<RepositoryCatalogDocument> file =
        new(path, StorageJsonContext.Default.RepositoryCatalogDocument, 1024 * 1024);

    public async Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) =>
        RepositoryEntrySanitizer.Sanitize((await file.LoadAsync(ct).ConfigureAwait(false))?.Repositories ?? []);

    public async Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct) =>
        (await file.UpdateAsync(document => new(RepositoryEntrySanitizer.Sanitize(change(RepositoryEntrySanitizer.Sanitize(document?.Repositories ?? [])))), ct)
            .ConfigureAwait(false)).Repositories;
}
