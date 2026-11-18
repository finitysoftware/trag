using Trag.Application.Ports.Storage;

namespace Trag.TestSupport;

public sealed class FakeGitChoiceStore : IGitChoiceStore
{
    public string? Path { get; set; }
    public int Saves { get; private set; }
    public Exception? LoadFailure { get; set; }
    public Exception? SaveFailure { get; set; }

    public Task<string?> LoadAsync(CancellationToken ct) => LoadFailure is null ? Task.FromResult(Path) : Task.FromException<string?>(LoadFailure);

    public Task SaveAsync(string? path, CancellationToken ct)
    {
        Saves++;
        if (SaveFailure is not null) return Task.FromException(SaveFailure);
        Path = path;
        return Task.CompletedTask;
    }
}
