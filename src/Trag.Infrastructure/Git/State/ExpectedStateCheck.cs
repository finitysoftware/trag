using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Infrastructure.Git.State;

// Whether the repository is still the one a read or a write expected: the state a write compares, read again now the way
// a write reads it, and compared by the rule a write is let through by.
internal sealed class ExpectedStateCheck(WriteStateReader states)
{
    public async Task<bool> StillExpectedAsync(RepositorySnapshot expected, CancellationToken ct) =>
        SnapshotComparison.MatchesForWrite(expected, await states.ReadStateAsync(expected, expected.Files, ct).ConfigureAwait(false));

    // The transaction's index becomes the repository's only while the repository is still the one expected, and only once
    // Git changed it: every write published here is one that changes the index.
    public async Task<GitWriteResult> PublishAsync(RepositorySnapshot expected, IndexTransaction transaction, string message, CancellationToken ct)
    {
        if (await transaction.UnchangedAsync(ct).ConfigureAwait(false)) return GitWriteResult.Rejected(IndexOutcomeText.IndexNotVerified);
        if (!await StillExpectedAsync(expected, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(WriteText.ChangedDuring);
        await transaction.PublishAsync(ct).ConfigureAwait(false);
        return new(GitWriteOutcome.Succeeded, message);
    }
}
