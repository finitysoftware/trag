using Trag.Application.Ports.Storage;

namespace Trag.Infrastructure.Platform.Storage;

public sealed class MemoryLastRepositoryStore : ILastRepositoryStore
{
    private string? path;
    public Task<string?> LoadAsync(CancellationToken ct) => Task.FromResult(path);
    public Task SaveAsync(string? value, CancellationToken ct) { path = value; return Task.CompletedTask; }
}
