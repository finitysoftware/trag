namespace Trag.Domain.Catalog;

// Presentation data for a hosted repository; never contains credentials.
public sealed record RemoteRepository(string FullName, string CloneUrl, string WebUrl, string? DefaultBranch, bool? IsPrivate, DateTimeOffset? UpdatedAt)
{
    // The most repositories Trag reads from an account, the most recently active first.
    public const int MaximumListed = 1000;

    // The page a browser may open: plain HTTPS without credentials, on the account's own host. Anything else is refused.
    public Uri? SafeWebAddress(Accounts.AccountInfo account) =>
        Accounts.RemoteAddress.HttpsWithoutCredentials(WebUrl) is { } uri
        && string.Equals(uri.IdnHost, account.TransportOrigin.IdnHost, StringComparison.OrdinalIgnoreCase) ? uri : null;

    public string Name => FullName.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } parts ? parts[^1] : FullName;

    // A search typed in the picker finds the repository by its full name, owner included, ignoring case and the spaces
    // around it.
    public bool Matches(string search) => FullName.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase);
}
