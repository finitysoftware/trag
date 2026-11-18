using Trag.Application.Ports.Storage;

namespace Trag.Infrastructure.Platform.Storage;

// The chosen git.exe in git.json beside the settings.
public sealed class GitChoiceStore(string path) : IGitChoiceStore
{
    private readonly JsonFileStore<GitChoiceSettings> file =
        new(path, StorageJsonContext.Default.GitChoiceSettings, 64 * 1024);

    public async Task<string?> LoadAsync(CancellationToken ct) => (await file.LoadAsync(ct).ConfigureAwait(false))?.Executable;

    public Task SaveAsync(string? chosen, CancellationToken ct) => file.SaveAsync(new(chosen), ct);
}
