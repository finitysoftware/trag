using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Read;

// What a file too large for a whole diff shows, in a commit and in the working tree alike, and what a conflicted working
// file shows: the first rows of content that is actually there, never presented as a diff.
internal static class ContentPreview
{
    // Before the user asks, a large file only offers the preview.
    public static FileDiff Offer(IReadOnlyList<FileDiffMetadata> metadata) =>
        new(metadata, [], DiffContentKind.Limited, FileDiffText.LargeFileOffer, true);

    // A deletion shows what was there, numbered on the old side; anything else is content that exists now.
    public static FileDiff Of(IReadOnlyList<FileDiffMetadata> metadata, byte[] content, string? message = null)
    {
        var result = FileDiffParser.NewContent(metadata, content, true, true, contentOnly: true,
            oldContent: metadata.Count != 0 && metadata.All(m => m.NewMode == "000000"));
        return result.Kind == DiffContentKind.Binary ? result : result with { Message = message ?? FileDiffText.ContentPreview };
    }
}
