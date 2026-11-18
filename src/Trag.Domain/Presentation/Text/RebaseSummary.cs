using Trag.Domain.History;
using Trag.Domain.Rebase;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// Everything the rebase side of the merge dialog says and allows, from what was chosen, what Git previewed and the
// repository shown: the hints under both branches, what the rebase will do and leave out, the files changed on both
// sides, whether the branch is already on its remote or is the branch others build on, what stops it, and the buttons.
// The dialog binds these; it decides nothing itself.
public sealed record RebaseSummary(RebaseChoice Choice, RebasePreview Preview, RepositorySnapshot? Snapshot)
{
    public static readonly RebaseSummary Empty = new(new(null, null), RebasePreview.None, null);
    // How many files changed on both sides are named; the rest are counted.
    public const int ListedFiles = 6;

    private string BranchName => Choice.Branch?.Name ?? "";
    private string OntoName => Choice.Onto?.Name ?? "";
    private string OntoLocalName => Choice.Onto?.LocalName ?? "";
    private int Changes => Snapshot?.ChangedFiles ?? 0;
    private bool IsRead => Choice.IsComplete && Preview.IsRead;

    // What stops the rebase before Git is asked: uncommitted changes.
    public bool IsBlocked => Changes != 0;
    public string BlockedText => Counted.Uncommitted(Changes) + " A rebase needs a clean working tree, so your edits are not mixed with the replayed commits.";
    public string BlockedShort => Counted.Of(Changes, "uncommitted file");

    public string BranchHint => IsBlocked ? "Checked out · " + Counted.Of(Changes, "changed file")
        : !IsRead ? "Checked out"
        : "Checked out · " + Counted.Of(Preview.OwnCount, "commit") + " of its own" + (Preview.IsPublished ? " · on " + Remote : "");
    public string OntoHint => Choice is { Onto: not null, Branch: not null, IsComplete: false } ? "Choose a branch other than " + BranchName
        : !IsRead ? "" : Preview.IsUpToDate ? "Nothing " + BranchName + " doesn't have" : Counted.Of(Preview.OntoCount, "commit") + " " + BranchName + " doesn't have";

    public bool ShowsResult => IsRead && !Preview.IsUpToDate;
    public string ResultTitle => !IsRead ? "" : Preview.IsUpToDate ? "Already up to date"
        : Preview.OnlyMoves ? BranchName + " moves ahead to " + OntoName
        : Counted.Of(Preview.ReplayedCount, "commit") + " will be replayed on top of " + OntoName;
    public string ResultText => !IsRead ? ""
        : Preview.IsUpToDate ? BranchName + " already has every commit of " + OntoName + "."
        : Preview.OnlyMoves ? BranchName + " has no commits of its own, so it simply moves to " + OntoName + "."
        : "Each commit of " + BranchName + " is applied again after " + OntoName + "'s newest commit. They get new ids and the history stays one straight line, with no merge commit.";
    // What Git does not replay: merge commits (the history becomes one line) and changes the onto branch already has.
    public string LeftOutText => !ShowsResult ? "" : (Preview.Merges, Preview.AlreadyOnto) switch
    {
        (0, 0) => "",
        (var merges, 0) => "Git leaves out " + Counted.Of(merges, "merge commit") + "; conflicts they resolved can come back.",
        (0, var already) => "Git leaves out " + Counted.Of(already, "commit") + " " + OntoName + " already has.",
        var (merges, already) => "Git leaves out " + Counted.Of(merges, "merge commit") + " (conflicts they resolved can come back) and "
            + Counted.Of(already, "commit") + " " + OntoName + " already has."
    };

    // Git can only tell which files changed on both sides; whether they conflict shows while the rebase runs.
    public bool ShowsCheck => ShowsResult && !Preview.OnlyMoves && !Preview.HasBothSides;
    public bool IsCheckKnown => Preview.BothSides is not null;
    public string CheckText => Preview.BothSides is null ? "Conflicts show while the rebase runs" : "No file is changed on both sides";
    public bool HasBothSides => ShowsResult && Preview.HasBothSides;
    public string ConflictTitle => Counted.Of(Preview.BothSides?.Count ?? 0, "file") + (Preview.BothSides?.Count == 1 ? " is" : " are") + " changed on both sides";
    public IReadOnlyList<string> ConflictFiles => Preview.BothSides?.Take(ListedFiles).Select(path => path.DisplayEscaped).ToArray() ?? [];
    public string ConflictMore => (Preview.BothSides?.Count ?? 0) > ListedFiles ? "and " + Counted.Of(Preview.BothSides!.Count - ListedFiles, "more file") : "";
    public string ConflictText => "The rebase may stop at a conflict in one of the commits. Trag then opens " + RebaseText.InProgress + ", where you resolve it, skip that commit or abort.";

    // Rebasing the branch everyone builds on (its upstream is the remote's default branch, or it is main or master when the
    // remote names none) rewrites commits others have; bringing the other branch up to date is almost always what was
    // meant, and a local one can be rebased onto this one instead.
    public bool ShowsMainline => ShowsResult && Preview.IsPublished && (Preview.UpstreamIsDefault ?? BranchName is "main" or "master");
    public string MainlineTitle => "This rewrites " + BranchName + ", the branch others build on";
    public string MainlineText => "Its " + Counted.Of(Preview.ReplayedCount, "commit") + (Preview.ReplayedCount == 1 ? " gets a new id" : " get new ids")
        + " on " + Remote
        + ", and everyone who pulled " + BranchName + " has to reset it. To bring " + OntoLocalName + " up to date instead, rebase it onto "
        + BranchName + (CanRebaseOtherWay ? "." : ": switch to it first.");
    public bool CanRebaseOtherWay => ShowsMainline && Choice.Onto is { IsRemote: false };
    public string OtherWayAction => "Rebase " + OntoLocalName + " onto " + BranchName + " instead";

    // A branch already pushed has to replace its remote copy afterwards; one never pushed rewrites nothing anyone has.
    // The mainline warning already says so.
    public bool ShowsPublished => ShowsResult && Preview.IsPublished && !ShowsMainline;
    public string PublishedTitle => BranchName + " is already on " + Remote;
    public string PublishedText => "The rebase gives its commits new ids, so the branch will no longer match " + Preview.Upstream
        + ". Afterwards Trag offers Force push with lease; anyone who already pulled the branch has to reset it.";
    public bool ShowsLocalOnly => ShowsResult && Preview.Upstream is null;
    public string LocalOnlyText => BranchName + " is not on a remote yet, so rewriting its commits affects no one else.";

    public string CommitsCaption => "COMMITS BEING REPLAYED · " + Counted.Number(Preview.ReplayedCount);
    public IReadOnlyList<HistoryCommit> Commits => Preview.Replayed;
    public string MoreCommits => Preview.MoreCount == 0 ? "" : "and " + Counted.Number(Preview.MoreCount) + " more";

    // The button names what happens only once Git has confirmed there is something to rebase.
    public string Action => !ShowsResult ? "Rebase" : "Rebase " + BranchName + " onto " + OntoName;
    public bool CanStart => ShowsResult && !IsBlocked && Snapshot is { CanStartOperation: true };

    // The remote the branch tracks, which is where its upstream is; a remote's name may hold a slash.
    private string Remote => Snapshot?.TrackingRemote ?? "";
}
