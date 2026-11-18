using Trag.Domain;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// A detached checkout: git switch --detach moves HEAD onto the commit itself and refuses to overwrite local changes.
internal sealed class DetachedCheckoutWriter(WriteCommandRunner git, HeadReader heads)
{
    public async Task<GitWriteResult> CheckoutCommitAsync(BranchOperation operation, RepositorySnapshot before, CancellationToken ct)
    {
        var target = operation.Name.Trim();
        if (await git.ResolveAsync(before.Root, target + "^{commit}", ct).ConfigureAwait(false) is not { } id)
            return GitWriteResult.Rejected(HistoryText.CommitGone);
        if (before.Branch is null && before.Head == id) return new(GitWriteOutcome.Succeeded, BranchText.AlreadyOn(GitObjectId.Short(id)));
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["switch", "--detach", id], token, literalPathspecs: false),
            (command, failure, token) => VerifyAsync(before, id, command, failure, token),
            (command, _) => new(GitWriteOutcome.Uncertain, BranchOutcomeText.CheckoutNotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(20), ct,
            new StoppedWriteLock([Path.Combine(before.GitDirectory!, "index"), .. StoppedWriteLock.HeadStorage(before)])).ConfigureAwait(false);
    }

    private async Task<GitWriteResult> VerifyAsync(RepositorySnapshot before, string id, GitResult? command, Exception? failure, CancellationToken ct)
    {
        var (head, branch) = await heads.ReadAsync(before.Root, ct).ConfigureAwait(false);
        if (branch is null && head == id) return new(GitWriteOutcome.Succeeded, BranchOutcomeText.Detached(GitObjectId.Short(id)));
        if (head == before.Head && branch == before.Branch)
            return BranchFailureText.Unchanged(command, failure, BranchOutcomeText.CheckoutInterrupted);
        return new(GitWriteOutcome.Uncertain, BranchOutcomeText.CheckoutUnexpected,
            GitOutputText.Details(command));
    }
}
