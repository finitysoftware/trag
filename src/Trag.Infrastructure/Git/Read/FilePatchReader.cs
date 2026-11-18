using Trag.Domain.Diff;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The diff of a file that fits: the patch Git prints, or, for a file Git has never seen, its whole content, because
// there is nothing to compare it against.
internal sealed class FilePatchReader(PresetGitRunner git, BlobPreviewReader blobs)
{
    public async Task<FileDiff> ReadAsync(string root, FileStatus file, string fullPath, bool fromIndex, bool preview,
        IReadOnlyList<FileDiffMetadata> metadata, string[] paths, CancellationToken ct)
    {
        if (!fromIndex && file.IsUntracked)
        {
            var content = await blobs.FileAsync(fullPath, DiffLimits.ByteLimit + 1, ct);
            return FileDiffParser.NewContent(metadata, content, preview, content.Length > DiffLimits.ByteLimit);
        }
        var patch = await git.RunAsync(root, ["-c", "diff.algorithm=myers", "-c", "diff.autoRefreshIndex=false", "diff", "--patch", "--no-ext-diff", "--no-textconv", "--no-color",
            "--find-renames", DiffContext.WholeFile, .. fromIndex ? new[] { "--cached" } : [], "--", .. paths], ct, prefix: DiffLimits.ByteLimit);
        return FileDiffParser.Patch(metadata, patch.StandardOutput, preview, patch.OutputTruncated);
    }
}
