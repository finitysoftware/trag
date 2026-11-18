using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Accounts;

namespace Trag.ViewModels.Publish;

// The account choices for a remote address (Domain AccountChoice.ForUrl), with the dialog's selection: the earlier choice
// while it is still offered, else the only matching account, else Git's own credentials.
public sealed partial class PublishUrlAccountList : ObservableObject
{
    public IReadOnlyList<AccountChoice> Items { get; }
    public bool HasChoices => Items.Count != 0;
    public string? AccountId => AccountChoice.TragAccount(Selected?.Id);

    [ObservableProperty]
    public partial AccountChoice? Selected { get; set; }

    private PublishUrlAccountList(IReadOnlyList<AccountChoice> items, string? previousId)
    {
        Items = items;
        Selected = AccountChoice.Default(items, previousId);
    }

    public static PublishUrlAccountList Create(string url, IReadOnlyList<AccountInfo> accounts, string? previousId = null) =>
        new(AccountChoice.ForUrl(url, accounts), previousId);
}
