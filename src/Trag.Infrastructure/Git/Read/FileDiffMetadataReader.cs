using Trag.Domain.Diff;
using Trag.Domain.Status;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// The modes and object IDs of one path, from the index or from the working tree. An untracked file has no raw record
// to read: it is a new file with the mode Git would give it. This read and the patch (FilePatchReader) leave the index as
// it is: Git would otherwise refresh the stat data of a file whose content matches the index, which a hunk staged with
// git apply leaves without any, and write the index that every write compares with the one the user was shown.
internal sealed class FileDiffMetadataReader(PresetGitRunner git)
{
    public async Task<IReadOnlyList<FileDiffMetadata>> ReadAsync(string root, FileStatus file, bool fromIndex, string[] paths, CancellationToken ct)
    {
        if (!fromIndex && file.IsUntracked) return [new(file.Path, "A", "000000", "100644", "", "")];
        return FileDiffParser.Metadata((await git.RunAsync(root,
            ["-c", "diff.autoRefreshIndex=false", "diff", "--raw", "-z", "--no-abbrev", "--no-ext-diff", "--no-textconv", "--no-renames", .. fromIndex ? new[] { "--cached" } : [], "--", .. paths], ct)).StandardOutput);
    }
}
