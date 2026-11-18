using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.Git.Write;

// Branch changes run against the real index: git switch owns that update and refuses to overwrite local changes itself.
// Checkout and deletion keep their own flows; creation and switching share the same checks and verification; renaming
// moves a branch without touching the index. A submodule moves only when the user updates it, as after a merge, so the
// user's submodule.recurse does not reach a switch.
internal sealed class BranchWriter(WriteCommandRunner git, HeadReader heads, GitRefNameValidator refNames, BranchDeleteWriter deletes, DetachedCheckoutWriter checkouts)
{
    public async Task<GitWriteResult> BranchAsync(BranchRequest request, RepositorySnapshot before, CancellationToken ct)
    {
        var operation = request.Branch;
        if (request.Action == BranchAction.Checkout) return await checkouts.CheckoutCommitAsync(operation, before, ct).ConfigureAwait(false);
        var change = BranchChange.From(request);
        if (!await refNames.IsValidBranchNameAsync(before.Root, change.Name, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(BranchOutcomeText.NameInvalid);
        if (request.Action == BranchAction.Delete) return await deletes.DeleteBranchAsync(operation, before, change.Name, ct).ConfigureAwait(false);
        if (await ValidateReferencesAsync(before.Root, change, ct).ConfigureAwait(false) is { } rejected) return rejected;
        var exists = await git.RefExistsAsync(before.Root, change.LocalRef, ct).ConfigureAwait(false);
        if (change.CheckExisting(exists, before.Branch) is { } existing) return existing;
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["-c", "submodule.recurse=false", .. change.Arguments()], token, literalPathspecs: false),
            (command, failure, token) => VerifyAsync(change, before, command, failure, token),
            (command, _) => new(GitWriteOutcome.Uncertain, BranchOutcomeText.NotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(20), ct, new StoppedWriteLock([Path.Combine(before.GitDirectory!, "index"),
                .. StoppedWriteLock.HeadStorage(before), .. StoppedWriteLock.RefStorage(before.CommonDirectory!, change.LocalRef)])).ConfigureAwait(false);
    }

    // The new name has to be one Git takes and no other branch has; the branch has to be there still. Done is the new
    // name there with every HEAD that was on the branch, and the branch's settings, on it; the old one still there with no
    // new one is a rename that did not happen. Git moves the HEAD of each worktree that has the branch checked out, this one
    // or a linked one, and then the settings, only after the branch itself, so a Git stopped in between leaves that HEAD on
    // a name that is gone, which reads as a branch with no commits yet, or the settings under the old name; the write moves
    // them on as Git would have.
    public async Task<GitWriteResult> RenameAsync(BranchRenameRequest request, RepositorySnapshot before, CancellationToken ct)
    {
        var (from, to) = ("refs/heads/" + request.Name, "refs/heads/" + request.NewName);
        if (!await refNames.IsValidBranchNameAsync(before.Root, request.NewName, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(BranchOutcomeText.NameInvalid);
        if (!await git.RefExistsAsync(before.Root, from, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(BranchOutcomeText.Gone);
        if (await git.RefExistsAsync(before.Root, to, ct).ConfigureAwait(false)) return GitWriteResult.Rejected(BranchOutcomeText.NameTaken(request.NewName));
        var heads = await HeadsOnAsync(before, from, ct).ConfigureAwait(false);
        return await VerifiedGitOperation.RunAsync(
            token => git.RunAsync(before.Root, ["branch", "-m", "--", request.Name, request.NewName], token),
            async (command, failure, token) =>
            {
                var renamed = await git.RefExistsAsync(before.Root, to, token).ConfigureAwait(false);
                var left = await git.RefExistsAsync(before.Root, from, token).ConfigureAwait(false);
                if (renamed && await MoveHeadsAsync(heads, from, to, token).ConfigureAwait(false)
                    && await BranchSection.FollowAsync(git, before.Root, request.Name, request.NewName, token).ConfigureAwait(false))
                    return new(GitWriteOutcome.Succeeded, BranchOutcomeText.Renamed(request.Name, request.NewName), GitOutputText.Details(command));
                if (left) return BranchFailureText.Unchanged(command, failure, BranchOutcomeText.Interrupted);
                return new(GitWriteOutcome.Uncertain, BranchOutcomeText.UnexpectedState, GitOutputText.Details(command));
            },
            (command, _) => new(GitWriteOutcome.Uncertain, BranchOutcomeText.NotVerified, GitOutputText.Details(command)),
            TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(20), ct, new StoppedWriteLock([.. StoppedWriteLock.RefStorage(before.CommonDirectory!, from, "packed-refs"),
                .. heads.SelectMany(directory => StoppedWriteLock.RefStorage(directory, "HEAD")), Path.Combine(before.CommonDirectory!, "config")]))
            .ConfigureAwait(false);
    }

    // The git directory of each worktree whose HEAD is on the branch: the main one and every linked one. Git answers where
    // each HEAD points, since a reftable repository keeps only a stub in the HEAD file.
    private async Task<List<string>> HeadsOnAsync(RepositorySnapshot before, string branch, CancellationToken ct)
    {
        var linked = Path.Combine(before.CommonDirectory!, "worktrees");
        var heads = new List<string>();
        foreach (var directory in (string[])[before.CommonDirectory!, .. Directory.Exists(linked) ? Directory.GetDirectories(linked) : []])
            if (await HeadAsync(directory, ct).ConfigureAwait(false) == branch) heads.Add(directory);
        return heads;
    }

    private async Task<bool> MoveHeadsAsync(List<string> heads, string from, string to, CancellationToken ct)
    {
        foreach (var directory in heads)
            if (await HeadAsync(directory, ct).ConfigureAwait(false) == from
                && (await git.RunAsync(directory, ["symbolic-ref", "HEAD", to], ct).ConfigureAwait(false)).ExitCode != 0) return false;
        return true;
    }

    private async Task<string> HeadAsync(string directory, CancellationToken ct) =>
        GitBytes.Text((await git.RunAsync(directory, ["symbolic-ref", "--quiet", "HEAD"], ct).ConfigureAwait(false)).StandardOutput);

    private async Task<GitWriteResult?> ValidateReferencesAsync(string root, BranchChange change, CancellationToken ct)
    {
        if (change.Start is { } start && !await git.RefExistsAsync(root, start + "^{commit}", ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(BranchOutcomeText.StartGone);
        if (change.Track is { } track && !await git.RefExistsAsync(root, track + "^{commit}", ct).ConfigureAwait(false))
            return GitWriteResult.Rejected(BranchOutcomeText.TrackGone);
        return null;
    }

    private async Task<GitWriteResult> VerifyAsync(BranchChange change, RepositorySnapshot before,
        GitResult? command, Exception? failure, CancellationToken ct)
    {
        var (head, branch) = await heads.ReadAsync(before.Root, ct).ConfigureAwait(false);
        var created = await git.RefExistsAsync(before.Root, change.LocalRef, ct).ConfigureAwait(false);
        if (change.CompletedResult(before, head, branch, created) is { } completed) return completed;
        if (change.IsUnchanged(before, head, branch, created))
            return BranchFailureText.Unchanged(command, failure, BranchOutcomeText.Interrupted);
        return new(GitWriteOutcome.Uncertain, BranchOutcomeText.UnexpectedState,
            GitOutputText.Details(command));
    }
}
