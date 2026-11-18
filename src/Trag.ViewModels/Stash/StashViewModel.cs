using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.History;
using Trag.Application.Live;
using Trag.Application.Stash;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Stash;

// Stash as the screen shows it: the toolbar button's dialog that sets changes aside under a name, the sidebar section
// with the entries as rows, the preview of one as the commit the history's pane shows, and the drop question. The list
// and every write are the repository's stashes (StashesModel).
public sealed partial class StashViewModel : ObservableObject
{
    private readonly StashesModel stashes;
    private readonly Repository repository;
    private readonly HistoryModel history;
    private readonly Navigator navigator;

    public StashViewModel(StashesModel stashes, HistoryModel history, Navigator navigator)
    {
        this.stashes = stashes;
        repository = stashes.Repository;
        this.history = history;
        this.navigator = navigator;
        history.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HistoryModel.PreviewedStash)) FollowPreview(); };
        Drop = new(repository, row => stashes.DropAsync(row.Id), row => StashText.DropQuestion(row.Entry), Failures.StashNotDropped);
        List.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(List.Value)) FollowList(); };
        Draft.PropertyChanged += (_, _) => CreateStashCommand.NotifyCanExecuteChanged();
        repository.SnapshotApplied += (_, _) => { OnPropertyChanged(nameof(HasStashes)); OnPropertyChanged(nameof(StashDefaultHint)); };
        repository.Requery.Track(OpenStashCommand, CreateStashCommand, ApplyCommand, RequestDropCommand);
        repository.Requery.Suggested += () =>
        {
            OnPropertyChanged(nameof(HasStashes));
        };
    }

    // The repository's list, and its entries as the picker shows them.
    public Loader<IReadOnlyList<StashEntry>> List => stashes.List;
    [ObservableProperty] public partial IReadOnlyList<StashRow> Rows { get; private set; } = [];
    public Confirmation<StashRow> Drop { get; }
    public StashDraft Draft { get; } = new();
    public Repository Repository => repository;

    // The stash the history shows in the commit pane, as its row; its Apply and Drop buttons pass it to the row commands.
    public StashRow? SelectedStash => history.PreviewedStash is { } shown ? Rows.FirstOrDefault(row => row.Id == shown.Id) : null;
    // The sidebar flyout; a row action closes it because the result shows elsewhere.
    [ObservableProperty] public partial bool IsPickerOpen { get; set; }

    // The section exists only with a stash; the list itself arrives a moment later.
    public bool HasStashes => stashes.HasStashes;
    public string StashButtonText => StashList.ButtonText(List.Value);
    public string StashCountText => StashList.CountText(List.Value);
    public string StashDefaultHint => StashText.DefaultHint(repository.Snapshot?.Branch);
    public bool IsStashPreview => SelectedStash is not null;
    public string StashPreviewTitle => SelectedStash is { } row ? StashText.PreviewTitle(row.Entry) : "";
    private bool CanStash() => stashes.CanPush;

    private void FollowPreview()
    {
        OnPropertyChanged(nameof(SelectedStash));
        OnPropertyChanged(nameof(IsStashPreview));
        OnPropertyChanged(nameof(StashPreviewTitle));
    }

    // A new list becomes new rows; the question about an entry the list no longer has closes. The history ends the
    // preview of such an entry itself.
    private void FollowList()
    {
        Rows = List.Value.Select(entry => new StashRow(entry, stashes.Time)).ToArray();
        OnPropertyChanged(nameof(StashButtonText));
        OnPropertyChanged(nameof(StashCountText));
        FollowPreview();
        if (Drop.Subject is { } subject && Rows.All(row => row.Id != subject.Id)) Drop.Close();
    }

    [RelayCommand(CanExecute = nameof(CanStash))]
    private void OpenStash()
    {
        if (CanStash()) Draft.Open();
    }
    [RelayCommand] private void CancelStash() => Draft.Close();
    private bool CanCreateStash() => Draft.IsOpen && CanStash() && Draft.IsValid;
    [RelayCommand(CanExecute = nameof(CanCreateStash))]
    private async Task CreateStashAsync()
    {
        if (!Draft.IsOpen) return;
        await Draft.SubmitAsync(() => stashes.PushAsync(Draft.ToOperation()), Failures.ChangesNotStashed);
    }
    [RelayCommand] private Task OpenStashPickerAsync() => stashes.List.ReloadAsync();

    [RelayCommand]
    private Task PreviewAsync(StashRow row)
    {
        IsPickerOpen = false;
        navigator.Section = WorkspaceSection.History;
        return history.PreviewAsync(row.Entry);
    }

    private bool CanAct(StashRow? row) => row is not null && repository.CanWrite;

    // Apply brings the changes back and keeps the entry; the file status view is where they show up.
    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ApplyAsync(StashRow? row)
    {
        if (row is null) return;
        IsPickerOpen = false;
        var result = await stashes.ApplyAsync(row.Id);
        if (result is { Started: true }) navigator.Section = WorkspaceSection.FileStatus;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void RequestDrop(StashRow? row)
    {
        if (row is null) return;
        IsPickerOpen = false;
        Drop.Request(row);
    }
}
