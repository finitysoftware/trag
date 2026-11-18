using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Sync;
using Trag.Domain.Publish;

namespace Trag.ViewModels.Sync;

// The remote side of the current branch as the sidebar and the toolbar show it: the target in the two selectors
// (Selection), what Git says about it (Panel), and the fetch/pull/push commands, the force push of a rewritten branch
// among them. The target, when it is read, what each write needs and the writes are the repository's remotes. A push
// that found the remote's repository missing offers to create it (MissingRepository).
public sealed partial class SyncViewModel : ObservableObject
{
    private readonly RemotesModel remotes;
    private readonly MissingRepositoryModel missing;

    public SyncViewModel(RemotesModel remotes, MissingRepositoryModel missing)
    {
        this.remotes = remotes;
        this.missing = missing;
        var repository = remotes.Repository;
        repository.Requery.Track(FetchCommand, PullCommand, PushCommand, SaveSyncAccountCommand, CreateMissingRepositoryCommand);
        repository.Requery.Suggested += () => OnPropertyChanged(nameof(CanChooseSyncTarget));
        remotes.TargetFollowed += () => Selection = Selection.Following(remotes.Target, remotes.Upstream);
        remotes.ChoiceChanged += () => Selection.Show(remotes.Target, remotes.Upstream);
        remotes.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RemotesModel.Overview)) OnPropertyChanged(nameof(Panel));
            else if (e.PropertyName == nameof(RemotesModel.IsReading)) OnPropertyChanged(nameof(IsSyncBusy));
        };
        missing.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(MissingRepositoryModel.Offer)) return;
            OnPropertyChanged(nameof(MissingRepository));
            OnPropertyChanged(nameof(HasMissingRepository));
            CreateMissingRepositoryCommand.NotifyCanExecuteChanged();
        };
    }

    [ObservableProperty]
    public partial SyncSelection Selection { get; private set; } = SyncSelection.Empty();

    public SyncOverview Panel => remotes.Overview;
    public bool IsSyncBusy => remotes.IsReading;
    public Task SyncCompletion => remotes.Completion;
    public bool CanChooseSyncTarget => remotes.CanChooseTarget;
    // The repository the last push found missing on its host, which Trag can create with the user's account there.
    public MissingRepository? MissingRepository => missing.Offer;
    public bool HasMissingRepository => missing.Offer is not null;
    public Task MissingRepositoryCompletion => missing.Completion;

    // The sidebar's pickers. Every snapshot brings another selection, so whether one is open lives here and outlasts a
    // refresh; the upstream picker's search and rows are the selection's.
    [ObservableProperty] public partial bool IsUpstreamPickerOpen { get; set; }
    // The remote picker, offered with several remotes.
    [ObservableProperty] public partial bool IsRemotePickerOpen { get; set; }

    partial void OnSelectionChanging(SyncSelection value) => Selection.Changed -= Choose;

    partial void OnSelectionChanged(SyncSelection value) => value.Changed += Choose;

    private void Choose() => remotes.Choose(Selection.RemoteName, Selection.SelectedBranch?.Branch);

    [RelayCommand] private void OpenUpstreamPicker() => Selection.Search = "";

    [RelayCommand]
    private void ChooseBranch(SyncBranchRow row)
    {
        IsUpstreamPickerOpen = false;
        Selection.SelectedBranch = row;
    }

    [RelayCommand]
    private void ChooseRemote(SyncRemoteRow row)
    {
        IsRemotePickerOpen = false;
        Selection.RemoteName = row.Name;
    }

    // Enter in the search.
    [RelayCommand]
    private void ChooseFirstBranch()
    {
        if (Selection.Visible.Count != 0) ChooseBranch(Selection.Visible[0]);
    }

    private bool CanSynchronize() => remotes.CanSynchronize;
    private bool CanSaveSyncAccount() => remotes.CanSaveAccount;
    private bool CanPull() => remotes.CanPull;
    private bool CanPush() => remotes.CanPush;
    private bool CanCreateMissingRepository() => missing.CanCreate;

    [RelayCommand(CanExecute = nameof(CanSaveSyncAccount))]
    private Task SaveSyncAccountAsync() => remotes.SaveAccountAsync();

    [RelayCommand(CanExecute = nameof(CanSynchronize))]
    private Task FetchAsync() => remotes.FetchAsync();

    [RelayCommand(CanExecute = nameof(CanPull))]
    private Task PullAsync() => remotes.PullAsync();

    [RelayCommand(CanExecute = nameof(CanPush))]
    private Task PushAsync() => remotes.PushAsync();

    [RelayCommand(CanExecute = nameof(CanCreateMissingRepository))]
    private Task CreateMissingRepositoryAsync() => missing.CreateAsync();
}
