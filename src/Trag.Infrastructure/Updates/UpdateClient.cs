using System.Runtime.InteropServices;
using System.Text.Json;
using Trag.Application.Ports.Updates;
using Trag.Domain.Updates;

namespace Trag.Infrastructure.Updates;

// Finity Software's update service over HTTPS. Only the version and the platform go out, with no redirects and no
// cookies, a deadline and a bounded body. An error status (a service that is not there yet included), no connection,
// no answer in time (HttpClient cancels the request), or a body that is not the agreed answer is no answer.
public sealed class UpdateClient(HttpMessageHandler? handler = null) : IUpdateChecker
{
    private static readonly Uri Service = new("https://downloads.finitysoftware.io/api/trag/update");
    private readonly HttpClient http = new(handler ?? Handler())
    {
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 16 * 1024
    };

    internal static HttpMessageHandler Handler() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };

    public async Task<UpdateOffer?> CheckAsync(string version)
    {
        var query = "?version=" + Uri.EscapeDataString(version) + "&platform=" + Uri.EscapeDataString(RuntimeInformation.RuntimeIdentifier);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Service, query));
        request.Headers.UserAgent.TryParseAdd("Trag/" + version);
        request.Headers.Accept.Add(new("application/json"));
        try
        {
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize(body, UpdateJsonContext.Default.UpdateResponse) is { UpdateAvailable: { } available } answer
                ? UpdateOffer.From(available, answer.LatestVersion, answer.DownloadUrl)
                : null;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
        {
            return null;
        }
    }
}
