using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Catalog;
using Trag.Application.Live;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Blocks;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Repositories;

// The local side of the repository picker: rows over the catalog's local items, and the questions to add, initialize and
// remove; what each came to is the catalog's message. Selection and activity are shared through CatalogState; a link
// check that changes an item re-asks the commands that read it.
public sealed partial class LocalRepositoriesViewModel : ObservableObject
{
    public CatalogState State { get; }
    private readonly CatalogModel catalog;
    private readonly LocalRepositoriesModel local;
    private readonly Func<IShellHost?> host;
    private readonly Requery requery;
    private Dictionary<LocalRepository, LocalRepositoryRow> rows = [];
    public event Action? RowsChanged;

    public LocalRepositoriesViewModel(CatalogState state, CatalogModel catalog, Func<IShellHost?> host, Requery requery)
    {
        State = state;
        this.catalog = catalog;
        local = catalog.Local;
        this.host = host;
        this.requery = requery;
        Removal = new(RemoveAsync, row => CatalogText.RemovalQuestion(row.Name), () => !State.IsBusy);
        Initialization = new(InitializeAsync, CatalogText.InitializeQuestion, () => !State.IsBusy);
        local.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocalRepositoriesModel.Items)) Project();
        };
        local.Checked += requery.Invalidate;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LocalCountText), nameof(LocalHeaderText))]
    public partial IReadOnlyList<LocalRepositoryRow> LocalRows { get; set; } = [];
    // Two questions in the details pane: remove the selected entry from the list; initialize a folder that turned out not to be a repository.
    public Confirmation<LocalRepositoryRow> Removal { get; }
    public Fold Fold { get; } = new();
    public Confirmation<string> Initialization { get; }
    public string LocalCountText => CatalogText.Count(LocalRows.Count);
    public string LocalHeaderText => CatalogText.LocalHeader(LocalRows.Count);

    partial void OnLocalRowsChanged(IReadOnlyList<LocalRepositoryRow> value) => RowsChanged?.Invoke();

    private bool CanAddRepository() => !State.IsChoosingFolder && !State.IsBusy && !State.IsCloning;
    [RelayCommand(CanExecute = nameof(CanAddRepository), AllowConcurrentExecutions = true)]
    private async Task AddRepositoryAsync()
    {
        if (!CanAddRepository() || host() is not { } window)
            return;
        await State.PickFolderAsync(window.PickFolderAsync, async pick =>
        {
            if (pick.Failed) State.Message = CatalogText.AddFailed;
            else if (pick.Path is { } path) Show(await catalog.AddAsync(path), path);
        });
    }
    // Git initializes the folder, then it is added like any other; the question stays open, with why, while that fails.
    private async Task<string?> InitializeAsync(string path)
    {
        var added = await catalog.InitializeAsync(path);
        if (added.Outcome is CatalogAdditionOutcome.Failed or CatalogAdditionOutcome.Canceled)
            return State.Message;
        Show(added, path);
        return null;
    }
    // The question over a row: the row menu passes its row, the toolbar the selected one.
    private bool CanRequestRemove(LocalRepositoryRow? row) => (row ?? State.SelectedLocal) is not null && !State.IsBusy;
    [RelayCommand(CanExecute = nameof(CanRequestRemove))]
    private void RequestRemoveRepository(LocalRepositoryRow? row)
    {
        if ((row ?? State.SelectedLocal) is not { } subject)
            return;
        if (!ReferenceEquals(State.SelectedLocal, subject))
            State.SelectedLocal = subject;
        Removal.Request(subject);
    }
    // The entry leaves the list at once (CatalogModel); the selection lets go of its row first.
    private async Task<string?> RemoveAsync(LocalRepositoryRow row)
    {
        if (ReferenceEquals(State.SelectedLocal, row))
            State.SelectedLocal = null;
        await catalog.RemoveAsync(row.Item);
        return null;
    }

    internal LocalRepositoryRow RowOf(LocalRepository item) => rows[item];
    // The items were rebuilt: a row per item, kept with it, so a selection survives a reorder.
    private void Project()
    {
        var existing = rows;
        rows = local.Items.ToDictionary(item => item, item => existing.TryGetValue(item, out var row) ? row : new LocalRepositoryRow(item, local.Time));
        LocalRows = local.Items.Select(item => rows[item]).ToArray();
        if (State.SelectedLocal is { } selected && !LocalRows.Contains(selected))
            State.SelectedLocal = null;
        requery.Invalidate();
    }
    // The list's answer to an addition: the row added or found, selected, or the question for a folder that is not a
    // repository.
    internal LocalRepositoryRow? Show(CatalogAddition added, string path)
    {
        if (added.Outcome == CatalogAdditionOutcome.NotRepository) Initialization.Request(path);
        if (local.Accept(added) is not { } item) return null;
        var row = RowOf(item);
        Initialization.Close();
        State.SelectedLocal = row;
        return row;
    }
}
