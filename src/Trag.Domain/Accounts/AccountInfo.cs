using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Accounts;

// Safe presentation/persistence model. Credentials never enter this object.
public sealed record AccountInfo(string Id, AccountProvider Provider, string ProviderUserId, string UserName,
    Uri ApiBaseUri, Uri TransportOrigin, bool IsPersistent, DateTimeOffset VerifiedAt, DateTimeOffset? ExpiresAt)
{
    // Accounts as every list shows them: by provider, then by user name ignoring case.
    public static IReadOnlyList<AccountInfo> InDisplayOrder(IEnumerable<AccountInfo> accounts) =>
        accounts.OrderBy(account => account.Provider).ThenBy(account => account.UserName, StringComparer.OrdinalIgnoreCase).ToArray();
    // The accounts of one provider, as the settings and the publish dialog list them under it.
    public static IReadOnlyList<AccountInfo> OfProvider(IReadOnlyList<AccountInfo> accounts, AccountProvider provider) =>
        accounts.Where(account => account.Provider == provider).ToArray();

    public string Display => AccountText.Display(this);
}
