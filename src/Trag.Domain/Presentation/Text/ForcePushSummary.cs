using Trag.Domain.Status;
using Trag.Domain.Sync;

namespace Trag.Domain.Presentation.Text;

// What the file status says about a branch that was rewritten after it was pushed (a rebase, an amend): that it no longer
// matches its upstream, what pushing it means, and the question asked before the remote branch is replaced.
public sealed record ForcePushSummary(RepositorySyncState? State, RepositorySnapshot? Snapshot)
{
    public static readonly ForcePushSummary Empty = new(null, null);

    public bool Shows => State is { } state && Snapshot is { } snapshot && state.CanForcePush(snapshot);
    private string Branch => Snapshot?.Branch ?? "";
    private string Upstream => State?.UpstreamName ?? "";

    public string Title => Branch + " no longer matches " + Upstream;
    public string Text => "It was rewritten after it was pushed: " + Counted.Of(Ahead, "commit") + (Ahead == 1 ? " here is" : " here are")
        + " new to the remote, and " + Counted.Number(Behind) + (Behind == 1 ? " there is" : " there are")
        + " no longer part of it. Pushing replaces the remote branch; anyone who already pulled it has to reset it.";
    private int Ahead => State?.Ahead ?? 0;
    private int Behind => State?.Behind ?? 0;
    public string ConfirmTitle => "Replace " + Upstream + " with " + Branch + "?";
    public string ConfirmText => "Trag replaces it only while it is still at " + GitObjectId.Short(State?.TrackingHead ?? "")
        + ", where your last fetch saw it. If someone pushed since, nothing is sent.";
}
