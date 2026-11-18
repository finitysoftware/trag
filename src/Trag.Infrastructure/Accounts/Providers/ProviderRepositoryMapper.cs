using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Naming;

namespace Trag.Infrastructure.Accounts.Providers;

// What Trag keeps from a repository a provider lists, and what makes one unusable. A repository without a clean HTTPS
// clone address is left out entirely; a web address that cannot be used falls back to the clone address.
internal static class ProviderRepositoryMapper
{
    public static RemoteRepository? Map(string? fullName, string? cloneUrl, string? webUrl, string? defaultBranch, bool? isPrivate, DateTimeOffset? changed)
    {
        if (!PlainNames.IsValid(fullName, 300) || RemoteAddress.HttpsWithoutCredentials(cloneUrl) is not { } clone) return null;
        var web = RemoteAddress.HttpsWithoutCredentials(webUrl) ?? WithoutGitSuffix(clone);
        return new(fullName, clone.AbsoluteUri, web.AbsoluteUri, PlainNames.IsValid(defaultBranch, 512) ? defaultBranch : null, isPrivate, changed);
    }

    private static Uri WithoutGitSuffix(Uri clone) =>
        new(clone.AbsoluteUri.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? clone.AbsoluteUri[..^4] : clone.AbsoluteUri);
}
