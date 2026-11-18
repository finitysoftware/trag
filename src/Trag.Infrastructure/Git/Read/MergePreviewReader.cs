using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain.History;

namespace Trag.Infrastructure.Git.Read;

// What merging one ref into another would do, read without touching the working tree or the index: the commits both refs
// point at, how many commits each side has that the other lacks, the newest incoming commits, and the files a merge would
// leave in conflict, as merge-tree --write-tree tells them; null when it cannot. When it cannot, merge-base says whether
// the two share no commit at all, and Git does not merge such histories.
internal sealed class MergePreviewReader(PresetGitRunner git)
{
    public async Task<MergePreview> ReadAsync(string root, string into, string from, CancellationToken ct)
    {
        if (!into.StartsWith("refs/heads/", StringComparison.Ordinal) || !from.StartsWith("refs/", StringComparison.Ordinal))
            throw new ArgumentException("Expected the full ref names of two branches.");
        var (intoHead, fromHead) = await PreviewReads.HeadsAsync(git, root, into, from, ct).ConfigureAwait(false);
        var (own, incomingCount) = await PreviewReads.CountsAsync(git, root, intoHead, fromHead, ct).ConfigureAwait(false);
        var incoming = await PreviewReads.CommitsAsync(git, root,
            ["--topo-order", "--max-count=" + HistoryLimits.PreviewCommits, fromHead, "^" + intoHead], HistoryLimits.PreviewCommits, ct).ConfigureAwait(false);
        var conflicts = await ReadConflictsAsync(root, intoHead, fromHead, ct).ConfigureAwait(false);
        var sharesNoCommit = conflicts is null && (await git.ResultAsync(root, ["merge-base", intoHead, fromHead], ct).ConfigureAwait(false)).ExitCode == 1;
        return new(intoHead, fromHead, incoming, incomingCount, own, conflicts) { SharesNoCommit = sharesNoCommit };
    }

    // merge-tree answers with the tree it would write and, after exit code 1, every conflicted path once.
    private async Task<IReadOnlyList<GitPath>?> ReadConflictsAsync(string root, string intoHead, string fromHead, CancellationToken ct)
    {
        var result = await git.ResultAsync(root, ["merge-tree", "--write-tree", "-z", "--name-only", "--no-messages", intoHead, fromHead], ct).ConfigureAwait(false);
        if (result.ExitCode == 0) return [];
        if (result.ExitCode != 1) return null;
        return NulRecordReader.Records(result.StandardOutput).Skip(1).Select(path => new GitPath(path)).ToArray();
    }
}
