using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Catalog;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Repositories;

// The clone dialog over a hosted repository: the source, the target folder, whether to open the clone, and what the
// running clone says. The catalog model runs the clone; the clone lands in the local list, which selects it.
public sealed partial class CloneViewModel : ObservableObject
{
    public CatalogState State { get; }
    private readonly LocalRepositoriesViewModel local;
    private readonly Func<LocalRepositoryRow, Task> open;
    private readonly CatalogModel catalog;
    private readonly Func<IShellHost?> host;

    public CloneViewModel(CatalogState state, LocalRepositoriesViewModel local, Func<LocalRepositoryRow, Task> open, CatalogModel catalog, Func<IShellHost?> host)
    {
        State = state;
        this.local = local;
        this.open = open;
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CatalogState.IsCloning))
                return;
            OnPropertyChanged(nameof(CanEditClone));
            ConfirmCloneCommand.NotifyCanExecuteChanged();
        };
        this.catalog = catalog;
        this.host = host;
    }

    [ObservableProperty]
    public partial bool IsCloneDialogOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloneTitle), nameof(CloneSubtitle))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCloneCommand))]
    public partial RemoteRepositoryRow? CloneSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCloneCommand))]
    public partial string CloneTarget { get; set; } = "";

    [ObservableProperty]
    public partial bool CloneOpenAfter { get; set; } = true;

    [ObservableProperty]
    public partial bool CloneSubmodules { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloneStatus))]
    public partial string CloneStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloneDetails))]
    public partial string CloneDetails { get; set; } = "";
    public string CloneTitle => CloneSource?.FullName ?? "";
    public string CloneSubtitle => CloneSource?.StateText ?? "";
    public bool HasCloneStatus => CloneStatus.Length != 0;
    public bool HasCloneDetails => CloneDetails.Length != 0;
    public bool CanEditClone => !State.IsCloning;

    [RelayCommand]
    private void CancelClone()
    {
        if (State.IsCloning)
        {
            catalog.Cloning.Cancel();
            return;
        }

        IsCloneDialogOpen = false;
        CloneSource = null;
        CloneStatus = "";
        CloneDetails = "";
    }
    private bool CanConfirmClone() => CloneSource is not null && !State.IsCloning && CloneTarget.Trim().Length != 0;

    // The catalog runs the clone, which the shell's Cancel reaches too; a clone that joined the list closes the dialog.
    [RelayCommand(CanExecute = nameof(CanConfirmClone))]
    private async Task ConfirmCloneAsync()
    {
        if (CloneSource is not { } source) return;
        var target = CloneTarget.Trim();
        CloneDetails = "";
        CloneStatus = CatalogText.Cloning;
        if (await catalog.CloneAsync(source.Repository, source.Account, target, CloneSubmodules, value => CloneStatus = value) is not { } outcome)
            return;
        if (!outcome.Succeeded)
        {
            (CloneStatus, CloneDetails) = (outcome.Message, outcome.Details);
            return;
        }
        if (Close(outcome, target) is { IsValid: true } row && CloneOpenAfter)
            await open(row);
    }

    // The clone joined the list: the dialog closes on its row, which the list selected; the catalog said so.
    private LocalRepositoryRow? Close(CloneResult outcome, string target)
    {
        var row = outcome.Addition is { } added ? local.Show(added, target) : null;
        IsCloneDialogOpen = false;
        CloneSource = null;
        CloneStatus = "";
        return row;
    }

    [RelayCommand]
    private async Task PickCloneFolderAsync()
    {
        if (host() is not { } window || State.IsChoosingFolder || State.IsCloning || CloneSource is not { } source)
            return;
        await State.PickFolderAsync(window.PickFolderAsync, pick =>
        {
            if (pick.Failed) CloneStatus = CatalogText.PickerFailed;
            else if (pick.Path is { } folder) CloneTarget = CloneTargets.Under(folder, source.Repository.CloneUrl, source.Name);
            return Task.CompletedTask;
        });
    }
    // The dialog opens over the row with Domain's proposed folder: next to the most recently opened repository.
    internal void Begin(RemoteRepositoryRow row)
    {
        if (State.IsCloning)
            return;

        CloneSource = row;
        CloneTarget = catalog.Clone.SuggestTarget(row.Repository.CloneUrl, row.Account.UserName, row.Name);
        CloneOpenAfter = true;
        CloneStatus = "";
        CloneDetails = "";
        IsCloneDialogOpen = true;
    }
}
