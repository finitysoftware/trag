using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Domain.Accounts;
using Trag.Domain.Publish;

namespace Trag.Application.Publish;

// The owners Domain lists for an account (PublishOwners), with the dialog's selection: the account itself until the user picks an organization.
public sealed partial class PublishOwnerList : ObservableObject
{
    public IReadOnlyList<PublishOwner> Items { get; }
    public bool HasChoice => Items.Count > 1;

    [ObservableProperty]
    public partial PublishOwner? Selected { get; set; }

    private PublishOwnerList(IReadOnlyList<PublishOwner> items)
    {
        Items = items;
        Selected = items.FirstOrDefault();
    }

    public static PublishOwnerList ForAccount(AccountInfo? account) => new(PublishOwners.ForAccount(account));

    public PublishOwnerList WithOrganizations(IEnumerable<PublishOwner> owners) => Items.Count == 0 ? this : new(PublishOwners.With(Items, owners));
}
