using Trag.Domain;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Git's own rule decides what is safe: -d refuses a branch whose commits are merged nowhere, and the caller asks before forcing with -D.
// A branch that moved since the question goes with -d again: the commits it gained are not the ones the user agreed to lose.
internal sealed class BranchDeleteWriter(WriteCommandRunner git)
{
    public async Task<GitWriteResult> DeleteBranchAsync(BranchOperation operation, RepositorySnapshot before, string name, CancellationToken ct)
    {
        if (await git.ResolveAsync(before.Root, "refs/heads/" + name, ct).ConfigureAwait(false) is not { } id)
            return GitWriteResult.Rejected(BranchOutcomeText.Gone);
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["branch", operation.ForceAt == id ? "-D" : "-d", name], token),
            async (command, _, token) =>
            {
                var remains = await git.RefExistsAsync(before.Root, "refs/heads/" + name, token).ConfigureAwait(false);
                if (!remains) return await BranchSection.FollowAsync(git, before.Root, name, null, token).ConfigureAwait(false)
                    ? new(GitWriteOutcome.Succeeded, BranchOutcomeText.Deleted(name, GitObjectId.Short(id, 8)))
                    : new(GitWriteOutcome.Uncertain, BranchOutcomeText.DeleteNotVerified, GitOutputText.Details(command));
                if (command is null) return new(GitWriteOutcome.Cancelled, BranchOutcomeText.DeleteInterrupted(name));
                return Refused(name, GitOutputText.Details(command));
            },
            (command, _) => new(GitWriteOutcome.Uncertain, BranchOutcomeText.DeleteNotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(20), ct,
            new StoppedWriteLock([.. StoppedWriteLock.RefStorage(before.CommonDirectory!, "refs/heads/" + name, "packed-refs"),
                Path.Combine(before.CommonDirectory!, "config")])).ConfigureAwait(false);
    }

    // Why Git kept the branch, in its own words.
    internal static GitWriteResult Refused(string name, string text)
    {
        if (text.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
            return new(GitWriteOutcome.NeedsResolution, BranchOutcomeText.NotFullyMerged(name), text);
        if (text.Contains("used by worktree", StringComparison.OrdinalIgnoreCase))
            return GitWriteResult.Rejected(BranchOutcomeText.InOtherWorktree(name), text);
        return GitWriteResult.Rejected(BranchOutcomeText.DeleteFailed, text);
    }
}
