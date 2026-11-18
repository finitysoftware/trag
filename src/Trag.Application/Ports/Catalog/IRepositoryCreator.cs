using Trag.Domain.Catalog;

namespace Trag.Application.Ports.Catalog;

// Puts a new working copy on disk, either empty or cloned from a remote. HomeFolder is the user's home folder, where a
// clone goes when no listed repository suggests another place.
public interface IRepositoryCreator
{
    string HomeFolder { get; }
    Task InitializeAsync(string path, CancellationToken ct);
    Task<CloneResult> CloneAsync(CloneRequest request, CancellationToken ct);
}
