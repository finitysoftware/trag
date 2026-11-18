using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Details are bounded and redacted before crossing the Infrastructure boundary.
public sealed record GitWriteResult(GitWriteOutcome Outcome, string Message, string Details = "", string? CommitId = null,
    RepositorySnapshot? Snapshot = null)
{
    // Git did the write, or began it and left conflicts for the user: either way the write is under way, and what asked
    // for it is done.
    public bool Started => Outcome is GitWriteOutcome.Succeeded or GitWriteOutcome.NeedsResolution;
    // A transfer the remote refused for its credentials: asking again unchanged would only be refused again.
    public bool RefusedCredentials { get; init; }
    // A transfer the host answered as having no repository at the remote's address: one Trag may offer to create.
    public bool MissingRepository { get; init; }

    // The rejection every writer and every synchronization returns without explaining itself further.
    public static GitWriteResult Rejected(string message, string details = "") => new(GitWriteOutcome.Rejected, message, details);

    // A write Cancel stopped is judged again by the repository its verification read. Git answers a switch, a reset or a
    // discard it was stopped in as canceled or refused, but it may have rewritten files already; then nothing was undone
    // and the outcome needs a review.
    public GitWriteResult AfterStop(RepositorySnapshot expected) =>
        Outcome is GitWriteOutcome.Cancelled or GitWriteOutcome.Rejected && Snapshot is { } after && !SnapshotComparison.Unchanged(expected, after)
            ? this with { Outcome = GitWriteOutcome.Uncertain, Message = WriteText.StoppedPartway } : this;

    public GitWriteResult WithoutFinalStatus() => this with
    {
        Outcome = GitWriteOutcome.Uncertain,
        Message = WriteText.WithoutFinalStatus(Message),
        Snapshot = null
    };
}
