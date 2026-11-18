using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.History;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Branches;

// The repository's branches: the list, which follows the snapshot (the checked-out branch, the local and remote-tracking
// refs and the remotes' addresses, RepositorySnapshot.BranchesKey): it is read when one of them moved, by a commit, a
// switch, a fetch, a remote's new address or a branch write of Trag's or of any other program's, and once more before a
// move to a commit is decided; and the writes that switch to, create, check out and delete a branch, each through the
// repository's writer. A move off a detached HEAD whose commits no branch holds waits for the user: keep them as a branch
// first, or leave them behind. The decision belongs to the HEAD that asked for it. Going to a commit of the history ends
// in nothing to do (the status line says why) or in a question for the user.
public sealed class BranchesModel : ObservableObject
{
    private readonly StatusBarState statusBar;
    private DetachedMove? waiting;

    public BranchesModel(Repository repository, IBranchWorkspace? reader, StatusBarState statusBar)
    {
        Repository = repository;
        this.statusBar = statusBar;
        List = new(repository, reader is null ? null : async (expected, ct) => await reader.ReadBranchesAsync(expected, ct) is { } set ? BranchCatalog.Create(set, expected) : null,
            BranchCatalog.Empty, key: snapshot => snapshot.BranchesKey) { FailedMessage = Failures.BranchesNotRead };
    }

    public Repository Repository { get; }
    public Loader<BranchCatalog> List { get; }

    // The detached HEAD whose commits the waiting move would leave behind; null while no move waits. It is announced
    // whenever a move starts waiting or the decision is made, the same HEAD asking again included.
    public string? Asking => waiting?.Head;
    // The name offered for the branch that would keep the commits the waiting move leaves behind.
    public string KeptName => Asking is { } head ? BranchNames.SuggestKept(head, List.Value.LocalNames) : "";

    // Switching to a branch of the list goes past a detached HEAD with commits of its own; a branch whose local counterpart
    // is checked out already needs no write, and the status line says so.
    public Task<GitWriteResult?> SwitchToAsync(BranchInfo branch)
    {
        if (BranchMoves.SwitchTo(branch, List.Value) is { } move) return LeaveDetachedThenAsync(() => MoveAsync(branch, move));
        statusBar.Status = BranchText.AlreadyOn(branch.LocalName);
        return RepositoryWriter.NotRun;
    }

    // A new branch from a remote one checks out files whose Git LFS content may be only on that remote, so the content
    // comes down first, with the remote's credentials, as before a merge of it; the switch runs only after it did.
    private async Task<GitWriteResult?> MoveAsync(BranchInfo branch, BranchMove move)
    {
        if (move.Action == BranchAction.Create && branch.Remote is { } remote)
        {
            var downloaded = await Repository.Writer.RunAsync(expected => new LfsDownloadRequest(expected, remote, branch.Head, BranchOutcomeText.LfsNotDownloaded));
            if (downloaded?.Outcome != GitWriteOutcome.Succeeded) return downloaded;
        }
        return await WriteAsync(move.Action, move.Operation);
    }

    // History goes to a commit while the repository is writable; a merge or rebase in progress still asks (the question
    // is blocked), any other operation keeps History read-only.
    public bool CanGoTo => Repository.IsWritable && Repository.Snapshot is { } snapshot
        && !(snapshot.HasUnsupportedWriteState && !Repository.HasMerge && !Repository.HasRebase);

    // Going to a commit: at HEAD on a branch already there is nothing to do; otherwise the branches are read again, and if
    // neither HEAD nor a branch moved meanwhile (a refresh for a saved file changes neither), the user is asked: to switch
    // to the branch whose tip the commit is, since a switch to another branch is a decision and not a side effect of a
    // double-click, or else for a branch there or a detached look, which a HEAD detached at the commit already is. Null
    // when nothing is left to ask; the status line says why.
    public async Task<GoToQuestion?> GoToAsync(HistoryCommit commit)
    {
        if (!CanGoTo || Repository.Snapshot is not { } snapshot) return null;
        if (GoToCommit.IsAt(snapshot, commit.Id) && !snapshot.IsDetached) return Nothing(BranchText.AlreadyOnCommit);
        await List.ReloadAsync();
        if (Repository.Snapshot is not { } now || !now.IsSameHead(snapshot.Root, snapshot.Head) || !Equals(now.BranchesKey, snapshot.BranchesKey))
            return Nothing(BranchText.MovedWhileReading);
        var tip = GoToCommit.Tip(List.Value, commit.Id);
        if (tip is null && GoToCommit.IsAt(snapshot, commit.Id)) return Nothing(BranchText.AlreadyOnCommit);
        return new(tip, BranchNames.SuggestFor(commit.Subject, commit.Id, List.Value.LocalNames));
    }

