using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// The current branch moved back to one of its commits by Git's reset, in the mode asked: soft keeps the index and the
// working tree, mixed resets the index, hard the index and the tracked files; untracked files and the submodules' own
// working trees, which submodule.recurse would reset as well, are never touched. It runs on the real index, and the
// outcome is judged from the repository afterwards: the same branch on the commit, or the branch still where it was.
internal sealed class ResetWriter(WriteCommandRunner git, IRepositoryReader reader)
{
    public Task<GitWriteResult> ResetAsync(ResetRequest request, RepositorySnapshot fresh, CancellationToken ct) =>
        VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, ["-c", "submodule.recurse=false", "reset", Flag(request.Mode), request.Commit], token),
            (command, failure, token) => VerifyAsync(request, fresh, command, failure, token),
            (command, _) => new(GitWriteOutcome.Uncertain, ResetText.NotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), ct,
            new StoppedWriteLock([Path.Combine(fresh.GitDirectory!, "index"), .. StoppedWriteLock.HeadStorage(fresh, "ORIG_HEAD")]));

    private static string Flag(ResetMode mode) => mode switch { ResetMode.Soft => "--soft", ResetMode.Mixed => "--mixed", _ => "--hard" };

    private async Task<GitWriteResult> VerifyAsync(ResetRequest request, RepositorySnapshot before, GitResult? command, Exception? failure, CancellationToken ct)
    {
        var after = await reader.ReadAsync(before.Root, ct).ConfigureAwait(false);
        var details = GitOutputText.Details(command);
        if (after.Branch == before.Branch && after.Head == request.Commit)
            return new(GitWriteOutcome.Succeeded, ResetText.Done(before.Branch!, GitObjectId.Short(request.Commit), request.Mode), details);
        // The branch where it was is Git's refusal only when Git ran to the end; a Git that was stopped refused nothing.
        if (after.Branch == before.Branch && after.Head == before.Head)
            return failure is null && command is not null ? GitWriteResult.Rejected(ResetText.Failed, details) : new(GitWriteOutcome.Cancelled, ResetText.Interrupted);
        return new(GitWriteOutcome.Uncertain, ResetText.NotVerified, details);
    }
}
