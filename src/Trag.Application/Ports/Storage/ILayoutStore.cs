using Trag.Domain.Presentation;

namespace Trag.Application.Ports.Storage;

public interface ILayoutStore
{
    Task<SavedLayout> LoadLayoutAsync(CancellationToken ct);
    Task SaveLayoutAsync(SavedLayout preferences, CancellationToken ct);
}
