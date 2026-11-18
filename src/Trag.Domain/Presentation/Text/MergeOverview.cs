using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// A merge in progress at a glance: which branch comes into which, how many files are still open, what keeps Finish back,
// and the lines the merge view's banner, the file status banner and the sidebar show. A merge Trag cannot continue says
// why instead.
public sealed record MergeOverview(RepositorySnapshot? Snapshot, MergeFiles Files)
{
    public static readonly MergeOverview Empty = new(null, MergeFiles.Empty);

    public string Incoming => MergeMessages.IncomingName(Snapshot?.Merge);
    public string Branch => Snapshot?.Branch ?? "";
    private int Open => Files.Conflicts.Count;
    private int Markers => Files.WithMarkers.Count;
    private bool IsSupported => Snapshot is { HasSupportedMerge: true };

    public bool HasConflicts => Files.HasConflicts;
    public bool HasMarkers => Files.HasMarkers;
    public bool IsBlocked => Files.IsBlocked;

    public string Direction => IsSupported ? Incoming + " → " + Branch : "";
    public string Remaining => !IsSupported ? MergeText.Unsupported(Snapshot)
        : HasConflicts ? Counted.Number(Open) + " of " + Counted.Of(Files.Total, "file") + (Open == 1 ? " is" : " are") + " still in conflict."
        : HasMarkers ? Counted.Number(Markers) + " of " + Counted.Of(Files.Resolved.Count, "resolved file")
            + (Markers == 1 ? " still contains" : " still contain") + " conflict markers."
        : "All conflicts are resolved. Review the result and finish.";
    public double Progress => Files.Total == 0 ? 100 : 100.0 * Files.Resolved.Count / Files.Total;
    public string ProgressText => Counted.Number(Files.Resolved.Count) + " of " + Counted.Number(Files.Total) + " resolved";
    public string FinishHint => HasConflicts ? "Finish is available once every conflict is resolved · " + Counted.Number(Open) + " left"
        : HasMarkers ? "Blocked: a staged file still has conflict markers" : "Finish commits the entire staged result with both parents.";

    public string MarkersText => MergeText.MarkersText(Files.WithMarkers);

    // The file status names the open conflicts and sends them to the merge view; the sidebar counts the files that keep
    // Finish back, conflicts first.
    public string FileStatusBanner => Counted.Of(Open, "file") + (Open == 1 ? " is" : " are") + " still in conflict. Conflicts are resolved in the merge view, not here.";
    public string SidebarCount => HasConflicts ? Counted.Number(Open) : HasMarkers ? Counted.Number(Markers) : "";
    public string SidebarTip => HasConflicts ? Counted.Of(Open, "file") + " still in conflict"
        : HasMarkers ? Counted.Of(Markers, "file") + (Markers == 1 ? " still has" : " still have") + " conflict markers" : "All conflicts are resolved";
    public bool ShowsFileStatusBanner => IsSupported && HasConflicts;
}
