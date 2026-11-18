using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Merge;

// Whether a commit holds Git LFS files, asked of its own tree and attributes: the commit a merge would bring in, before
// anything is checked out, and the commit a push sends. Both answers are read as they arrive, so a tree larger than the
// runner holds is still answered.
internal sealed class LfsTreeInspector(PresetGitRunner git)
{
    public async Task<bool> HasLfsAsync(string root, string commit, CancellationToken ct) => await CountLfsAsync(root, commit, ct).ConfigureAwait(false) > 0;

    // How many of the commit's files are Git LFS's, as the repository's details count them.
    public async Task<int> CountLfsAsync(string root, string commit, CancellationToken ct)
    {
        var paths = await git.StreamAsync(root, ["ls-tree", "-r", "-z", "--full-tree", commit], LfsFilterRecords.PathsAsync, ct).ConfigureAwait(false);
        return paths.Length == 0 ? 0
            : await git.StreamAsync(root, ["check-attr", "--source=" + commit, "-z", "--stdin", "filter"], LfsFilterRecords.CountLfsAsync, ct, paths).ConfigureAwait(false);
    }
}
