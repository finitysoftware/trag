using Trag.Application.Ports.Storage;

namespace Trag.TestSupport;

public sealed class FakeLastRepositoryStore : ILastRepositoryStore
{
    public string? Path { get; set; }
    public Exception? SaveFailure { get; set; }
    public Func<CancellationToken, Task<string?>>? Load { get; set; }
    // Runs before a save takes effect or fails: a test holds a save there.
    public Func<Task>? Saving { get; set; }
    public int Saves { get; private set; }

    public Task<string?> LoadAsync(CancellationToken cancellationToken) => Load?.Invoke(cancellationToken) ?? Task.FromResult(Path);

    public async Task SaveAsync(string? path, CancellationToken cancellationToken)
    {
        Saves++;
        if (Saving is not null) await Saving();
        if (SaveFailure is not null) throw SaveFailure;
        Path = path;
    }
}
