using Trag.Application.Ports.Storage;
using Trag.Domain.Presentation;

namespace Trag.Infrastructure.Platform.Storage;

public sealed class LayoutStore(string path) : ILayoutStore
{
    private readonly JsonFileStore<SavedLayout> file =
        new(path, StorageJsonContext.Default.SavedLayout, 4096);

    public async Task<SavedLayout> LoadLayoutAsync(CancellationToken ct) => await file.LoadAsync(ct).ConfigureAwait(false) ?? new SavedLayout();

    public Task SaveLayoutAsync(SavedLayout preferences, CancellationToken ct) => file.SaveAsync(preferences, ct);
}
