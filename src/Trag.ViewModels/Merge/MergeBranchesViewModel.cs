using System.Windows.Input;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Branches;
using Trag.Application.Live;
using Trag.Application.Merge;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Branches;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Merge;

// The merge dialog: which branch goes into which, what Git says the merge would do, and the merge itself. The branches
// come from the repository's list, the preview and the merge from the repository's merge, every rule and sentence from
// Domain (MergeChoice, MergeSummary); this connects them and follows the repository while the dialog is open.
public sealed partial class MergeBranchesViewModel : ObservableObject
{
    private readonly MergeModel merge;
    private readonly Repository repository;
    private readonly BranchesModel branches;
    private readonly Navigator navigator;

    public MergeBranchesViewModel(MergeModel merge, BranchesModel branches, Navigator navigator)
    {
        this.merge = merge;
        repository = merge.Repository;
        this.branches = branches;
        this.navigator = navigator;
        Draft.PropertyChanged += DraftChanged;
        Preview.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Preview.Value)) Refresh(); };
        merge.Starting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Operation.IsBusy)) return;
            OnPropertyChanged(nameof(IsMerging));
            SwapCommand.NotifyCanExecuteChanged();
            MergeCommand.NotifyCanExecuteChanged();
        };
        branches.List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(branches.List.Value) && Draft.IsOpen && !IsMerging) FollowBranches(); };
        repository.SnapshotApplied += (_, _) => FollowSnapshot();
        repository.Requery.Track(OpenCommand, SwapCommand, MergeCommand);
    }

    public MergeDraft Draft { get; } = new();
    // The stash dialog, offered while uncommitted changes keep the dialog back; the composition hands it over.
    public ICommand? OpenStash { get; set; }
    public Loader<MergePreview> Preview => merge.Preview;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SwapCommand))] public partial MergeSummary Summary { get; private set; } = MergeSummary.Empty;
    // The repository's merge runs the confirmed merge's writes.
    public bool IsMerging => merge.Starting.IsBusy;

    private bool CanOpen() => repository.CanStartOperation && !Draft.IsOpen;
    [RelayCommand(CanExecute = nameof(CanOpen))] private Task OpenAsync() => OpenForAsync(null);

    // The picker's row or the toolbar: the rows are the list's, which follows the repository; a read still in flight
    // arrives through it.
    public async Task OpenForAsync(BranchInfo? from)
    {
        if (!CanOpen()) return;
        Draft.Open(from?.Ref);
        FollowBranches();
        await branches.List.Completion;
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsMerging) Close();
    }

    // Leaves the question for the file status, where the changes can be committed or discarded.
    [RelayCommand]
    private void ReviewChanges()
    {
        Cancel();
        navigator.ShowFileStatus();
    }

    private bool CanSwap() => !IsMerging && Summary.Choice.CanSwap;
    [RelayCommand(CanExecute = nameof(CanSwap))] private void Swap() => Draft.Swap();

    private bool CanMerge() => Draft.IsOpen && !IsMerging && repository.CanWrite && Summary.CanStart;
    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        if (!Draft.IsOpen) return;
        var (choice, preview) = (Draft.Choice, Preview.Value);
        // A merge done, or left for the user in the merge view, closes the dialog: the form does on a write that started.
        await Draft.SubmitAsync(() => merge.StartAsync(choice, preview), Failures.MergeNotStarted);
        // One that did not finish leaves the dialog open over a repository and branches that moved: it looks again.
        if (!Draft.IsOpen) return;
        FollowBranches();
        FollowSnapshot();
    }

    private void DraftChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MergeDraft.From) or nameof(MergeDraft.Into)) ReadPreview();
        if (e.PropertyName is nameof(MergeDraft.IsOpen) or nameof(MergeDraft.Notice)) MergeCommand.NotifyCanExecuteChanged();
        else Refresh();
    }

    // While a merge runs, its own writes move the repository; the preview it started from stays.
    private void FollowSnapshot()
    {
        if (!Draft.IsOpen || IsMerging) return;
        ReadPreview();
        Refresh();
    }

    private void ReadPreview() => merge.ReadPreview(Draft.Choice);

    private void FollowBranches() => Draft.Update(BranchList.Of(branches.List.Value), repository.Snapshot?.Branch);

    private void Refresh()
    {
        Summary = new(Draft.Choice, Preview.Value, repository.Snapshot);
        MergeCommand.NotifyCanExecuteChanged();
    }

    private void Close()
    {
        Draft.Reset();
        merge.ClearPreview();
    }
}
