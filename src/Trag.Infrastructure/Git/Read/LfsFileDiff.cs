using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// A Git LFS file is kept as a pointer, so Git's diff of it compares pointers. Read as such, it shows what the pointers
// stand for: Git's diff of the two contents when both are in the repository's LFS storage as the files themselves, with
// no Git LFS extension between, fit a whole diff and are text; otherwise the file, with its size on each side unless a
// Git LFS extension stored it, and whether the content of the side shown is here. Git is asked where that storage is
// only for a diff that is a pointer's.
internal sealed class LfsFileDiff(PresetGitRunner git)
{
    private readonly LfsStorage lfs = new(git);

    public async Task<FileDiff> OfAsync(string root, FileDiff diff, bool preview, CancellationToken ct)
    {
        if (LfsPointer.Sides(diff.Lines) is not { } sides) return diff;
        var storage = await lfs.ObjectsAsync(root, ct).ConfigureAwait(false);
        string? Stored(LfsPointer? pointer) => pointer is null ? null : Path.Combine(storage, pointer.Oid[..2], pointer.Oid[2..4], pointer.Oid);
        var (before, after) = (Stored(sides.Before), Stored(sides.After));
        var extended = sides.Before?.Extended == true || sides.After?.Extended == true;
        var shown = Shown(diff.Metadata, extended, sides.Before?.Size, sides.After?.Size, File.Exists(after ?? before));
        if (before is { } old && !File.Exists(old) || after is { } now && !File.Exists(now) || extended
            || Math.Max(sides.Before?.Size ?? 0, sides.After?.Size ?? 0) > DiffLimits.ByteLimit)
            return shown;
        // Git's own empty side stands for a file added or deleted.
        var patch = await git.ResultAsync(root, ["diff", "--no-index", "--no-color", "--no-ext-diff", "--no-textconv",
            DiffContext.WholeFile, "--", before ?? "/dev/null", after ?? "/dev/null"], ct, prefix: DiffLimits.ByteLimit).ConfigureAwait(false);
        var content = FileDiffParser.Patch(diff.Metadata, patch.StandardOutput, preview, patch.OutputTruncated);
        return content.Kind == DiffContentKind.Binary ? shown : content with { IsLfsContent = true };
    }

    // A working file too large for a whole diff is the content itself; it is an LFS file when the index holds its pointer.
    public async Task<FileDiff?> OfLargeWorkingFileAsync(string root, string fullPath, IReadOnlyList<FileDiffMetadata> metadata, CancellationToken ct)
    {
        if (metadata.FirstOrDefault(m => m.HasOldObject)?.OldObject is not { } id) return null;
        var indexed = await git.RunAsync(root, ["cat-file", "blob", id], ct, prefix: 1024).ConfigureAwait(false);
        return LfsPointer.Parse(GitBytes.Text(indexed.StandardOutput).Split('\n')) is { } pointer
            ? Shown(metadata, pointer.Extended, pointer.Size, new FileInfo(fullPath).Length, downloaded: true) : null;
    }

    // Through a Git LFS extension a pointer records the size of what the extension stored, not the file's, so a diff with
    // such a pointer on either side shows no size.
    private static FileDiff Shown(IReadOnlyList<FileDiffMetadata> metadata, bool extended, long? before, long? after, bool downloaded) =>
        new(metadata, [], DiffContentKind.Unavailable, extended ? LfsText.File(null, null, downloaded) : LfsText.File(before, after, downloaded));
}
