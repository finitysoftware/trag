namespace Trag.Application.Ports.Storage;

// The git.exe the user chose, kept for the next start; null when Trag finds Git itself.
public interface IGitChoiceStore
{
    Task<string?> LoadAsync(CancellationToken ct);
    Task SaveAsync(string? path, CancellationToken ct);
}
