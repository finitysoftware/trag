using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// A commit is made once and verified afterwards, because Git can succeed and still be interrupted before Trag hears it.
// Everything after the command runs on its own 15 second token, so a cancelled request still gets a verified answer.
// An amend is verified the same way, against the parents of the commit it replaces, read before Git replaces it.
internal sealed class CommitWriter(WriteCommandRunner git, HeadReader heads, ExpectedStateCheck snapshots)
{
    public async Task<GitWriteResult> CommitAsync(CommitRequest request, RepositorySnapshot before, IndexTransaction transaction, CancellationToken ct)
    {
        if (!await snapshots.StillExpectedAsync(before, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(WriteText.ChangedDuring);
        var replaced = request.Amend ? (await heads.ReadCommitAsync(before.Root, before.Head!, ct).ConfigureAwait(false)).Parent : null;
        // Git would write the oldest commit of a shallow clone anew without the parent it names.
        if (replaced == "" && await heads.NamesParentAsync(before.Root, before.Head!, ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(CommitOutcomeText.ShallowAmend);
        string[] arguments = request.Amend
            ? ["commit", "--amend", "--file=-", "--cleanup=verbatim"]
            : ["commit", "--file=-", "--cleanup=verbatim"];
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, arguments, token, transaction,
                Encoding.UTF8.GetBytes(MessageLines.ForGit(request.Message)), "trag-commit-" + transaction.Id, literalPathspecs: false),
            (command, failure, token) => VerifyCommitAsync(before, replaced, transaction, command, failure, ct, token),
            (_, _) => transaction.KeepForReview(CommitOutcomeText.StoppedUnverified),
            operationTimeout: null, TimeSpan.FromSeconds(15), ct, new StoppedWriteLock(StoppedWriteLock.HeadStorage(before))).ConfigureAwait(false);
    }

    // Verification continues after caller cancellation and must finish before the index lock is released.
    private async Task<GitWriteResult> VerifyCommitAsync(RepositorySnapshot before, string? replaced, IndexTransaction transaction,
        GitResult? command, Exception? failure, CancellationToken ct, CancellationToken verification)
    {
        var (head, branch) = await heads.ReadAsync(before.Root, verification).ConfigureAwait(false);
        var state = new CommitStateCheck(before, head, branch, replaced);
        var committed = await FindCommitAsync(before, transaction.Id, verification).ConfigureAwait(false);
        if (committed is null && state.CanUseHead(command?.ExitCode))
            committed = await FindCommitWithoutReflogAsync(state, transaction, verification).ConfigureAwait(false);
        if (committed is not null)
            return await PublishCommitAsync(state, transaction, committed, ct, verification).ConfigureAwait(false);
        if (state.HasMoved || command?.ExitCode == 0 || !await snapshots.StillExpectedAsync(before, verification).ConfigureAwait(false))
            return transaction.KeepForReview(CommitOutcomeText.Uncertain);
        if (failure is OperationCanceledException || ct.IsCancellationRequested)
            return new(GitWriteOutcome.Cancelled, CommitOutcomeText.Canceled);
        // Trag stopped Git, not Git the commit: a hook printed more than Trag holds.
        if (failure is GitOutputLimitException) return GitWriteResult.Rejected(WriteText.TooMuchOutput);
        if (command is null) return GitWriteResult.Rejected(CommitOutcomeText.Rejected, CommitOutcomeText.StoppedEarly);
        var details = GitOutputText.Details(command);
        return GitWriteResult.Rejected(state.Refusal(details), details);
    }

    // The index's tree as git write-tree wrote it; null when it could not.
    private static string? TreeOf(GitResult tree) => tree.ExitCode == 0 ? GitBytes.Text(tree.StandardOutput) : null;

    private async Task<string?> FindCommitWithoutReflogAsync(CommitStateCheck state, IndexTransaction transaction, CancellationToken ct)
    {
        var tree = await git.RunAsync(state.Before.Root, ["write-tree"], ct, transaction).ConfigureAwait(false);
        if (state.Head is null) return null;
        var commit = await heads.ReadCommitAsync(state.Before.Root, state.Head, ct).ConfigureAwait(false);
        return state.MatchesIndex(commit, TreeOf(tree)) ? state.Head : null;
    }

    private async Task<GitWriteResult> PublishCommitAsync(CommitStateCheck state, IndexTransaction transaction, string committed,
        CancellationToken caller, CancellationToken verification)
    {
        var before = state.Before;
        var commit = await heads.ReadCommitAsync(before.Root, committed, verification).ConfigureAwait(false);
        var tree = await git.RunAsync(before.Root, ["write-tree"], verification, transaction).ConfigureAwait(false);
        var matches = state.MatchesCommit(committed) && state.MatchesIndex(commit, TreeOf(tree))
            && RepositoryOperationDetector.Detect(before.GitDirectory!, await heads.ReadRootRefsAsync(before, verification).ConfigureAwait(false)) == RepositoryOperation.None
            && before.IndexFingerprint == await IndexFingerprint.ReadAsync(before.GitDirectory!, verification).ConfigureAwait(false);
        if (!matches)
            return transaction.KeepForReview(CommitOutcomeText.ChangedBeforeSync, committed);
        try
        {
            await transaction.PublishAsync(verification).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return transaction.KeepForReview(CommitOutcomeText.NotSynchronized, committed);
        }
        return new(GitWriteOutcome.Succeeded, state.SuccessMessage(caller.IsCancellationRequested), CommitId: committed);
    }

    // The reflog carries the action Trag set for this transaction, so its own commit is found again by id, not by guessing.
    // A hook's own Git commands inherit that action, and one that moves HEAD in place leaves an entry naming the commit
    // the branch already had; the commit this transaction made is always a new one.
    private async Task<string?> FindCommitAsync(RepositorySnapshot before, string id, CancellationToken ct)
    {
        var result = await git.RunAsync(before.Root, ["reflog", "show", "-n", "16", "--no-show-signature", "--format=%H%x00%gs", "-z", "HEAD"], ct).ConfigureAwait(false);
        if (result.ExitCode != 0) return null;
        var fields = NulRecordReader.Records(result.StandardOutput).ToArray();
        for (var i = 0; i + 1 < fields.Length; i += 2)
            if (GitBytes.Text(fields[i + 1]).StartsWith("trag-commit-" + id + ":", StringComparison.Ordinal) && GitBytes.Text(fields[i]) != before.Head)
                return GitBytes.Text(fields[i]);
        return null;
    }
}
