using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Branches;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Branches;

// The branch picker: the repository's branches as rows with a filter, switching, the create and delete dialogs, and the
// row action that hands a branch to the merge dialog. The list and every write are the repository's (BranchesModel).
public sealed partial class BranchesViewModel : ObservableObject
{
    private readonly BranchesModel branches;
    private readonly Repository repository;
    private readonly HistoryModel history;

    public BranchesViewModel(BranchesModel branches, HistoryModel history)
    {
        this.branches = branches;
        repository = branches.Repository;
        this.history = history;
        Delete = new(repository, row => branches.DeleteAsync(row.Branch, confirmed: true), row => row.DeleteSubtitle, Failures.BranchNotDeleted);
        List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(List.Value)) FollowList(); };
        Draft.PropertyChanged += (_, _) => CreateBranchCommand.NotifyCanExecuteChanged();
        Rename.PropertyChanged += (_, _) => RenameBranchCommand.NotifyCanExecuteChanged();
        repository.Requery.Track(OpenCreateBranchCommand, CreateBranchFromCommitCommand, CreateBranchCommand, RenameBranchCommand);
    }

    // A row's merge action opens the merge dialog with that branch to merge from; the composition links it.
    public Func<BranchInfo, Task> Merge { get; set; } = _ => Task.CompletedTask;
    // The window's services, for a branch's page on its provider; the composition links them.
    public Func<IShellHost?> Host { get; set; } = () => null;
    // The repository's list, and its rows as the picker shows them.
    public Loader<BranchCatalog> List => branches.List;
    public BranchList Rows => BranchList.Of(List.Value);
    public Confirmation<BranchRow> Delete { get; }
    public BranchDraft Draft { get; } = new();
    public BranchRenameDraft Rename { get; } = new();

    // The list under the current filter.
    [ObservableProperty] public partial BranchList Visible { get; private set; } = BranchList.Empty;
    [ObservableProperty] public partial string BranchSearch { get; set; } = "";
    // The sidebar flyout; a row action closes it because the result shows elsewhere.
    [ObservableProperty] public partial bool IsPickerOpen { get; set; }

    partial void OnBranchSearchChanged(string value) => Visible = Rows.Filter(value);

    // A new list: the filter applies to it, an open create dialog offers its start points, and an open rename dialog checks
    // the new name against it.
    private void FollowList()
    {
        OnPropertyChanged(nameof(Rows));
        Visible = Rows.Filter(BranchSearch);
        if (Draft.IsOpen) Draft.Update(List.Value, repository.Snapshot?.Branch);
        Rename.Update(List.Value);
    }

    // The list follows the repository; opening the picker waits for a read still in flight, or reads one still owed.
    [RelayCommand]
    private Task OpenBranchPickerAsync()
    {
        BranchSearch = "";
        return List.ShowAsync();
    }

    [RelayCommand]
    private Task SwitchToFirstMatchAsync()
    {
        return Visible.Catalog.FirstMatch is { } branch ? SwitchToBranchAsync(Visible.Row(branch)) : Task.CompletedTask;
    }

    private bool CanOpenCreateBranch() => repository.HasRepository && !repository.IsWriting;
    [RelayCommand(CanExecute = nameof(CanOpenCreateBranch))] private Task OpenCreateBranchAsync() => OpenCreateBranchAsync(null);
    private bool CanCreateBranchFromCommit() => history.CanActOnSelected;
    [RelayCommand(CanExecute = nameof(CanCreateBranchFromCommit))]
    private Task CreateBranchFromCommitAsync() => history.Selected is { } commit
        ? OpenCreateBranchAsync(new StartPointOption(StartPoint.AtCommit(commit))) : Task.CompletedTask;
    [RelayCommand] private void CancelCreateBranch() => Draft.Close();

    private bool CanCreateBranch() => Draft.IsOpen && repository.CanWrite && Draft.IsValid;
    [RelayCommand(CanExecute = nameof(CanCreateBranch))]
    private async Task CreateBranchAsync()
    {
        if (!Draft.IsOpen) return;
        var operation = Draft.ToOperation();
        await branches.CreateAsync(operation, () => Draft.SubmitAsync(() => branches.WriteAsync(BranchAction.Create, operation), Failures.BranchNotCreated));
    }

    private async Task OpenCreateBranchAsync(StartPointOption? start)
    {
        IsPickerOpen = false;
        Draft.Open(List.Value, repository.Snapshot?.Branch, start);
        await List.Completion;
    }

    // The repository's branches delete the row's branch, or say that the user has to confirm; then the question opens.
    [RelayCommand]
    private async Task RequestDeleteAsync(BranchRow row)
    {
        IsPickerOpen = false;
        if (row.IsCurrent || Delete.IsRunning) return;
        if (await branches.RequestDeleteAsync(row.Branch)) Delete.Request(row);
    }

    private static Uri? PageOf(BranchRow? row) => row?.Request?.Address;
    private bool CanCreatePullRequest(BranchRow? row) => PageOf(row) is not null;
    [RelayCommand(CanExecute = nameof(CanCreatePullRequest))]
    private void CreatePullRequest(BranchRow? row)
    {
        IsPickerOpen = false;
        Host()?.OpenBrowser(PageOf(row)!);
    }

    // A local branch's menu renames it in a dialog that opens with its name.
    [RelayCommand]
    private void RequestRename(BranchRow? row)
    {
        IsPickerOpen = false;
        Rename.Open(row!, List.Value);
    }

    private bool CanRenameBranch() => Rename.IsOpen && repository.CanWrite && Rename.IsValid;
    [RelayCommand(CanExecute = nameof(CanRenameBranch))]
    private Task RenameBranchAsync() => Rename.SubmitAsync(() => branches.RenameAsync(Rename.Branch!.Branch, Rename.Name), Failures.BranchNotRenamed);
    [RelayCommand] private void CancelRename() => Rename.Close();

    [RelayCommand]
    private Task RequestMergeAsync(BranchRow row)
    {
        IsPickerOpen = false;
        return row.CanMerge ? Merge(row.Branch) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task SwitchToBranchAsync(BranchRow row)
    {
        IsPickerOpen = false;
        // A remote row's local counterpart may be the current branch, which the LOCAL list leaves out; the repository's
        // branches say so then.
        return row.IsCurrent ? Task.CompletedTask : branches.SwitchToAsync(row.Branch);
    }
}
