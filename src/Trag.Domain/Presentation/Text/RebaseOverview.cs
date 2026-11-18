using Trag.Domain.Merge;
using Trag.Domain.Rebase;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// A rebase in progress at a glance: which branch goes onto which, the plan with the commit Git stopped at, what is left in
// that commit and what keeps Continue back, and the lines the rebase view's banner, the file status banner and the
// sidebar show. A rebase Trag cannot continue says why instead.
public sealed record RebaseOverview(RepositorySnapshot? Snapshot, MergeFiles Files)
{
    public static readonly RebaseOverview Empty = new(null, MergeFiles.Empty);

    private RepositoryRebase? Rebase => Snapshot?.Rebase;
    private bool IsSupported => Snapshot is { HasSupportedRebase: true };
    private int Open => Files.Conflicts.Count;
    private int Markers => Files.WithMarkers.Count;
    // The commits after the one Git stopped at.
    private int Left => Rebase is { } rebase ? Math.Max(0, rebase.Total - rebase.Step) : 0;

    public string Branch => Rebase?.Branch ?? "";
    public string Onto => Rebase?.OntoText ?? "";
    public bool HasConflicts => Files.HasConflicts;
    public bool HasMarkers => Files.HasMarkers;
    public bool IsBlocked => Files.IsBlocked;
    public string Step => Rebase is { } rebase ? RebaseText.Step(rebase.Step, rebase.Total) : "";

    public string Direction => IsSupported ? Branch + " onto " + Onto : "";
    public string Remaining => !IsSupported ? Rebase?.Limitation ?? ""
        : HasConflicts ? Step + " stopped: " + Counted.InConflict(Open)
        : HasMarkers ? Step + ": " + Counted.Of(Markers, "file") + (Markers == 1 ? " still contains" : " still contain") + " conflict markers."
        : Rebase!.StoppedForEdit ? Step + " stopped so you can edit it. Continue replays the rest."
        : Rebase.FoldPlanChanged ? Step + ": Git could not fold it into the commit before it, and its plan was changed in a terminal. Finish the rebase there or abort."
        : Rebase.FoldRepeatsMessage ? Step + ": Git could not fold it into the commit before it. Finish it in a terminal or abort."
        : Rebase.FoldNotCommitted ? Step + ": Git could not fold it into the commit before it. Continue folds it again" + Rest
        : Step + ": every conflict is resolved. Continue commits it" + Rest;
    private string Rest => Left == 0 ? " and finishes the rebase." : " and replays the " + Counted.Of(Left, "commit") + " left.";
    public double Progress => Rebase is { Total: > 0 } rebase ? 100.0 * rebase.Applied / rebase.Total : 0;
    public string ProgressText => Rebase is { } rebase ? Counted.Number(rebase.Applied) + " of " + Counted.Number(rebase.Total) + " commits applied" : "";
    public string ContinueHint => HasConflicts ? "Continue is available once every conflict in this commit is resolved · " + Counted.Number(Open) + " left"
        : HasMarkers ? "Blocked: a staged file still has conflict markers"
        : Rebase is { FoldPlanChanged: true } ? RebaseText.FoldPlanChanged
        : Rebase is { FoldRepeatsMessage: true } ? RebaseText.FoldRepeatsMessage
        : Rebase is { FoldNotCommitted: true } ? "Continue folds " + Step + " into the commit before it with the message the plan gives, not this one."
        : Left == 0 ? "Continue commits the last commit with this message and finishes the rebase."
        : "Continue commits " + Step + " with this message, then replays the rest; a new conflict stops it here again.";
    public string MarkersText => MergeText.MarkersText(Files.WithMarkers);
    // Over the message editor: which commit the message is for.
    public string MessageCaption => Rebase is { } rebase ? "Commit " + Counted.Number(rebase.Step) + " of " + Counted.Number(rebase.Total) + " · "
        + (rebase.FoldPlanChanged ? "the rebase is finished in a terminal"
            : rebase.FoldRepeatsMessage ? "the message is set in Git's editor in a terminal"
            : rebase.FoldNotCommitted ? "the fold keeps the plan's message" : "its message; edit it here before continuing") : "";

    // The file status names the open conflicts and sends them to the rebase view; the sidebar shows the step.
    public string FileStatusBanner => Step + " has " + Counted.Of(Open, "file") + " in conflict. Conflicts are resolved in the rebase view, not here.";
    public bool ShowsFileStatusBanner => IsSupported && HasConflicts;
    public string SidebarCount => Rebase is { } rebase ? Counted.Number(rebase.Step) + "/" + Counted.Number(rebase.Total) : "";
    public string SidebarTip => Rebase is null ? "" : "Rebase at " + Step + (HasConflicts ? " · " + Counted.Of(Open, "file") + " in conflict" : "");

    // The plan: the commits replayed, the one Git stopped at, and those still waiting; a fold Git put back is the stop while
    // the plan still folds it first.
    public IReadOnlyList<RebaseStepLine> Steps
    {
        get
        {
            if (Rebase is not { } rebase) return [];
            var current = IsBlocked ? rebase.StoppedForEdit ? "stopped · edit" : "stopped · conflict"
                : rebase.FoldRepeatsMessage || rebase.FoldPlanChanged ? "finish in a terminal" : "ready to continue";
            return rebase.Done.Select((commit, index) => index == rebase.Done.Count - 1
                    ? new RebaseStepLine(index + 1, commit, RebaseStepState.Current, current)
                    : new RebaseStepLine(index + 1, commit, RebaseStepState.Applied, "applied"))
                .Concat(rebase.Waiting.Select((commit, index) => new RebaseStepLine(rebase.Done.Count + index + 1, commit, RebaseStepState.Waiting, "waiting")))
                .ToArray();
        }
    }
}
