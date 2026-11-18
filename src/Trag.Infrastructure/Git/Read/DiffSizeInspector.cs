using System.Globalization;
using System.Text;
using Trag.Domain.Diff;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Read;

// Whether a whole diff of this file can be asked for. Both sides count: the working file on disk and every object the
// raw record names. An object Git answers as missing is one this repository does not have, as a partial clone leaves
// versions on its remote.
internal sealed class DiffSizeInspector(PresetGitRunner git)
{
    public async Task<DiffSize> MeasureAsync(string root, string fullPath, bool fromIndex, IReadOnlyList<FileDiffMetadata> metadata, CancellationToken ct)
    {
        if (!fromIndex && File.Exists(fullPath) && new FileInfo(fullPath).Length > DiffLimits.ByteLimit) return DiffSize.Large;
        return await MeasureObjectsAsync(root, metadata.SelectMany(m => m.Objects).Distinct().ToArray(), ct).ConfigureAwait(false);
    }

    // One cat-file answers for every object at once, from the working tree's diff and from a commit's alike.
    public async Task<DiffSize> MeasureObjectsAsync(string root, string[] objects, CancellationToken ct)
    {
        if (objects.Length == 0) return DiffSize.Fits;
        return Size(await git.ResultAsync(root, ["cat-file", "--batch-check=%(objectsize)"], ct, input: Encoding.ASCII.GetBytes(string.Join('\n', objects) + "\n"))
            .ConfigureAwait(false));
    }

    // Git before 2.45 does not know GIT_NO_LAZY_FETCH: for an object the repository does not have it asks the promisor
    // remote, which the empty GIT_ALLOW_PROTOCOL refuses, and stops instead of answering it as missing.
    internal static DiffSize Size(GitResult sizes)
    {
        if (sizes.ExitCode != 0)
            return Encoding.UTF8.GetString(sizes.StandardError).Contains(" from promisor remote", StringComparison.Ordinal) ? DiffSize.NotDownloaded
                : throw new GitCommandException(sizes.ExitCode);
        var answers = Encoding.ASCII.GetString(sizes.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (answers.Any(answer => answer.EndsWith(" missing", StringComparison.Ordinal))) return DiffSize.NotDownloaded;
        return answers.Any(size => long.Parse(size, NumberStyles.None, CultureInfo.InvariantCulture) > DiffLimits.ByteLimit) ? DiffSize.Large : DiffSize.Fits;
    }
}
