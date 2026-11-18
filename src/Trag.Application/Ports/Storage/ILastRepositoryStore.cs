namespace Trag.Application.Ports.Storage;

public interface ILastRepositoryStore
{
    Task<string?> LoadAsync(CancellationToken ct);
    Task SaveAsync(string? path, CancellationToken ct);
}
