using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Application.Diff;

// How the diff reader gets a diff's presentation; tests hold or delay it.
public interface IDiffPresentationBuilder
{
    Task<DiffPresentation> BuildAsync(FileDiff diff, string path, CancellationToken ct);
}