    private GoToQuestion? Nothing(string why)
    {
        statusBar.Status = why;
        return null;
    }

    // Deleting a branch the user chose: a merged local branch goes at once, one Git refuses as unmerged waits for the user,
    // and a remote branch always waits, since deleting it reaches the remote. True when the user has to confirm; the
    // confirmed delete is DeleteAsync(branch, confirmed: true).
    public async Task<bool> RequestDeleteAsync(BranchInfo branch) =>
        branch.IsRemote || await DeleteAsync(branch, confirmed: false) is { Outcome: GitWriteOutcome.NeedsResolution };

    // A delete Git may refuse for an unmerged local branch; once the user confirmed, the local one goes by force and a
    // remote one on its remote.
    public Task<GitWriteResult?> DeleteAsync(BranchInfo branch, bool confirmed) =>
        Repository.Writer.RunAsync(expected => BranchMoves.Delete(expected, branch, confirmed));

    public Task<GitWriteResult?> RenameAsync(BranchInfo branch, string newName) =>
        Repository.Writer.RunAsync(expected => new BranchRenameRequest(expected, branch.Name, newName.Trim()));

    public Task<GitWriteResult?> WriteAsync(BranchAction action, BranchOperation operation) =>
        Repository.Writer.RunAsync(expected => new BranchRequest(expected, operation, action));

    // Everything that moves HEAD off a detached commit with work of its own goes through here: the move waits for the
    // decision; any other move runs at once.
    public Task<GitWriteResult?> LeaveDetachedThenAsync(Func<Task<GitWriteResult?>> move)
    {
        if (Repository.Snapshot is not { LeavesCommitsBehind: true, Head: { } head } snapshot) return move();
        Wait(new(snapshot.Root, head, move));
        return RepositoryWriter.NotRun;
    }

    // A new branch moves HEAD only when the user switches to it and it starts elsewhere; one created at HEAD holds what a
    // detached HEAD has of its own. So only that create goes past the question, like every other move; submit writes it.
    public Task<GitWriteResult?> CreateAsync(BranchOperation create, Func<Task<GitWriteResult?>> submit) =>
        create.StartPoint is not null && create.Switch ? LeaveDetachedThenAsync(submit) : submit();

    public void StayDetached() => Wait(null);

    // Keeping the commits and then leaving: two writes, one operation, busy until the move ran.
    public Operation Leaving { get; } = new();

    // Keeping creates a branch at HEAD without switching, so the commits are reachable before the move runs. Null once the
    // move ran, and the status line names the branch that keeps the commits beside the move's outcome, which alone, a
    // refusal above all, would not mention it; otherwise why it did not run, and the move still waits.
    public Task<string?> KeepThenLeaveAsync(string name) => Leaving.RunAsync<string>(async _ =>
    {
        if (waiting is not { } move || !move.Matches(Repository.Snapshot)) return BranchText.ChooseAgain;
        var kept = await WriteAsync(BranchAction.Create, new(name, null, false));
        if (kept is not { Outcome: GitWriteOutcome.Succeeded }) return kept?.Message ?? Failures.BranchNotCreated;
        if (!move.Matches(Repository.Snapshot)) return BranchText.ChooseAgain;
        Wait(null);
        if (await move.Run() is { } moved) statusBar.Status = BranchText.Kept(name, moved.Message);
        return null;
    });

    // The commits are left behind and the move runs as it was asked.
    public async Task<string?> LeaveBehindAsync()
    {
        if (waiting is not { } move || !move.Matches(Repository.Snapshot)) return BranchText.ChooseAgain;
        Wait(null);
        await move.Run();
        return null;
    }

    private void Wait(DetachedMove? move)
    {
        waiting = move;
        OnPropertyChanged(nameof(Asking));
    }

    // The waiting move, with the HEAD that asked, including after the branch that keeps its commits was created.
    private sealed record DetachedMove(string Root, string Head, Func<Task<GitWriteResult?>> Run)
    {
        public bool Matches(RepositorySnapshot? snapshot) => snapshot?.IsSameHead(Root, Head) == true;
    }
}
