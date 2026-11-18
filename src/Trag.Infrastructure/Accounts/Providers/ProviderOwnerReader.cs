using System.Net;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;

namespace Trag.Infrastructure.Accounts.Providers;

// The organizations or groups an account may publish into, a page of a hundred at a time until a page that is not full,
// at most two hundred entries; an owner the provider describes in a way Trag cannot use is skipped rather than shown.
internal sealed class ProviderOwnerReader(ProviderHttpClient http)
{
    private const int OwnerLimit = 200;

    public async Task<IReadOnlyList<PublishOwner>> ReadAsync(Uri api, string token, Func<int, string> relative,
        Func<HttpStatusCode, string?> explain, Func<ProviderOwner, PublishOwner?> map, CancellationToken ct)
    {
        var result = new List<PublishOwner>();
        for (var page = 1; result.Count < OwnerLimit; page++)
        {
            using var request = ProviderHttpClient.Request(HttpMethod.Get, new Uri(api, relative(page)), token);
            var items = await http.JsonAsync(request, ProviderJsonContext.Default.ProviderOwnerArray,
                ProviderText.OwnersUnreadable, ct, 1024 * 1024, explain).ConfigureAwait(false) ?? [];
            result.AddRange(items.Select(map).OfType<PublishOwner>().Take(OwnerLimit - result.Count));
            if (items.Length < ProviderRepositoryPages.PageSize) break;
        }
        return result;
    }

    // An owner Trag can show and send back: an id that is there, and a display name that is one. What each provider
    // puts in the two fields is the provider's business.
    public static PublishOwner? Owner(string? id, string? display) =>
        string.IsNullOrWhiteSpace(id) || !PlainNames.IsValid(display, 300)
            ? null : new(id, display);
}
