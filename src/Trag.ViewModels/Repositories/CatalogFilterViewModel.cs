using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Trag.ViewModels.Repositories;

// The filter side of the repository picker: the search text, the all / local / one-account choice, and the rows each group shows.
public sealed partial class CatalogFilterViewModel : ObservableObject
{
    private IReadOnlyList<LocalRepositoryRow> localRows = [];
    private IReadOnlyList<AccountGroup> accountGroups = [];

    public void Update(IReadOnlyList<LocalRepositoryRow> localRows, IReadOnlyList<AccountGroup> accountGroups)
    {
        this.localRows = localRows;
        this.accountGroups = accountGroups;
        Apply();
    }

    [ObservableProperty] public partial string CatalogSearch { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAllFilter), nameof(IsLocalFilter), nameof(ShowLocalGroup))]
    public partial CatalogScope Scope { get; private set; }
    [ObservableProperty]
    public partial AccountGroup? FilterGroup { get; private set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasVisibleLocalRows))] public partial IReadOnlyList<LocalRepositoryRow> VisibleLocalRows { get; set; } = [];
    public bool IsAllFilter => Scope == CatalogScope.All;
    public bool IsLocalFilter => Scope == CatalogScope.Local;
    public bool ShowLocalGroup => Scope != CatalogScope.Account;
    public bool HasVisibleLocalRows => VisibleLocalRows.Count != 0;

    partial void OnCatalogSearchChanged(string value) => Apply();

    [RelayCommand]
    public void ShowAllRepositories() => Show(CatalogScope.All);
    [RelayCommand]
    private void ShowLocalRepositories() => Show(CatalogScope.Local);
    // One account's repositories, with the group as the parameter; a group that was never loaded starts loading now.
    [RelayCommand]
    private void ShowAccount(AccountGroup? group)
    {
        if (group is null) return;
        Show(CatalogScope.Account, group);
        if (!group.IsLoaded && !group.IsLoading)
            _ = group.LoadAsync();
    }

    private void Show(CatalogScope scope, AccountGroup? group = null)
    {
        FilterGroup = group;
        Scope = scope;
        Apply();
    }
    // The rows or the filter changed: recompute what the local list and every group show; what the search finds is the
    // items' own rule.
    private void Apply()
    {
        VisibleLocalRows = Scope == CatalogScope.Account ? [] : localRows.Where(row => row.Item.Entry.Matches(CatalogSearch)).ToArray();
        foreach (var group in accountGroups)
        {
            group.IsFiltered = Scope == CatalogScope.Account && ReferenceEquals(FilterGroup, group);
            group.IsVisible = Scope == CatalogScope.All || group.IsFiltered;
            group.VisibleRows = group.IsVisible ? group.Rows.Where(row => row.Repository.Matches(CatalogSearch)).ToArray() : [];
        }
    }
}
