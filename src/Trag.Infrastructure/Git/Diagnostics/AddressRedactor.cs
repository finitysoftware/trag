using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Git.Diagnostics;

// Never forward embedded credentials, queries or fragments to presentation.
internal static class AddressRedactor
{
    public static string Redact(string address)
    {
        if (address.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri)) return DetailsText.HiddenAddress;
            return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        }
        // Only an scp-like address, [user@]host:path, carries a user: an '@' before its path starts, with the host's ':'
        // after it. A local path, D:\me@work\repo or ../backup@2, keeps its '@'.
        var path = address.IndexOfAny(['/', '\\']);
        var host = path >= 0 ? address[..path] : address;
        var at = host.LastIndexOf('@');
        var safe = at >= 0 && host.IndexOf(':', at) > at ? address[(at + 1)..] : address;
        var query = safe.IndexOfAny(['?', '#']);
        return (query >= 0 ? safe[..query] : safe).Replace('\r', ' ').Replace('\n', ' ');
    }
}
