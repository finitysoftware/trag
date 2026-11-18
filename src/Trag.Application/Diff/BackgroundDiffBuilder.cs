using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Application.Diff;

// Domain's presentation of a diff, built off the UI thread and one at a time, so a burst of selections never builds
// several large diffs at once.
public sealed class BackgroundDiffBuilder : IDiffPresentationBuilder
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<DiffPresentation> BuildAsync(FileDiff diff, string path, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => DiffPresentationBuilder.Build(diff, path, ct), ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
