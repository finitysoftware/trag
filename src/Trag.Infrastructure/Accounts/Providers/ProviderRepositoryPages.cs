using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Accounts.Providers;

// Paging over a provider's repository list: at most ten pages of a hundred, the most Trag lists, and a page that is not
// full is the last one.
internal sealed class ProviderRepositoryPages(ProviderHttpClient http)
{
    // The size every provider's query must ask for, because a shorter page is what ends the loop below. GitHubProvider
    // and GitLabProvider build their per_page from this, so the two cannot drift apart.
    public const int PageSize = 100;
    private const int MaxPages = RemoteRepository.MaximumListed / PageSize;

    public async Task<RemoteRepositoryList> ReadAsync(Uri api, string token, Func<int, string> relative,
        Func<ProviderRepository, RemoteRepository?> map, CancellationToken ct)
    {
        var result = new List<RemoteRepository>();
        var listed = 0;
        var full = true;
        for (var page = 1; full && page <= MaxPages; page++)
        {
            using var request = ProviderHttpClient.Request(HttpMethod.Get, new Uri(api, relative(page)), token);
            var items = await http.JsonAsync(request, ProviderJsonContext.Default.ProviderRepositoryArray,
                ProviderText.RepositoriesUnreadable, ct, 4 * 1024 * 1024).ConfigureAwait(false) ?? [];
            listed += items.Length;
            foreach (var item in items) if (map(item) is { } mapped) result.Add(mapped);
            full = items.Length >= PageSize;
        }
        return listed != 0 && result.Count == 0 ? throw new AccountException(ProviderText.NoHttpsCloneAddress) : new(result, full);
    }
}
