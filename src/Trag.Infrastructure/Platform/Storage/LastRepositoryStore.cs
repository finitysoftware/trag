using Trag.Application.Ports.Storage;

namespace Trag.Infrastructure.Platform.Storage;

public sealed class LastRepositoryStore(string settingsPath) : ILastRepositoryStore
{
    private readonly JsonFileStore<LastRepositorySettings> file =
        new(settingsPath, StorageJsonContext.Default.LastRepositorySettings, 64 * 1024);

    public async Task<string?> LoadAsync(CancellationToken ct)
    {
        var settings = await file.LoadAsync(ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(settings?.LastRepository) ? null : settings.LastRepository;
    }

    public Task SaveAsync(string? path, CancellationToken ct) => file.SaveAsync(new(path), ct);
}
