using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Git.Read;

// What a file too large for a whole diff shows instead: nothing until the user asks, then a bounded preview of the
// content that is actually there — the staged object, or the working file when it still exists.
internal sealed class LargeFileDiffReader(BlobPreviewReader blobs)
{
    public async Task<FileDiff> ReadAsync(string root, string fullPath, bool fromIndex, bool preview,
        IReadOnlyList<FileDiffMetadata> metadata, CancellationToken ct)
    {
        if (!preview) return ContentPreview.Offer(metadata);
        byte[] content;
        if (fromIndex || !File.Exists(fullPath))
        {
            var id = metadata.LastOrDefault(m => m.HasNewObject)?.NewObject ?? metadata.FirstOrDefault(m => m.HasOldObject)?.OldObject;
            if (id is null) return new(metadata, [], DiffContentKind.Unavailable, FileDiffText.ContentGone);
            content = await blobs.BlobAsync(root, id, DiffLimits.PreviewBytes, ct);
        }
        else content = await blobs.FileAsync(fullPath, DiffLimits.PreviewBytes, ct);
        return ContentPreview.Of(metadata, content);
    }
}
