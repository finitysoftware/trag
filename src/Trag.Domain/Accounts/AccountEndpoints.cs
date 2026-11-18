namespace Trag.Domain.Accounts;

// The address rules an account's token may be offered under. A clean HTTPS address carries nothing but the host, the
// port and a path: no credentials, query or fragment that could send the token somewhere else. Matching an account to
// an endpoint, binding a broker to an origin and accepting a GitLab instance address all apply this one rule.
public static class AccountEndpoints
{
    public static bool IsClean(Uri address) => address.IsAbsoluteUri && address.Scheme == "https"
        && address.UserInfo.Length == 0 && address.Query.Length == 0 && address.Fragment.Length == 0;

    public static bool Matches(Uri endpoint, AccountInfo account) => IsClean(endpoint)
        && account.TransportOrigin.IdnHost.Equals(endpoint.IdnHost, StringComparison.OrdinalIgnoreCase)
        && account.TransportOrigin.Port == endpoint.Port
        && (endpoint.AbsolutePath == BasePath(account) || endpoint.AbsolutePath.StartsWith(BasePath(account) + "/", StringComparison.Ordinal));

    // The path the account's host serves it under, without a trailing slash.
    public static string BasePath(AccountInfo account) => account.TransportOrigin.AbsolutePath.TrimEnd('/');
}
