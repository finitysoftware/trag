using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Catalog;
using Trag.Application.Live;

namespace Trag.ViewModels.Repositories;

// State shared by the picker, its local list and its clone dialog: the catalog's activity and message, which are the
// model's, and the picker's own folder choice and selection.
public sealed partial class CatalogState : ObservableObject
{
    private readonly CatalogModel catalog;

    public CatalogState(CatalogModel catalog)
    {
        this.catalog = catalog;
        catalog.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(CatalogModel.IsBusy) or nameof(CatalogModel.Message)) OnPropertyChanged(e.PropertyName); };
        catalog.Cloning.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Operation.IsBusy)) OnPropertyChanged(nameof(IsCloning)); };
    }

    public bool IsBusy => catalog.IsBusy;
    public bool IsCloning => catalog.Cloning.IsBusy;
    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }
    // The window's own answers, a folder that went or a page that is not safe, are said there too.
    public string Message { get => catalog.Message; set => catalog.Message = value; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedLocal), nameof(HasSelection))]
    public partial LocalRepositoryRow? SelectedLocal { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedRemote), nameof(HasSelection))]
    public partial RemoteRepositoryRow? SelectedRemote { get; set; }
    public bool HasSelectedLocal => SelectedLocal is not null;
    public bool HasSelectedRemote => SelectedRemote is not null;
    public bool HasSelection => HasSelectedLocal || HasSelectedRemote;

    // The folder picker and what the caller does with its answer, with IsChoosingFolder up for both, so nothing else
    // that waits for the picker starts before the folder is used.
    public async Task PickFolderAsync(Func<Task<string?>> pick, Func<FolderPick, Task> then)
    {
        IsChoosingFolder = true;
        try { await then(await FolderPick.AskAsync(pick)); }
        finally { IsChoosingFolder = false; }
    }
}
