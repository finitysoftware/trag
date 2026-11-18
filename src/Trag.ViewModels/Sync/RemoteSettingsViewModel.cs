using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.Application.Sync;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.ViewModels.Sync;

// The Remotes dialog: the repository's remotes, and under them one form at a time, adding a remote (and fetching it, unless
// told not to), changing one or asking before one is removed. The edit form starts from the address the configuration keeps, and only a changed
// address is sent. What keeps a form back and the writes are the repository's remote settings' (RemoteSettingsModel);
// a write Git refuses leaves its form as it was, with the reason.
public sealed partial class RemoteSettingsViewModel : ObservableObject
{
    private readonly RemoteSettingsModel remotes;
    private readonly Repository repository;
    // The address the edit form started from: unchanged, it is not sent.
    private string shownUrl = "";

    public RemoteSettingsViewModel(RemoteSettingsModel remotes)
    {
        this.remotes = remotes;
        repository = remotes.Repository;
        repository.SnapshotApplied += (_, _) => OnPropertyChanged(nameof(Remotes));
        repository.Requery.Track(OpenCommand, AddCommand, SaveCommand, ConfirmRemoveCommand);
    }

    [ObservableProperty] public partial bool IsOpen { get; private set; }
    public IReadOnlyList<RepositoryRemote> Remotes => remotes.Remotes;
    [ObservableProperty] public partial string Message { get; private set; } = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(AddError)), NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewName { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AddError)), NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewUrl { get; set; } = "";
    // On unless unticked: the new remote's branches come in right after it is added.
    [ObservableProperty] public partial bool FetchAfterAdding { get; set; } = true;
    // Said once something is typed.
    public string AddError => NewName.Length == 0 && NewUrl.Length == 0 ? "" : remotes.AddError(NewName, NewUrl);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsAdding), nameof(EditError)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Editing { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditError)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditName { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EditError)), NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string EditUrl { get; set; } = "";
    public bool IsEditing => Editing is not null;
    public string EditError => Editing is { } name ? remotes.EditError(name, EditName, ChangedUrl) : "";
    private string? ChangedUrl => EditUrl == shownUrl ? null : EditUrl;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRemoving), nameof(IsAdding), nameof(Removal)), NotifyCanExecuteChangedFor(nameof(ConfirmRemoveCommand))]
    public partial string? Removing { get; private set; }
    public bool IsRemoving => Removing is not null;
    public string Removal => Removing is { } name ? remotes.Removal(name) : "";
    public bool IsAdding => Editing is null && Removing is null;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open()
    {
        Cancel();
        NewName = NewUrl = "";
        IsOpen = true;
    }
    private bool CanOpen() => repository.HasRepository;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Cancel() => (Editing, Removing, Message) = (null, null, "");

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task AddAsync()
    {
        var name = NewName;
        if (!await WriteAsync(remotes.AddAsync(name, NewUrl))) return;
        NewName = NewUrl = "";
        if (FetchAfterAdding) await WriteAsync(remotes.FetchAsync(name));
    }
    private bool CanAdd() => repository.CanWrite && NewName.Length != 0 && NewUrl.Length != 0 && AddError.Length == 0;

    [RelayCommand]
    private async Task BeginEditAsync(RepositoryRemote? remote)
    {
        if (remote is null) return;
        Cancel();
        shownUrl = await remotes.ReadUrlAsync(remote);
        (EditName, EditUrl, Editing) = (remote.Name, shownUrl, remote.Name);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Editing is { } name && await WriteAsync(remotes.EditAsync(name, EditName, ChangedUrl))) Editing = null;
    }
    private bool CanSave() => repository.CanWrite && Editing is { } name && remotes.CanEdit(name, EditName, ChangedUrl);

    [RelayCommand]
    private void BeginRemove(RepositoryRemote? remote)
    {
        if (remote is null) return;
        Cancel();
        Removing = remote.Name;
    }

    [RelayCommand(CanExecute = nameof(CanConfirmRemove))]
    private async Task ConfirmRemoveAsync()
    {
        if (Removing is { } name && await WriteAsync(remotes.RemoveAsync(name))) Removing = null;
    }
    private bool CanConfirmRemove() => repository.CanWrite && Removing is not null;

    // True when Git did what was asked; otherwise the form stays, with the reason.
    private async Task<bool> WriteAsync(Task<GitWriteResult?> write)
    {
        var result = await write;
        Message = result is { Outcome: GitWriteOutcome.Succeeded } ? "" : result?.Message ?? "";
        return result?.Outcome == GitWriteOutcome.Succeeded;
    }
}
