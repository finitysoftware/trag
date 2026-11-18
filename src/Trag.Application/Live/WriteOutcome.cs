using Trag.Domain.Writes;

namespace Trag.Application.Live;

// The last write the shell ran and what came of it. A null result means the outcome could not be displayed: the write
// threw before Git could report, and the repository is marked for review. What it means to the panes beside it: a sync
// reports in its own panel, a commit's message goes once it is committed, an amend ends, and a write of the selected files
// clears the selection it acted on, while a hunk or lines keep their file. Any other write leaves the selection, and a
// file it took away leaves the lists by itself.
public sealed record WriteOutcome(GitWriteRequest Request, GitWriteResult? Result)
{
    public bool IsSync => Request is SyncRequest;

    private bool Succeeded => Result is { Outcome: GitWriteOutcome.Succeeded };

    public string? CommittedMessage => Succeeded && Result!.CommitId is not null && Request is CommitRequest { Amend: false } commit ? commit.Message : null;

    public bool EndsAmend => Succeeded && Request is CommitRequest { Amend: true };

    public bool ClearsSelection => Succeeded && Request is StageRequest or UnstageRequest or DiscardRequest
        or ConflictSideRequest or MergeToolRequest or ReopenConflictRequest;
}
