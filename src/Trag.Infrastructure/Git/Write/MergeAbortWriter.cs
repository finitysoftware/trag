using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Abort is verified against what the merge started from: HEAD, the branch, the pre-merge tree and the original index.
// Abort can partially change working files before failing, so an unverified abort is never followed by reset or clean.
internal sealed class MergeAbortWriter(WriteCommandRunner git, HeadReader heads)
{
    public Task<GitWriteResult> AbortMergeAsync(RepositorySnapshot before, IndexTransaction transaction, CancellationToken ct) =>
        VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["merge", "--abort"], token, transaction),
            (command, failure, token) => VerifyAsync(before, transaction, command, failure, token),
            (_, _) => transaction.KeepForReview(MergeOutcomeText.AbortNotVerified),
            operationTimeout: null, TimeSpan.FromSeconds(20), ct, new StoppedWriteLock(StoppedWriteLock.HeadStorage(before, "ORIG_HEAD")));

    private async Task<GitWriteResult> VerifyAsync(RepositorySnapshot before, IndexTransaction transaction, GitResult? command, Exception? failure, CancellationToken ct)
    {
        if (!await IsRestoredAsync(before, transaction, ct).ConfigureAwait(false))
            return transaction.KeepForReview(MergeOutcomeText.AbortIncomplete,
                details: command is null ? "" : GitOutputText.Details(command) + "\n");
        await transaction.PublishAsync(ct).ConfigureAwait(false);
        return new(GitWriteOutcome.Succeeded, failure is not null ? MergeOutcomeText.AbortedAfterInterruption : MergeOutcomeText.Aborted);
    }

    private async Task<bool> IsRestoredAsync(RepositorySnapshot before, IndexTransaction transaction, CancellationToken ct)
    {
        var (head, branch) = await heads.ReadAsync(before.Root, ct).ConfigureAwait(false);
        if (head != before.Head || branch != before.Branch) return false;
        var tree = await git.RunAsync(before.Root, ["write-tree"], ct, transaction).ConfigureAwait(false);
        var original = await heads.ReadCommitAsync(before.Root, before.Head!, ct).ConfigureAwait(false);
        if (tree.ExitCode != 0 || GitBytes.Text(tree.StandardOutput) != original.Tree) return false;
        return RepositoryOperationDetector.Detect(before.GitDirectory!, await heads.ReadRootRefsAsync(before, ct).ConfigureAwait(false)) == RepositoryOperation.None
            && before.IndexFingerprint == await IndexFingerprint.ReadAsync(before.GitDirectory!, ct).ConfigureAwait(false);
    }
}
