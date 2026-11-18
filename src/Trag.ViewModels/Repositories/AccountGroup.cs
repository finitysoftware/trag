using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trag.Application.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Presentation;
using Trag.ViewModels.Blocks;

namespace Trag.ViewModels.Repositories;

// One account's hosted repositories in the picker: the header, the rows over the account's hosted list, and the
// filter's view of them. The catalog follows Rows and SelectedRow to match clones, filter and keep one selection across
// the groups.
public sealed partial class AccountGroup : ObservableObject
{
    private readonly HostedRepositories hosted;

    public AccountGroup(HostedRepositories hosted, TimeProvider clock)
    {
        this.hosted = hosted;
        // The rows and the loading state are the list's; the message is its message, or what the list itself says.
        hosted.List.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(hosted.List.Value))
            {
                Rows = hosted.List.Value.Select(repository => new RemoteRepositoryRow(repository, Account, clock)).ToArray();
                foreach (var name in new[] { nameof(Rows), nameof(CountText), nameof(HeaderText) }) OnPropertyChanged(name);
            }
            else if (e.PropertyName == nameof(hosted.List.IsBusy))
            {
                OnPropertyChanged(nameof(IsLoading));
                LoadCommand.NotifyCanExecuteChanged();
            }
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        };
        hosted.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HostedRepositories.IsLoaded)) return;
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(LoadText));
        };
    }

    internal HostedRepositories Hosted => hosted;
    public AccountInfo Account => hosted.Account;
    public IReadOnlyList<RemoteRepositoryRow> Rows { get; private set; } = [];
    public bool IsLoading => hosted.List.IsBusy;
    public bool IsLoaded => hosted.IsLoaded;
    public string ProviderName => AccountText.ProviderName(Account.Provider);
    public string IconKind => IconKinds.Account(Account.Provider);
    public string Title => CatalogText.GroupTitle(ProviderName, Account.UserName);
    [ObservableProperty] public partial bool IsFiltered { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasVisibleRows))] public partial IReadOnlyList<RemoteRepositoryRow> VisibleRows { get; set; } = [];
    public bool HasVisibleRows => VisibleRows.Count != 0;
    public Fold Fold { get; } = new();
    [ObservableProperty] public partial RemoteRepositoryRow? SelectedRow { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
    public string CountText => CatalogText.Count(Rows.Count);
    public string HeaderText => CatalogText.GroupHeader(Title, CountText);
    public string Message => CatalogText.GroupMessage(IsLoading, hosted.List.Message, IsLoaded, Rows.Count, hosted.ReachedLimit);
    public bool HasMessage => Message.Length != 0;
    public string LoadText => CatalogText.LoadRepositories(IsLoaded);
    private bool CanLoad() => !IsLoading;
    // Reads the account's repositories; the catalog matches them to local clones when they arrive.
    [RelayCommand(CanExecute = nameof(CanLoad))]
    public Task LoadAsync() => hosted.LoadAsync();
}
