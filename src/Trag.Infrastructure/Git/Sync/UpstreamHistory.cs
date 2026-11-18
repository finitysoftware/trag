using System.Globalization;
using System.Text;
using Trag.Domain;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.Git.Sync;

// Whether the branch holds commits its upstream once had and has dropped since: someone rewrote the upstream and forced
// it over the remote. Every tip the remote-tracking ref had is in its reflog, so a commit the branch has, the upstream
// now lacks and one of those tips reached was the upstream's own, and a merge would bring it back beside its rewritten
// copy. A commit only the branch ever had is the user's own work and merges as usual. A remote-tracking ref without a
// reflog proves nothing, and the pull goes on as before.
internal static class UpstreamHistory
{
    // The newest reflog entries that are searched, as for the branch's own reflog.
    private const int Entries = 1000;

    // Ahead is how many commits the branch has that the upstream's tip lacks.
    public static async Task<bool> DroppedAsync(PresetGitRunner git, string root, string head, string trackingRef, string tip, int ahead, CancellationToken ct)
    {
        var log = await git.RunAsync(root, ["log", "-g", "--no-show-signature", "--format=%H", "--max-count=" + Entries, trackingRef, "--"], ct).ConfigureAwait(false);
        var tips = GitBytes.StrictText(log.StandardOutput).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // The oldest entry's old value was a tip too: a clone writes no reflog for its remote-tracking refs, so the first
        // fetch records the tip the clone brought only there. A ref that was created by that entry has none.
        var oldest = await git.ResultAsync(root, ["rev-parse", "--verify", "--quiet", trackingRef + "@{" + tips.Length + "}"], ct).ConfigureAwait(false);
        if (oldest.ExitCode == 0) tips = [.. tips, GitBytes.StrictText(oldest.StandardOutput).Trim()];
        foreach (var entry in tips) GitObjectId.Validate(entry);
        var revisions = tips.Prepend(tip).Select(commit => "^" + commit).Prepend(head);
        var input = Encoding.ASCII.GetBytes(string.Concat(revisions.Select(revision => revision + "\n")));
        var own = await git.RunAsync(root, ["rev-list", "--count", "--stdin"], ct, input).ConfigureAwait(false);
        return int.Parse(GitBytes.StrictText(own.StandardOutput).Trim(), NumberStyles.None, CultureInfo.InvariantCulture) < ahead;
    }
}
