using System.Text;
using Trag.Application.Ports.Git;
using Trag.Domain;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// A commit Git applies to the current branch as a new commit: undone (revert) or applied again (cherry-pick). It runs on
// the real index with a clean working tree, Git makes the commit with its own message and no editor opens, and the
// outcome is judged from the repository afterwards: a new commit on top of the old HEAD, a pick stopped at conflicts and
// left in progress for the user, or nothing at all when there is nothing left to change, in which case a pick Git left
// in progress is given up at once. A commit the branch has already is not cherry-picked. A stopped pick is finished
// with the message Git prepared for it once its conflicts are resolved, or aborted back to where the branch was.
internal sealed class PickWriter(WriteCommandRunner git, IRepositoryReader reader)
{

    // What one kind of pick runs and says. AlreadyHere is the refusal of a commit the branch has; null when that is allowed.
    private sealed record Kind(string[] Command, RepositoryOperation Operation, string MergeCommit,
        Func<string, string?, string>? AlreadyHere, Func<string, string> Empty, Func<string, string, string> Done, Func<string, int, string> Stopped);

    private static readonly Kind Revert = new(["revert", "--no-edit", "--no-rerere-autoupdate"], RepositoryOperation.Revert,
        RevertText.MergeCommit, null, RevertText.AlreadyUndone, RevertText.Reverted, RevertText.Stopped);

    // A commit empty from the start is picked as the empty commit it is; one that only becomes empty here still stops.
    private static readonly Kind CherryPick = new(["cherry-pick", "--allow-empty", "--no-rerere-autoupdate"], RepositoryOperation.CherryPick,
        CherryPickText.MergeCommit, CherryPickText.AlreadyOnBranch, CherryPickText.NothingToApply,
        CherryPickText.Picked, CherryPickText.Stopped);

    public Task<GitWriteResult> StartAsync(RevertRequest request, RepositorySnapshot fresh, CancellationToken ct) => StartAsync(Revert, request.Commit, fresh, ct);
    public Task<GitWriteResult> StartAsync(CherryPickRequest request, RepositorySnapshot fresh, CancellationToken ct) => StartAsync(CherryPick, request.Commit, fresh, ct);

    private async Task<GitWriteResult> StartAsync(Kind kind, string commit, RepositorySnapshot fresh, CancellationToken ct)
    {
        if (!await git.RefExistsAsync(fresh.Root, commit + "^{commit}", ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(HistoryText.CommitGone);
        if (await git.RefExistsAsync(fresh.Root, commit + "^2", ct).ConfigureAwait(false)) return GitWriteResult.Rejected(kind.MergeCommit);
        var shortId = GitObjectId.Short(commit);
        // Git takes the oldest commit of a shallow clone, whose parent is not here, for a first commit and its whole content
        // for its change.
        if (!await git.RefExistsAsync(fresh.Root, commit + "^", ct).ConfigureAwait(false)
            && CommitObject.NamesParent((await git.RunAsync(fresh.Root, ["cat-file", "commit", commit], ct).ConfigureAwait(false)).StandardOutput))
            return GitWriteResult.Rejected(PickText.ShallowBoundary(kind.Operation, shortId));
        if (kind.AlreadyHere is { } alreadyHere
            && (await git.RunAsync(fresh.Root, ["merge-base", "--is-ancestor", commit, "HEAD"], ct).ConfigureAwait(false)).ExitCode == 0)
            return GitWriteResult.Rejected(alreadyHere(shortId, fresh.Branch));
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, [.. kind.Command, commit], token, literalPathspecs: false, variables: WriteCommandRunner.NoEditor),
            (command, failure, token) => VerifyAsync(kind, fresh, shortId, command, failure, token),
            (command, _) => new(GitWriteOutcome.Uncertain, PickText.NotVerified(kind.Operation), GitOutputText.Details(command)),
            OperationTimeLimit.Of(fresh, TimeSpan.FromMinutes(5)), TimeSpan.FromSeconds(30), ct,
            Left(fresh)).ConfigureAwait(false);
    }

    private async Task<GitWriteResult> VerifyAsync(Kind kind, RepositorySnapshot before, string shortId, GitResult? command, Exception? failure, CancellationToken ct)
    {
        var after = await reader.ReadAsync(before.Root, ct).ConfigureAwait(false);
        var details = GitOutputText.Details(command);
        if (CommitSignings.Refused(after.Signing, details))
            return await NotCommittedAsync(kind, before, after, details,
                PickText.NotSigned(kind.Operation, after.Signing), PickText.NotSignedUndone(after.Signing), ct).ConfigureAwait(false);
        if (details.Contains("'prepare-commit-msg' hook failed", StringComparison.Ordinal))
            return await NotCommittedAsync(kind, before, after, details, PickText.HookRefused(kind.Operation), PickText.HookRefusedUndone, ct).ConfigureAwait(false);
        if (after.Operation == kind.Operation && after.Conflicts.Any())
            return new(GitWriteOutcome.NeedsResolution, kind.Stopped(shortId, after.Conflicts.Count()), details);
        if (after.Operation == RepositoryOperation.None && after.Head is { } head && head != before.Head
            && await git.ResolveAsync(before.Root, head + "^", ct).ConfigureAwait(false) == before.Head)
            return new(GitWriteOutcome.Succeeded, kind.Done(shortId, GitObjectId.Short(head)), details);
        // An empty pick Git keeps in progress changed nothing; it goes at once.
        if (after.Operation == kind.Operation && after.Head == before.Head && !after.HasChanges)
        {
            var abort = await git.RunAsync(before.Root, [kind.Command[0], "--abort"], ct, variables: WriteCommandRunner.NoEditor).ConfigureAwait(false);
            return abort.ExitCode == 0 ? GitWriteResult.Rejected(kind.Empty(shortId), details)
                : new(GitWriteOutcome.Uncertain, PickText.NotVerified(kind.Operation), GitOutputText.Details(abort));
        }
        if (after.Operation == RepositoryOperation.None && after.Head == before.Head && !after.HasChanges)
        {
            if (failure is not null || command is null) return new(GitWriteOutcome.Cancelled, PickText.Interrupted(kind.Operation));
            return GitWriteResult.Rejected(NothingToCommit(details) ? kind.Empty(shortId) : PickText.Failed(kind.Operation), details);
        }
        return new(GitWriteOutcome.Uncertain, PickText.NotVerified(kind.Operation), details);
    }

