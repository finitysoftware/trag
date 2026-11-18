using System.Windows.Input;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Branches;
using Trag.Application.Live;
using Trag.Application.Rebase;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.ViewModels.Branches;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Rebase;

// The rebase side of the merge dialog: which branch the current one goes onto, what Git says the rebase would do, and the
// rebase itself. The branches and the switch come from the repository's branches, the preview and the rebase from the
// repository's rebase, every rule and sentence from Domain (RebaseChoice, RebaseSummary); this connects them and follows
// the repository while the dialog is open.
public sealed partial class RebaseBranchViewModel : ObservableObject
{
    private readonly RebaseModel rebase;
    private readonly Repository repository;
    private readonly BranchesModel branches;
    private readonly Navigator navigator;

    public RebaseBranchViewModel(RebaseModel rebase, BranchesModel branches, Navigator navigator)
    {
        this.rebase = rebase;
        repository = rebase.Repository;
        this.branches = branches;
        this.navigator = navigator;
        Draft.PropertyChanged += DraftChanged;
        Preview.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Preview.Value)) Refresh(); };
        rebase.Starting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Operation.IsBusy)) return;
            OnPropertyChanged(nameof(IsRebasing));
            RebaseCommand.NotifyCanExecuteChanged();
            RebaseOtherWayCommand.NotifyCanExecuteChanged();
        };
        branches.List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(branches.List.Value) && Draft.IsOpen && !IsRebasing) FollowBranches(); };
        repository.SnapshotApplied += (_, _) => FollowSnapshot();
        repository.Requery.Track(RebaseCommand, RebaseOtherWayCommand);
    }

    public RebaseDraft Draft { get; } = new();
    // The stash dialog, offered while uncommitted changes keep the dialog back; the composition hands it over.
    public ICommand? OpenStash { get; set; }
    public Loader<RebasePreview> Preview => rebase.Preview;
    [ObservableProperty] public partial RebaseSummary Summary { get; private set; } = RebaseSummary.Empty;
    // The repository's rebase runs the rebase and the switch that turns one around.
    public bool IsRebasing => rebase.Starting.IsBusy;

    // The merge side of the dialog hands its branch over; the rows are the list's, which follows the repository, and a
    // read still in flight arrives through it.
    public async Task OpenForAsync(BranchInfo? onto)
    {
        if (!repository.CanStartOperation || Draft.IsOpen) return;
        Draft.Open(onto?.Ref);
        FollowBranches();
        await branches.List.Completion;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRebasing) Close();
    }

    // Leaves the question for the file status, where the changes can be committed or discarded.
    [RelayCommand]
    private void ReviewChanges()
    {
        Cancel();
        navigator.ShowFileStatus();
    }

    private bool CanRebase() => Draft.IsOpen && !IsRebasing && repository.CanWrite && Summary.CanStart;
    [RelayCommand(CanExecute = nameof(CanRebase))]
    private async Task RebaseAsync()
    {
        if (!Draft.IsOpen || Draft.Onto is not { } onto) return;
        var ontoHead = Preview.Value.OntoHead;
        // A finished rebase, or one left for the user in the rebase view, closes the dialog: the form does on a write that started.
        await Draft.SubmitAsync(() => rebase.StartAsync(onto.Branch, ontoHead), Failures.RebaseNotStarted);
        FollowAgain();
    }

    // Rebasing the branch others build on onto an older one is almost always the wrong way round: Trag switches to that
    // branch and the dialog offers to rebase it onto the one that was checked out, for the user to confirm.
    private bool CanRebaseOtherWay() => Draft.IsOpen && !IsRebasing && repository.CanWrite && Summary is { CanRebaseOtherWay: true, IsBlocked: false };
    [RelayCommand(CanExecute = nameof(CanRebaseOtherWay))]
    private async Task RebaseOtherWayAsync()
    {
        if (!CanRebaseOtherWay() || Draft is not { Onto: { } onto, Branch: { } branch }) return;
        if (await rebase.TurnAroundAsync(onto.Branch) is { } notice) Draft.Notice = notice;
        else Draft.Retarget(branch.Branch.Ref);
        FollowAgain();
    }

    // A rebase that did not finish, or the switch that turned one around, leaves the dialog open over a repository and
    // branches that moved: it looks again.
    private void FollowAgain()
    {
        if (!Draft.IsOpen) return;
        FollowBranches();
        FollowSnapshot();
    }

    private void FollowBranches() => Draft.Update(BranchList.Of(branches.List.Value));

    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RebaseDraft.Onto)) ReadPreview();
        if (e.PropertyName is nameof(RebaseDraft.IsOpen) or nameof(RebaseDraft.Notice)) RebaseCommand.NotifyCanExecuteChanged();
        else Refresh();
    }

    // While a rebase runs, its own writes move the repository; the preview it started from stays.
    private void FollowSnapshot()
    {
        if (!Draft.IsOpen || IsRebasing) return;
        ReadPreview();
        Refresh();
    }

    private void ReadPreview() => rebase.ReadPreview(Draft.Choice);

    private void Refresh()
    {
        Summary = new(Draft.Choice, Preview.Value, repository.Snapshot);
        RebaseCommand.NotifyCanExecuteChanged();
        RebaseOtherWayCommand.NotifyCanExecuteChanged();
    }

    public void Close()
    {
        Draft.Reset();
        rebase.ClearPreview();
    }
}
