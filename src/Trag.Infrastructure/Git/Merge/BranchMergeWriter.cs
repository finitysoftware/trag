using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Write;
using Trag.Domain.Merge;

namespace Trag.Infrastructure.Git.Merge;

// A branch the user chose, merged into the current one. The router has checked the request, that the repository is the
// one shown, and that it has a branch with a commit and a clean working tree; this adds what only Git can tell: a commit
// the branch does not contain yet. Then it is a verified merge like a pull's, with the user's message and fast-forward choice.
internal sealed class BranchMergeWriter(WriteCommandRunner git, VerifiedMerge merge)
{
    public async Task<GitWriteResult> MergeAsync(MergeRequest request, RepositorySnapshot fresh, CancellationToken ct)
    {
        var contained = await git.RunAsync(fresh.Root, ["merge-base", "--is-ancestor", request.FromHead, fresh.Head!], ct).ConfigureAwait(false);
        if (contained.ExitCode == 0) return new(GitWriteOutcome.Succeeded, MergeOutcomeText.AlreadyContains(fresh.Branch, request.FromName));
        if (contained.ExitCode != 1) return GitWriteResult.Rejected(MergeOutcomeText.CommitGone);
        return await merge.MergeAsync(new(fresh, request.FromHead, MergeKind.Branch, request.Message, request.NoFastForward) { Progress = request.Progress }, ct)
            .ConfigureAwait(false);
    }
}
