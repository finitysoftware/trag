using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Catalog;
using Trag.Application.Live;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.Repositories;

// Repository picker: the list of repositories added to Trag, hosted repositories per account, and clone.
// CatalogState owns shared selection and activity; this class connects Local, Filter and Clone, shows what the catalog
// model reads as it arrives, and holds the row actions, which take the row as their parameter.
public sealed partial class RepositoryCatalogViewModel : ObservableObject
{
    private readonly CatalogModel catalog;
    private readonly RepositorySession session;
    private readonly Navigator navigator;
    private bool applyingCatalogSelection;
    public LocalRepositoriesViewModel Local { get; }
    public CatalogFilterViewModel Filter { get; }
    public CloneViewModel Clone { get; }
    public CatalogState State { get; }
    // The window's services while the window shows this shell; the composition links them.
    public Func<IShellHost?> Host { get; set; } = () => null;
    public Task CatalogCompletion => catalog.Read.Completion;

    public RepositoryCatalogViewModel(CatalogModel catalog, RepositorySession session, Navigator navigator)
    {
        this.catalog = catalog;
        this.session = session;
        this.navigator = navigator;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RepositorySession.Current)) OnPropertyChanged(nameof(Repository)); };
        State = new(catalog);
        Local = new(State, catalog, () => Host(), session.Requery);
        Filter = new();
        Clone = new(State, Local, OpenEntryAsync, catalog, () => Host());
        // The catalog's read applies as it goes, so the page fills while it runs.
        catalog.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CatalogModel.Accounts)) RebuildAccountGroups(); };
        catalog.LinksChecked += MatchRemoteRows;
        Local.RowsChanged += HandleGroupLoaded;
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CatalogState.SelectedLocal))
                SelectLocal(State.SelectedLocal);
            if (e.PropertyName is nameof(CatalogState.SelectedLocal) or nameof(CatalogState.SelectedRemote))
                Local.Removal.Close();
            if (e.PropertyName is nameof(CatalogState.SelectedLocal) or nameof(CatalogState.SelectedRemote)
                or nameof(CatalogState.IsBusy) or nameof(CatalogState.IsCloning) or nameof(CatalogState.IsChoosingFolder))
                session.Requery.Invalidate();
        };
        // The list page loads when it shows and when it is chosen again.
        navigator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Navigator.Section)) return;
            BackCommand.NotifyCanExecuteChanged();
            if (navigator.Section == WorkspaceSection.Repositories) Load();
        };
        navigator.Reselected += section => { if (section == WorkspaceSection.Repositories) Load(); };
        session.Requery.Track(OpenSelectedRepositoryCommand, RefreshCatalogCommand, BackCommand,
            Local.RequestRemoveRepositoryCommand, Local.Removal.ConfirmCommand, Local.AddRepositoryCommand, Local.Initialization.ConfirmCommand);
    }

    // The repository shown now, which Back returns to.
    public Repository Repository => session.Current;

    // Edited accounts may have been added or removed: a shown list reads again unless it is reading already.
    // CatalogCompletion is the read to wait for.
    public void FollowAccounts()
    {
        if (navigator.Section == WorkspaceSection.Repositories && RefreshCatalogCommand.CanExecute(null)) RefreshCatalogCommand.Execute(null);
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAccounts))] public partial IReadOnlyList<AccountGroup> AccountGroups { get; set; } = [];
    public string AllCountText => CatalogText.Count(Local.LocalRows.Count + AccountGroups.Sum(group => group.Rows.Count));
    public bool HasAccounts => AccountGroups.Count != 0;

    partial void OnAccountGroupsChanged(IReadOnlyList<AccountGroup> value) => OnPropertyChanged(nameof(AllCountText));
    private bool CanOpenSelected() => catalog.CanOpen && (State.SelectedLocal?.IsValid == true || State.SelectedRemote?.Local?.IsValid == true);
    [RelayCommand(CanExecute = nameof(CanOpenSelected))]
    private Task OpenSelectedRepositoryAsync() =>
        (State.SelectedLocal ?? State.SelectedRemote?.Local) is { } row ? OpenEntryAsync(row) : Task.CompletedTask;
    private bool CanRefreshCatalog() => !State.IsBusy;
    [RelayCommand(CanExecute = nameof(CanRefreshCatalog))]
    private Task RefreshCatalogAsync() => Load();
    // Back to the open repository: its merge or rebase view while one is in progress, else the file status.
    private bool CanBack() => session.Current.HasRepository && navigator.Section == WorkspaceSection.Repositories;
    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => navigator.ShowOpenRepository();
    [RelayCommand] private void RequestSettings() => Host()?.ShowSettings();
    // Reads the list and the accounts, then checks every local link; the returned task is also CatalogCompletion.
    public Task Load() => catalog.ReadAsync();

    // The row actions; the list templates and the details pane pass the row. The host says whether the folder is still there.
    [RelayCommand]
    private void Reveal(LocalRepositoryRow? row)
    {
        if (row is null) return;
        if (Host()?.Reveal(row.Path) == false)
            State.Message = CatalogText.FolderGone;
    }
    [RelayCommand]
    private void OpenWeb(RemoteRepositoryRow? row)
    {
        if (row is null) return;
        if (row.Repository.SafeWebAddress(row.Account) is { } uri)
            Host()?.OpenBrowser(uri);
        else
            State.Message = CatalogText.AddressUnsafe;
    }
    [RelayCommand]
    private void BeginClone(RemoteRepositoryRow? row)
    {
        if (row is not null) Clone.Begin(row);
    }
    // Double-click on a hosted repository: its clone opens when there is one, otherwise the clone dialog does.
    [RelayCommand]
    private Task ActivateRemoteAsync(RemoteRepositoryRow? row)
    {
        if (row?.Local is { } local) return OpenEntryAsync(local);
        BeginClone(row);
        return Task.CompletedTask;
    }
    // One selection across the local list and every account group: choosing on one side clears the other.
    private void SelectLocal(LocalRepositoryRow? value)
    {
        if (applyingCatalogSelection)
            return;
        if (value is null)
            return;
        applyingCatalogSelection = true;
        foreach (var group in AccountGroups)
            group.SelectedRow = null;
        applyingCatalogSelection = false;
        State.SelectedRemote = null;
    }
    private void SelectRemote(AccountGroup group, RemoteRepositoryRow? value)
    {
        if (applyingCatalogSelection)
            return;
        if (value is null)
        {
            if (State.SelectedRemote?.Account.Id == group.Account.Id)
                State.SelectedRemote = null;
            return;
        }
        applyingCatalogSelection = true;
        State.SelectedLocal = null;
        foreach (var other in AccountGroups)
        if (!ReferenceEquals(other, group))
            other.SelectedRow = null;
        applyingCatalogSelection = false;
        State.SelectedRemote = value;
    }
    // A listed repository that did not open says why on the list, which shows then.
    [RelayCommand]
    private async Task OpenEntryAsync(LocalRepositoryRow? row)
    {
        if (row is not null && !await catalog.OpenAsync(row.Item)) navigator.Section = WorkspaceSection.Repositories;
    }
    // A group finished loading its rows: match them to local clones and show them under the current filter.
    private void HandleGroupLoaded()
    {
        MatchRemoteRows();
        Filter.Update(Local.LocalRows, AccountGroups);
        OnPropertyChanged(nameof(AllCountText));
    }

    // The accounts were read: a group per account, kept with it; a group whose account went leaves the filter and the
    // selection.
    private void RebuildAccountGroups()
    {
        var existing = AccountGroups.ToDictionary(group => group.Hosted);
        AccountGroups = catalog.Accounts
            .Select(hosted => existing.TryGetValue(hosted, out var group) ? group : Follow(new AccountGroup(hosted, catalog.Local.Time))).ToArray();
        if (Filter.FilterGroup is { } filter && !AccountGroups.Contains(filter))
            Filter.ShowAllRepositories();
        if (State.SelectedRemote is { } remote && AccountGroups.All(group => group.Account.Id != remote.Account.Id))
            State.SelectedRemote = null;
        Filter.Update(Local.LocalRows, AccountGroups);
    }
    // A group's rows arrived or its selection moved: the catalog matches, filters and unifies the selection.
    private AccountGroup Follow(AccountGroup group)
    {
        group.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountGroup.Rows)) HandleGroupLoaded();
            else if (e.PropertyName == nameof(AccountGroup.SelectedRow)) SelectRemote(group, group.SelectedRow);
        };
        return group;
    }
    // A hosted repository whose clone address matches a local origin shows that clone; whether it can open follows.
    private void MatchRemoteRows()
    {
        var clones = catalog.Local.Clones();
        foreach (var group in AccountGroups)
            foreach (var row in group.Rows)
                row.Local = clones.Find(row.Repository.CloneUrl) is { } item ? Local.RowOf(item) : null;
        session.Requery.Invalidate();
    }
}
