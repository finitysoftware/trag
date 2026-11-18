using Trag.Domain;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// Whether a commit was once the branch's own tip: one of the branch's reflog entries points at it. A remote tip that
// passes is what the branch was when it was pushed, before a rebase or an amend rewrote it. A tip that is only reachable
// from an entry came in through a merge or a rebase onto it, and undoing that leaves exactly this behind: those commits
// are someone else's, to be pulled, never replaced. That makes the check stricter than Git's --force-if-includes, which
// lets such a tip through; the check is made here because a lease that names its expected commit turns that option off.
// A branch without a reflog proves nothing, so it fails.
internal static class BranchHistory
{
    // The newest reflog entries that are searched; older rewrites are not offered.
    private const int Entries = 1000;

    public static async Task<bool> WasTipAsync(PresetGitRunner git, string root, string branchRef, string commit, CancellationToken ct)
    {
        if (!branchRef.StartsWith("refs/heads/", StringComparison.Ordinal)) throw new ArgumentException("Expected the full ref name of a local branch.");
        GitObjectId.Validate(commit);
        var log = await git.RunAsync(root, ["log", "-g", "--no-show-signature", "--format=%H", "--max-count=" + Entries, branchRef, "--"], ct).ConfigureAwait(false);
        var entries = GitBytes.StrictText(log.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries) GitObjectId.Validate(entry);
        return entries.Contains(commit, StringComparer.OrdinalIgnoreCase);
    }
}
