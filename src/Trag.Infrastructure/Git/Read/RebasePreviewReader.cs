using System.Text;
using Trag.Domain.History;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Domain;

namespace Trag.Infrastructure.Git.Read;

// What rebasing HEAD onto a ref would do, read without touching the working tree or the index: the commits both point at,
// how many commits each side has that the other lacks, which of HEAD's Git would replay (as its plan does: no merge
// commits, no change the onto side already has) and the newest of them, the files changed on both sides since the merge
// base, how many of HEAD's commits its upstream already has, and whether that upstream is its remote's default branch.
internal sealed class RebasePreviewReader(PresetGitRunner git)
{
    public async Task<RebasePreview> ReadAsync(string root, string onto, CancellationToken ct)
    {
        if (!onto.StartsWith("refs/", StringComparison.Ordinal)) throw new ArgumentException("Expected the full ref name of a branch.");
        var (head, ontoHead) = await PreviewReads.HeadsAsync(git, root, "HEAD", onto, ct).ConfigureAwait(false);
        var (own, ontoCount) = await PreviewReads.CountsAsync(git, root, head, ontoHead, ct).ConfigureAwait(false);
        var (replayed, alreadyOnto, listed) = await ReadPlanAsync(root, head, ontoHead, ct).ConfigureAwait(false);
        var commits = await ReadCommitsAsync(root, listed, ct).ConfigureAwait(false);
        var both = await ReadBothSidesAsync(root, head, ontoHead, ct).ConfigureAwait(false);
        var (upstream, published) = await ReadPublishedAsync(root, head, ontoHead, own, ct).ConfigureAwait(false);
        return new(head, ontoHead, commits, replayed, ontoCount, both, upstream is null ? null : upstream["refs/remotes/".Length..], published)
        {
            Merges = Math.Max(0, own - replayed - alreadyOnto),
            AlreadyOnto = alreadyOnto,
            UpstreamIsDefault = upstream is null ? null : await ReadIsDefaultAsync(root, upstream, ct).ConfigureAwait(false)
        };
    }

    // The commits Git's plan picks ("+") and those it drops because the onto side has the same change ("="); merge
    // commits are left out of both. Comparing the changes is the costly part, so it is done once: the walk is in the
    // order the list shows, newest first, and the first picked commits are the ones listed.
    private async Task<(int Replayed, int AlreadyOnto, string[] Listed)> ReadPlanAsync(string root, string head, string ontoHead, CancellationToken ct)
    {
        var marked = PreviewReads.Text(await git.RunAsync(root, ["rev-list", "--topo-order", "--left-only", "--no-merges", "--cherry-mark", head + "..." + ontoHead, "--"], ct).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var picked = marked.Where(line => line[0] == '+').Select(line => line[1..].Trim()).ToArray();
        foreach (var id in picked.Take(HistoryLimits.PreviewCommits)) GitObjectId.Validate(id);
        return (picked.Length, marked.Count(line => line[0] == '='), picked.Take(HistoryLimits.PreviewCommits).ToArray());
    }

    // The listed commits as the history shows them, in the plan's order; no change is compared again.
    private async Task<IReadOnlyList<HistoryCommit>> ReadCommitsAsync(string root, string[] listed, CancellationToken ct)
    {
        if (listed.Length == 0) return [];
        return await PreviewReads.CommitsAsync(git, root, ["--no-walk=unsorted", .. listed], HistoryLimits.PreviewCommits, ct).ConfigureAwait(false);
    }

    // The paths both sides changed since they parted, a submodule whatever the configuration ignores; null when there is
    // no merge base to compare with.
    private async Task<IReadOnlyList<GitPath>?> ReadBothSidesAsync(string root, string head, string ontoHead, CancellationToken ct)
    {
        var mergeBase = await git.ResultAsync(root, ["merge-base", head, ontoHead], ct).ConfigureAwait(false);
        if (mergeBase.ExitCode != 0) return null;
        var baseId = Encoding.UTF8.GetString(mergeBase.StandardOutput).Trim();
        GitObjectId.Validate(baseId);
        var ours = await ChangedAsync(root, baseId, head, ct).ConfigureAwait(false);
        var theirs = (await ChangedAsync(root, baseId, ontoHead, ct).ConfigureAwait(false)).Select(path => path.Key).ToHashSet(StringComparer.Ordinal);
        return ours.Where(path => theirs.Contains(path.Key)).OrderBy(path => path, GitPath.ByBytes).ToArray();
    }

    private async Task<IReadOnlyList<GitPath>> ChangedAsync(string root, string from, string to, CancellationToken ct)
    {
        var diff = await git.RunAsync(root, ["diff", "--name-only", "--no-renames", "--ignore-submodules=none", "-z", from, to, "--"], ct).ConfigureAwait(false);
        return NulRecordReader.Records(diff.StandardOutput).Select(path => new GitPath(path)).ToArray();
    }

    // HEAD's own commits the upstream has are those not left once the upstream is excluded too.
    private async Task<(string? Upstream, int Published)> ReadPublishedAsync(string root, string head, string ontoHead, int own, CancellationToken ct)
    {
        var upstream = await git.ResultAsync(root, ["rev-parse", "--verify", "--quiet", "--symbolic-full-name", "@{upstream}"], ct).ConfigureAwait(false);
        var reference = Encoding.UTF8.GetString(upstream.StandardOutput).Trim();
        if (upstream.ExitCode != 0 || !reference.StartsWith("refs/remotes/", StringComparison.Ordinal)) return (null, 0);
        var unpublished = PreviewReads.Text(await git.RunAsync(root, ["rev-list", "--count", head, "^" + ontoHead, "^" + reference, "--"], ct).ConfigureAwait(false));
        return (reference, Math.Max(0, own - PreviewReads.Number(unpublished)));
    }

    // The remote of the upstream is the one whose refs/remotes/<remote>/ holds it; its HEAD alias names the default
    // branch. A remote without that alias names none.
    private async Task<bool?> ReadIsDefaultAsync(string root, string upstream, CancellationToken ct)
    {
        var aliases = PreviewReads.Text(await git.RunAsync(root, ["for-each-ref", "--format=%(refname)%00%(symref)", "refs/remotes/**/HEAD"], ct).ConfigureAwait(false))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row.Split('\0'))
            .Where(row => row.Length == 2 && row[1].Length != 0 && upstream.StartsWith(row[0][..^"HEAD".Length], StringComparison.Ordinal))
            .OrderByDescending(row => row[0].Length)
            .ToArray();
        return aliases.Length == 0 ? null : aliases[0][1] == upstream;
    }
}