    // A pick Git applied but did not commit, as a key that could not sign or a failed prepare-commit-msg hook leaves it:
    // a cherry-pick is left ready to finish. A revert is left with no revert to finish, only its changes staged; the tree
    // was clean before it started, so they go back.
    private async Task<GitWriteResult> NotCommittedAsync(Kind kind, RepositorySnapshot before, RepositorySnapshot after, string details,
        string ready, string undone, CancellationToken ct)
    {
        if (after.Operation == kind.Operation) return new(GitWriteOutcome.NeedsResolution, ready, details);
        var reset = await git.RunAsync(before.Root, ["reset", "--merge"], ct).ConfigureAwait(false);
        return reset.ExitCode == 0 ? GitWriteResult.Rejected(undone, details)
            : new(GitWriteOutcome.Uncertain, PickText.NotVerified(kind.Operation), GitOutputText.Details(reset));
    }

    public async Task<GitWriteResult> ContinueAsync(RepositorySnapshot fresh, CancellationToken ct)
    {
        var operation = fresh.Operation;
        var shortId = GitObjectId.Short(fresh.PickHead!);
        // Git's prepared message ends with its list of the conflicts. The list goes here, written whole, and Git marks its
        // own lines with a character none of the message's lines starts with, so a line starting with '#' stays.
        var prepared = Path.Combine(fresh.GitDirectory!, "MERGE_MSG");
        var message = MergeMessages.Clean(await File.ReadAllTextAsync(prepared, ct).ConfigureAwait(false)) + "\n";
        await File.WriteAllTextAsync(prepared, message, new UTF8Encoding(false), CancellationToken.None).ConfigureAwait(false);
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, ["-c", "core.commentChar=" + MessageLines.CommentChar(message), Command(operation), "--continue"], token,
                literalPathspecs: false, variables: WriteCommandRunner.NoEditor),
            async (command, _, token) =>
            {
                var after = await reader.ReadAsync(fresh.Root, token).ConfigureAwait(false);
                var details = GitOutputText.Details(command);
                if (after.Operation == RepositoryOperation.None && after.Head is { } head && head != fresh.Head
                    && await git.ResolveAsync(fresh.Root, head + "^", token).ConfigureAwait(false) == fresh.Head)
                    return new(GitWriteOutcome.Succeeded, PickText.Finished(operation, shortId, GitObjectId.Short(head)), details);
                return after.Operation == operation && after.Head == fresh.Head
                    ? GitWriteResult.Rejected(CommitSignings.Refused(after.Signing, details) ? PickText.NotSigned(operation, after.Signing) : PickText.NotFinished(operation), details)
                    : new(GitWriteOutcome.Uncertain, PickText.NotVerified(operation), details);
            },
            (command, _) => new(GitWriteOutcome.Uncertain, PickText.NotVerified(operation), GitOutputText.Details(command)),
            OperationTimeLimit.Of(fresh, TimeSpan.FromMinutes(5)), TimeSpan.FromSeconds(30), ct, Left(fresh))
            .ConfigureAwait(false);
    }

    // Abort is answered by no pick left and the branch at the commit it was on.
    public Task<GitWriteResult> AbortAsync(RepositorySnapshot fresh, CancellationToken ct)
    {
        var operation = fresh.Operation;
        return VerifiedGitOperation.RunAsync(
            token => git.RunAsync(fresh.Root, [Command(operation), "--abort"], token, variables: WriteCommandRunner.NoEditor),
            async (command, _, token) => await reader.ReadAsync(fresh.Root, token).ConfigureAwait(false) is { Operation: RepositoryOperation.None } after
                && after.Head == fresh.Head && after.Branch == fresh.Branch
                    ? new(GitWriteOutcome.Succeeded, PickText.Aborted(operation, fresh.Branch), GitOutputText.Details(command))
                    : new(GitWriteOutcome.Uncertain, PickText.AbortNotVerified(operation), GitOutputText.Details(command)),
            (command, _) => new(GitWriteOutcome.Uncertain, PickText.AbortNotVerified(operation), GitOutputText.Details(command)),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), ct, Left(fresh, "ORIG_HEAD"));
    }

    // The refs a pick command writes: HEAD, the branch and AUTO_MERGE, the sequencer's CHERRY_PICK_HEAD and REVERT_HEAD,
    // the root refs it names, and packed-refs, which Git locks as well to delete one.
    private static StoppedWriteLock Left(RepositorySnapshot fresh, params string[] roots) =>
        new(StoppedWriteLock.HeadStorage(fresh, ["CHERRY_PICK_HEAD", "REVERT_HEAD", .. roots]));

    private static string Command(RepositoryOperation operation) => operation == RepositoryOperation.CherryPick ? CherryPick.Command[0] : Revert.Command[0];

    // Git's own line for a pick that would change nothing on the branch.
    internal static bool NothingToCommit(string details) => details.Contains("nothing to commit", StringComparison.Ordinal);
}
