using Trag.Domain.Merge;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// Where the repository's conflicts are resolved, and what the rest of the workspace says about it: the merge view during
// a merge, the rebase view during a rebase, each with the sides named its way, the file status banner and links, the
// unstage tooltip and the lead of the markers notice. A revert or a cherry-pick stopped at conflicts has a view of its
// own, which finishes or aborts it. Conflicts outside all three (a stash applied over changes) have the conflict view, where each file is
// resolved and nothing is finished afterwards.
public sealed record ConflictScope(WorkspaceSection? Section, ConflictSides Sides, string Lead, string Banner, bool ShowsBanner, string Resolves,
    string ViewLine = "")
{
    private string View => Resolves + " view";
    public static readonly ConflictScope None = new(null, ConflictSides.For(null), "", "", false, "");

    public bool HasView => Section is not null;
    public bool IsRebase => Section == WorkspaceSection.Rebase;
    // The icon of the view that resolves the conflicts: the rebase's in a rebase, the merge's in any other.
    public string Icon => IsRebase ? IconKinds.Rebase : IconKinds.Merge;
    public string OpenText => "Open " + View;
    public string ResolveText => "Resolve in " + View + " →";
    public string UnstageTip => Section switch
    {
        null => "",
        WorkspaceSection.Conflicts => "Unstaging waits until every conflict is resolved in the " + View + ".",
        _ => "A " + Resolves + " is in progress. To change a file's resolution, open the " + View + " and choose Mark as unresolved."
    };
    // What would commit a staged file's markers: continuing a rebase, finishing a merge, a revert or a cherry-pick.
    public string MarkersLead => (IsRebase ? WriteText.ContinueAction : WriteText.FinishAction) + " would commit conflict markers.";
    // The tooltip of Unstage: what it does, or while an operation's view resolves the conflicts, why it waits there. It
    // names only the view, which the files do not change.
    public static string UnstageTipFor(RepositorySnapshot? snapshot) => For(snapshot, MergeFiles.Empty) is { HasView: true } scope ? scope.UnstageTip : StatusText.UnstageTip;

    private static string StashBanner(int open) => Counted.InConflict(open) + " Conflicts are resolved in the conflict view, not here.";
    // What the conflict view itself says over its panels.
    private static string StashViewLine(int open) => Counted.InConflict(open) + " A file is done once it is marked as resolved; nothing is finished afterwards.";

    public static ConflictScope For(RepositorySnapshot? snapshot, MergeFiles files) => snapshot?.Operation switch
    {
        RepositoryOperation.Merge when new MergeOverview(snapshot, files) is var merge => new(WorkspaceSection.Merge, ConflictSides.For(snapshot),
            MergeText.InProgress + ":", merge.FileStatusBanner, merge.ShowsFileStatusBanner, "merge"),
        RepositoryOperation.Rebase when new RebaseOverview(snapshot, files) is var rebase => new(WorkspaceSection.Rebase, ConflictSides.For(snapshot),
            RebaseText.InProgress + ":", rebase.FileStatusBanner, rebase.ShowsFileStatusBanner, "rebase"),
        RepositoryOperation.Revert or RepositoryOperation.CherryPick when snapshot.HasSupportedPick => new(WorkspaceSection.Pick, ConflictSides.For(snapshot),
            PickText.InProgress(snapshot.Operation) + ":", PickText.Banner(snapshot.Operation, snapshot.Conflicts.Count()), true,
            PickText.Name(snapshot.Operation), PickText.ViewLine(snapshot.Operation, snapshot.Conflicts.Count())),
        RepositoryOperation.Conflict when snapshot.HasSupportedConflicts => new(WorkspaceSection.Conflicts, ConflictSides.For(snapshot),
            MergeText.ConflictResolution + ":", StashBanner(snapshot.Conflicts.Count()), true, "conflict", StashViewLine(snapshot.Conflicts.Count())),
        _ => None
    };
}
