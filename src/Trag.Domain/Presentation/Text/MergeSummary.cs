using Trag.Domain.History;
using Trag.Domain.Merge;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Presentation.Text;

// Everything the merge dialog says about a choice: the hints under both branches, what the merge will do and why, the
// conflicts Git expects, what stops it from starting, and the words on its button. Built again whenever the choice, the
// preview or the repository changes; the dialog binds to nothing else about the merge.
public sealed record MergeSummary(MergeChoice Choice, MergePreview Preview, RepositorySnapshot? Snapshot)
{
    public const int ListedConflicts = 6;
    public static readonly MergeSummary Empty = new(new(null, null, null), MergePreview.None, null);

    private string FromName => Choice.From?.Name ?? "";
    private string IntoName => Choice.Into?.Name ?? "";
    private int Changes => Snapshot?.ChangedFiles ?? 0;
    private bool IsRead => Choice.IsComplete && Preview.IsRead;

    // What stops the merge before Git is asked: uncommitted changes. They are shown next to the conflicts, not instead.
    public bool IsBlocked => Changes != 0;
    public string BlockedText => Counted.Uncommitted(Changes) + " A merge needs a clean working tree, so your edits are not mixed with the merged result.";
    public string BlockedShort => Counted.Of(Changes, "uncommitted file");

    public string FromHint => Choice is { From: not null, Into: not null, IsComplete: false } ? "Choose a branch other than " + IntoName
        : !IsRead ? "" : Preview.IsUpToDate ? "Nothing " + IntoName + " doesn't have"
        : Counted.Of(Preview.IncomingCount, "commit") + " " + IntoName + " doesn't have";
    public string IntoHint => (Choice.SwitchesBranch ? "Not checked out" : "Checked out")
        + (!Choice.SwitchesBranch && IsBlocked ? " · " + Counted.Of(Changes, "changed file")
            : IsRead && Preview.OwnCount != 0 ? " · " + Counted.Of(Preview.OwnCount, "commit") + " of its own" : "");

    public bool IsFastForward => IsRead && Preview.CanFastForward && !Choice.NoFastForward;
    public bool CreatesMergeCommit => IsRead && !Preview.SharesNoCommit && Choice.CreatesMergeCommit(Preview);
    public bool ShowsResult => IsRead && !Preview.IsUpToDate;
    public string ResultTitle => !IsRead ? "" : Preview.IsUpToDate ? "Already up to date"
        : Preview.SharesNoCommit ? "Git will not merge these branches"
        : IsFastForward ? "Fast-forward: " + IntoName + " moves ahead" : "A merge commit will be created";
    public string ResultText => !IsRead ? "" : Preview.IsUpToDate ? IntoName + " already has every commit of " + FromName + ". There is nothing to merge."
        : Preview.SharesNoCommit ? "These branches share no commit in this repository, so Git will not merge them."
        : IsFastForward ? IntoName + " has no commits of its own since " + FromName + " started, so no merge commit is needed."
        : Preview.CanFastForward ? "Git could fast-forward, but a merge commit keeps a record of the merge on " + IntoName + "."
        : "Both branches have new commits, so Trag joins them with one new commit on " + IntoName + ".";
    public bool ShowsNoFastForward => IsRead && Preview.CanFastForward;

    // A fast-forward cannot conflict; otherwise merge-tree answered, or Git could not tell.
    public bool HasConflicts => CreatesMergeCommit && Preview.HasConflicts;
    public bool ShowsCheck => (CreatesMergeCommit && !Preview.HasConflicts) || IsFastForward;
    public string CheckText => IsFastForward ? "No conflicts possible"
        : Preview.Conflicts is null ? "Conflicts could not be checked" : "No conflicts expected";
    public bool IsCheckKnown => IsFastForward || Preview.Conflicts is not null;
    public string ConflictTitle => Counted.Of(Preview.Conflicts?.Count ?? 0, "file") + " will conflict";
    public IReadOnlyList<string> ConflictFiles => Preview.Conflicts?.Take(ListedConflicts).Select(path => path.DisplayEscaped).ToArray() ?? [];
    public string ConflictMore => Preview.Conflicts is { Count: > ListedConflicts } conflicts ? "and " + Counted.Of(conflicts.Count - ListedConflicts, "more file") : "";
    public string ConflictText => IsBlocked
        ? "Once your changes are saved, Trag starts the merge and opens " + MergeText.InProgress + ", where you resolve each file."
        : "Trag starts the merge and opens " + MergeText.InProgress + ", where you resolve each file. You can abort there and return to where you are now.";

    public IReadOnlyList<HistoryCommit> Commits => IsRead ? Preview.Incoming : [];
    public string CommitsCaption => "COMMITS COMING IN · " + Counted.Number(Preview.IncomingCount);
    public string MoreCommits => Preview.MoreCount == 0 ? "" : "and " + Counted.Number(Preview.MoreCount) + " more";

    // Into another branch Trag has to leave the current one for a moment.
    public string SwitchText => Choice.StayOnTarget
        ? IntoName + " is not checked out. Trag switches to " + IntoName + " and merges; you stay there afterwards."
        : IntoName + " is not checked out. Trag switches to " + IntoName + ", merges, then switches back to " + Choice.Current + ".";
    public string StayLabel => "Stay on " + IntoName + " after merging";
    // Only a branch with an upstream can be pushed right away; publishing stays the sidebar's job.
    public bool CanPush => Choice.Into?.Upstream is not null;
    public string PushLabel => "Push " + IntoName + " after merging";

    // The button names the direction only once Git has said there is something to merge; while it reads, and when there is
    // nothing, it is just Merge.
    public string Action => !IsRead || Preview.IsUpToDate ? "Merge" : HasConflicts ? "Merge and resolve conflicts…" : "Merge " + FromName + " into " + IntoName;
    // Everything the merge needs from the choice, the preview and the working tree; the writer's own rules come on top.
    public bool CanStart => IsRead && !Preview.IsUpToDate && !Preview.SharesNoCommit && !IsBlocked && Snapshot is { CanStartOperation: true }
        && (!CreatesMergeCommit || CommitRequest.IsValidMessage(Choice.Message));
}
