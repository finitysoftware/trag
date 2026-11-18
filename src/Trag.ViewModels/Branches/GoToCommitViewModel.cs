using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Branches;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Domain.Branches;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Branches;

// Double-click in History opens a dialog: a switch to the branch whose tip the commit is, otherwise a branch there or a detached look.
// With uncommitted changes the dialog opens blocked, even for that branch, and offers Stash and File status, like the merge
// dialog; during a merge or rebase it offers that view. It follows the result. When the repository's branches wait for a
// decision about the commits a detached HEAD would leave behind, the keep dialog asks it.
public sealed partial class GoToCommitViewModel : ObservableObject
{
    private readonly Repository repository;
    private readonly BranchesModel branches;
    private readonly HistoryModel history;
    private readonly Navigator navigator;

    public GoToCommitViewModel(BranchesModel branches, HistoryModel history, Navigator navigator)
    {
        repository = branches.Repository;
        this.branches = branches;
        this.history = history;
        this.navigator = navigator;
        Target = new(() => branches.List.Value);
        Keep = new(() => branches.List.Value);
        // A dialog's field or the branch list (which decides what names are free) changed: the dialog commands ask again.
        Target.PropertyChanged += (_, _) => repository.Requery.Invalidate();
        Keep.PropertyChanged += (_, _) => repository.Requery.Invalidate();
        branches.List.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(branches.List.Value)) return;
            Target.Revalidate(); Keep.Revalidate();
            repository.Requery.Invalidate();
        };
        repository.Requery.Track(CreateBranchAtCommitCommand, DetachAtCommitCommand, SwitchToTipCommand, KeepCommitsCommand, DiscardCommitsCommand);
        branches.Leaving.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Operation.IsBusy)) return;
            OnPropertyChanged(nameof(IsLeavingDetached));
            KeepCommitsCommand.NotifyCanExecuteChanged();
            DiscardCommitsCommand.NotifyCanExecuteChanged();
        };
        branches.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(BranchesModel.Asking)) FollowAsking(); };
        repository.SnapshotApplied += (_, _) => RefreshDetachedState();
    }

    // Target is the go-to dialog: a branch there, or a detached look. Keep asks for a branch for what a detached HEAD would leave behind.
    public BranchNameDraft Target { get; }
    public BranchNameDraft Keep { get; }
    // The repository's branches keep the commits, then leave them.
    public bool IsLeavingDetached => branches.Leaving.IsBusy;

    public Repository Repository => repository;
    // The stash dialog, offered while uncommitted changes keep the dialog back; the composition hands it over.
    public ICommand? OpenStash { get; set; }
    public string? CurrentHeadId => repository.Snapshot?.Head;
    public bool IsDetachedHead => repository.Snapshot is { IsDetachedAtCommit: true };
    private DetachedHead Detached => new(CurrentHeadId ?? "", repository.Snapshot?.DetachedCommits ?? 0);
    public string DetachedCardText => Detached.CardText;
    public string KeepCommitsTitle => Detached.KeepTitle;
    // The open go-to dialog: the commit, the branch whose tip it is, and what keeps the move back.
    [ObservableProperty] public partial GoToSummary Dialog { get; private set; } = GoToSummary.Empty;

    // The snapshot changed: the detached card, the keep dialog title and an open go-to dialog follow it.
    private void RefreshDetachedState()
    {
        OnPropertyChanged(nameof(IsDetachedHead));
        OnPropertyChanged(nameof(DetachedCardText));
        OnPropertyChanged(nameof(KeepCommitsTitle));
        OnPropertyChanged(nameof(CurrentHeadId));
        if (Target.IsOpen) Dialog = Dialog with { Snapshot = repository.Snapshot };
    }

    [RelayCommand] private Task GoToSelectedCommitAsync() => history.Selected is { } commit ? GoToCommitAsync(commit) : Task.CompletedTask;
    // The repository's branches go there, or say why not, or ask; the question opens the dialog. An open dialog asks
    // nothing more.
    private async Task GoToCommitAsync(HistoryCommit commit)
    {
        if (Target.IsOpen || Keep.IsOpen || !branches.CanGoTo) return;
        if (await branches.GoToAsync(commit) is not { } question) return;
        Dialog = new(commit, question.Tip, repository.Snapshot);
        Target.Open(question.SuggestedName);
    }
    [RelayCommand]
    private void CancelGoToCommit()
    {
        Target.Close();
        Dialog = GoToSummary.Empty;
    }
    // Review in File status and the operation's view leave the dialog for what blocks it.
    [RelayCommand]
    private void ReviewChanges()
    {
        CancelGoToCommit();
        navigator.ShowFileStatus();
    }
    [RelayCommand]
    private void OpenOperationView()
    {
        CancelGoToCommit();
        navigator.ShowOpenRepository();
    }
    private bool CanSwitchToTip() => Target.IsOpen && Dialog.IsTip && repository.CanWrite && !Dialog.IsBlocked;
    [RelayCommand(CanExecute = nameof(CanSwitchToTip))]
    private Task SwitchToTipAsync()
    {
        if (Dialog.Tip is not { } tip || !CanSwitchToTip()) return Task.CompletedTask;
        CancelGoToCommit();
        return branches.SwitchToAsync(tip);
    }
    private bool CanCreateBranchAtCommit() => Target.IsOpen && !Dialog.IsTip && repository.CanWrite && Target.IsValid && !Dialog.IsBlocked;
    [RelayCommand(CanExecute = nameof(CanCreateBranchAtCommit))]
    private Task CreateBranchAtCommitAsync() => Dialog.Commit is { } commit && CanCreateBranchAtCommit()
        ? MoveToCommitAsync(BranchAction.Create, new(Target.Name.Trim(), commit.Id, true), Failures.BranchNotCreated) : Task.CompletedTask;
    private bool CanDetachAtCommit() => Target.IsOpen && !Dialog.IsTip && repository.CanWrite && !Dialog.IsBlocked;
    [RelayCommand(CanExecute = nameof(CanDetachAtCommit))]
    private Task DetachAtCommitAsync() => Dialog.Commit is { } commit && CanDetachAtCommit()
        ? MoveToCommitAsync(BranchAction.Checkout, new(commit.Id), Failures.CommitNotCheckedOut) : Task.CompletedTask;
    // Both moves out of the dialog run the same way: past a detached HEAD with commits of its own; the dialog closes on success.
    private Task MoveToCommitAsync(BranchAction action, BranchOperation operation, string failure) =>
        branches.LeaveDetachedThenAsync(() => Target.SubmitAsync(() => branches.WriteAsync(action, operation), failure));

    // A move waits for the decision: the dialog asks it, with a name for the branch that would keep the commits; the
    // decision closes it.
    private void FollowAsking()
    {
        if (branches.Asking is not null) Keep.Open(branches.KeptName);
        else Keep.Close();
    }
    [RelayCommand]
    private void CancelKeepCommits()
    {
        if (IsLeavingDetached) return;
        branches.StayDetached();
    }
    private bool CanKeepCommits() => Keep.IsOpen && !IsLeavingDetached && repository.CanWrite && Keep.IsValid;
    [RelayCommand(CanExecute = nameof(CanKeepCommits))]
    private async Task KeepCommitsAsync()
    {
        if (!Keep.IsOpen) return;
        Keep.Notice = "";
        if (await branches.KeepThenLeaveAsync(Keep.Name.Trim()) is { } notice) Keep.Notice = notice;
    }
    private bool CanDiscardCommits() => Keep.IsOpen && !IsLeavingDetached && repository.CanWrite;
    [RelayCommand(CanExecute = nameof(CanDiscardCommits))]
    private async Task DiscardCommitsAsync()
    {
        if (CanDiscardCommits()) Keep.Notice = await branches.LeaveBehindAsync() ?? "";
    }
}
