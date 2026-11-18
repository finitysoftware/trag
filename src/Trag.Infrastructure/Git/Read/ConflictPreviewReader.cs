using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.Read;

// A conflicted file has no diff to show: what the user has to act on is the working file Git left with its markers in it.
// The path was already checked for links by the reader that chose this one.
internal sealed class ConflictPreviewReader(BlobPreviewReader blobs)
{
    public async Task<FileDiff> ReadAsync(FileStatus file, string fullPath, CancellationToken ct)
    {
        if (!File.Exists(fullPath)) return new([], [], DiffContentKind.Unavailable, FileDiffText.ConflictWithoutWorkingFile);
        var content = await blobs.FileAsync(fullPath, DiffLimits.PreviewBytes, ct);
        return ContentPreview.Of([new(file.Path, "U", "", "", "", "")], content, FileDiffText.ConflictedWorkingFile);
    }
}
