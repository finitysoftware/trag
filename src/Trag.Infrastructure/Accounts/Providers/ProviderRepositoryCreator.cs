using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;

namespace Trag.Infrastructure.Accounts.Providers;

// Creates an empty repository (no README, no initial commit) so the first push carries the local history. The name is
// checked before the provider sees it; the provider decides where the request goes and how it refuses permission. The
// other refusals are worded the same for both sites, so they live here.
internal sealed class ProviderRepositoryCreator(ProviderHttpClient http)
{
    public async Task<RemoteRepository> CreateAsync(Uri api, string token, string name, string site,
        Func<string, (string Relative, RepositoryCreation Body)> build, string forbidden,
        Func<ProviderRepository, RemoteRepository?> map, CancellationToken ct)
    {
        name = name.Trim();
        if (!PublishNames.IsValid(name)) throw new AccountException(PublishNames.Help);
        var (relative, body) = build(name);
        using var request = ProviderHttpClient.Request(HttpMethod.Post, new Uri(api, relative), token);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, ProviderJsonContext.Default.RepositoryCreation));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var item = await http.JsonAsync(request, ProviderJsonContext.Default.ProviderRepository,
            ProviderText.CreateUnreadable(site),
            ct, 1024 * 1024, status => Refusal(site, status, forbidden)).ConfigureAwait(false);
        return (item is null ? null : map(item))
            ?? throw new AccountException(ProviderText.AddressUnreadable(site));
    }

    private static string? Refusal(string site, HttpStatusCode status, string forbidden) => status switch
    {
        HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.Conflict =>
            ProviderText.NotAccepted(site),
        HttpStatusCode.Forbidden => forbidden,
        HttpStatusCode.NotFound => ProviderText.OwnerNotFound,
        _ => null
    };
}
