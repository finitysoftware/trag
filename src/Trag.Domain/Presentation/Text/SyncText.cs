using Trag.Domain.Naming;
using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// What the sync panel says: while it has no target and while it reads, its remote and upstream choices and their search,
// the counts against the upstream, and when a read or a write could not be shown.
public static class SyncText
{
    public const string NoRemote = "Choose an existing remote to synchronize.";
    public const string Reading = "Reading local branch references…";
    public const string Changed = "Repository changed. Refresh to read the current sync target.";
    public const string Canceled = "Reading sync information canceled. Press F5 to retry.";
    public const string Failed = "Sync information could not be read. Check the remote configuration and refresh.";
    public const string ResultNotDisplayed = "The synchronization result could not be displayed. Review the local and remote branch before retrying.";

    // What the remote and upstream selectors say before anything is chosen.
    public const string ChooseRemote = "Choose remote";
    public const string ChooseBranch = "Choose branch";
    // One upstream choice, including a branch that Push would create.
    public static string BranchLabel(SyncBranch branch) => branch.IsNew ? branch.Display + " · new branch" : branch.Display;
    // A search of the upstream choices keeps a branch whose label holds the trimmed text, in any case; a blank one keeps all.
    public static bool Matches(SyncBranch branch, string search) =>
        search.Trim() is not { Length: > 0 } wanted || BranchLabel(branch).Contains(wanted, StringComparison.OrdinalIgnoreCase);

    // The note under the upstream choice: too many branches to list, a branch Push would create, no choice yet, a choice
    // not fetched yet, or what the counts compare.
    public static readonly string TooManyBranches = "More than " + Counted.Grouped(GitLimits.BranchCount) + " upstream branches. Narrow the remote fetch configuration in Git.";
    public static string NotOnRemoteYet(string branch, string remote) => branch + " is not on " + remote + " yet. Push creates it and sets it as the upstream.";
    public const string ChooseUpstream = "Choose an upstream branch. Fetch first if it is not listed.";
    public const string UpstreamNotLocal = "Upstream is not available locally. Fetch to compare commits.";
    public const string AheadBehind = "Ahead/behind compares your branch with its local fetched reference.";
}
