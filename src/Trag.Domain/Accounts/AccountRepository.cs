namespace Trag.Domain.Accounts;

// A repository a remote's HTTPS address names under an account's own name on that account's host, as
// https://github.com/<user>/<name>: the one Trag can create from the account when the host has none there.
public sealed record AccountRepository(AccountInfo Account, string Name)
{
    public static AccountRepository? At(string address, IReadOnlyList<AccountInfo> accounts) => RemoteAddress.Https(address) is { } endpoint
        ? accounts.Where(account => AccountEndpoints.Matches(endpoint, account)).Select(account => Under(endpoint, account)).FirstOrDefault(found => found is not null)
        : null;

    // The owner and the name after the account's own path on its host.
    private static AccountRepository? Under(Uri endpoint, AccountInfo account) =>
        RemoteAddress.ProjectPath(endpoint.AbsolutePath[AccountEndpoints.BasePath(account).Length..]).Split('/') is [var owner, var name]
        && owner.Equals(account.UserName, StringComparison.OrdinalIgnoreCase) ? new(account, name) : null;
}
