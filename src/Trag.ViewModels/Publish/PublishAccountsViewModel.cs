using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Publish;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.ViewModels.Publish;

// The accounts a repository can be published from, and the owners of the chosen one, as the dialog shows them: the
// provider, the accounts it lists and the chosen one, and the account choices for a remote address. The two reads are
// the repository's publish accounts; the dialog decides when this feature may be edited.
public sealed partial class PublishAccountsViewModel : ObservableObject
{
    private readonly PublishAccountsModel accounts;
    private string remoteUrl = "";
    private PublishOwnerList? followed;

    public PublishAccountsViewModel(PublishAccountsModel accounts)
    {
        this.accounts = accounts;
        All.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(All.Value)) FollowAccounts();
            else FollowReads();
        };
        Owners.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Owners.Value)) FollowOwners();
            else FollowReads();
        };
    }

    public Loader<IReadOnlyList<AccountInfo>> All => accounts.All;
    public Loader<PublishOwnerList> Owners => accounts.Owners;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsGitHub), nameof(IsGitLab))]
    public partial AccountProvider Provider { get; private set; }
    [ObservableProperty] public partial IReadOnlyList<AccountInfo> Visible { get; private set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreateRepository))] public partial AccountInfo? SelectedAccount { get; set; }
    [ObservableProperty] public partial PublishUrlAccountList UrlAccounts { get; private set; } = PublishUrlAccountList.Create("", []);

    public bool IsGitHub => Provider == AccountProvider.GitHub;
    public bool IsGitLab => Provider == AccountProvider.GitLab;
    public bool HasAccounts => All.Value.Count != 0;
    public bool HasGitHubAccounts => PublishAccounts.Has(All.Value, AccountProvider.GitHub);
    public bool HasGitLabAccounts => PublishAccounts.Has(All.Value, AccountProvider.GitLab);
    public string Message => accounts.Message;
    public bool IsBusy => accounts.IsBusy;
    public bool CanCreateRepository => accounts.CanCreate(SelectedAccount);
    public string EmptyMessage => PublishText.NoAccounts;
    // The account a publish of this kind uses: the chosen one creates a repository, the one matched to the address
    // authenticates it; and the owner chosen for a new repository.
    public string? AccountFor(PublishKind kind) => kind == PublishKind.CreateRepository ? SelectedAccount!.Id : UrlAccounts.AccountId;
    public string? OwnerId => Owners.Value.Selected?.OrganizationId;

    // The chosen account decides whose organizations to list; its own name is offered while they load.
    partial void OnSelectedAccountChanged(AccountInfo? value) => _ = accounts.ReadOwnersAsync(value);

    public void ChooseProvider(AccountProvider provider)
    {
        if (Provider == provider) return;
        Provider = provider;
        ApplyProvider();
    }

    // The returned task includes the owner read of the account chosen from the arriving list.
    public async Task LoadAsync()
    {
        Clear();
        await accounts.ReadAccountsAsync();
        await Owners.Completion;
    }

    public void RefreshUrlAccounts(string url)
    {
        remoteUrl = url;
        UrlAccounts = PublishUrlAccountList.Create(url, All.Value, UrlAccounts.Selected?.Id);
    }

    public void CancelLoads() => accounts.Cancel();

    public void Clear()
    {
        accounts.Clear();
        Visible = [];
        SelectedAccount = null;
        UrlAccounts = PublishUrlAccountList.Create("", []);
    }

    // New accounts: GitHub first unless only GitLab is there, the first account of the provider, and the URL match.
    private void FollowAccounts()
    {
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasGitHubAccounts));
        OnPropertyChanged(nameof(HasGitLabAccounts));
        Provider = PublishAccounts.DefaultProvider(All.Value);
        ApplyProvider();
        RefreshUrlAccounts(remoteUrl);
    }

    private void ApplyProvider()
    {
        Visible = AccountInfo.OfProvider(All.Value, Provider);
        if (SelectedAccount is not null && Visible.Contains(SelectedAccount)) return;
        SelectedAccount = Visible.FirstOrDefault();
    }

    // The owner chosen in the new list is part of the create precondition.
    private void FollowOwners()
    {
        if (followed is not null) followed.PropertyChanged -= OwnerSelectionChanged;
        followed = Owners.Value;
        followed.PropertyChanged += OwnerSelectionChanged;
        OnPropertyChanged(nameof(CanCreateRepository));
    }

    private void OwnerSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PublishOwnerList.Selected)) OnPropertyChanged(nameof(CanCreateRepository));
    }

    private void FollowReads()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(CanCreateRepository));
    }
}
