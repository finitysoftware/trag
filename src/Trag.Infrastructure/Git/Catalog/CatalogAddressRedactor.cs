using Trag.Domain.Naming;
using Trag.Infrastructure.Git.Diagnostics;

namespace Trag.Infrastructure.Git.Catalog;

// Remote addresses may embed credentials; only the location is kept for display and matching.
internal static class CatalogAddressRedactor
{
    public static string? Redact(string address)
    {
        if (address.Length is 0 or > GitLimits.AddressLength || address.Any(char.IsControl)) return null;
        return GitOutputRedaction.Redact(address);
    }
}
