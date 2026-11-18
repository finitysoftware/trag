namespace Trag.Domain.Updates;

// The company that makes Trag: its site, and the addresses Trag trusts as its own (HTTPS on its domain or a subdomain).
public static class FinitySoftware
{
    private const string Domain = "finitysoftware.io";

    public static Uri Website { get; } = new UriBuilder(Uri.UriSchemeHttps, Domain).Uri;

    public static bool Owns(Uri url) => url.Scheme == Uri.UriSchemeHttps
        && (url.Host.Equals(Domain, StringComparison.OrdinalIgnoreCase) || url.Host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase));
}
