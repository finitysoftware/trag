using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Accounts;

namespace Trag.Infrastructure.Accounts.Providers;

// The one way this layer talks to a hosting provider: no redirects, no cookies, a deadline of its own, a bounded body,
// and a message for every status the user can act on. Every buffer is cleared before it is released.
internal sealed class ProviderHttpClient : IDisposable
{
    private readonly HttpClient http;

    public ProviderHttpClient(HttpMessageHandler? handler)
    {
        // Never forward Authorization to redirects; use the normal OS trust chain.
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Trag/0.1");
        http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    // A request made as the account: the token is checked here, once, before it goes on the wire.
    public static HttpRequestMessage Request(HttpMethod method, Uri url, string token)
    {
        TokenValidation.Validate(token);
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<T?> JsonAsync<T>(HttpRequestMessage request, JsonTypeInfo<T> type, string unreadable,
        CancellationToken ct, int limit = 65_536, Func<HttpStatusCode, string?>? explain = null)
    {
        var bytes = await SendAsync(request, ct, limit, explain).ConfigureAwait(false);
        try { return JsonSerializer.Deserialize(bytes, type); }
        catch (JsonException) { throw new AccountException(unreadable); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private async Task<byte[]> SendAsync(HttpRequestMessage request, CancellationToken ct, int limit, Func<HttpStatusCode, string?>? explain)
    {
        var caller = ct;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        ct = deadline.Token;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new AccountException(IsRateLimited(response) ? ProviderText.RateLimited
                    : explain?.Invoke(response.StatusCode) ?? FailureMessage(response.StatusCode));
            return await ReadBodyAsync(response.Content, limit, ct).ConfigureAwait(false);
        }
        // A connection that drops while the body arrives is the same unreachable provider as one that drops before.
        catch (Exception error) when (error is HttpRequestException or HttpIOException) { throw new AccountException(ProviderText.Unreachable); }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested) { throw new AccountException(ProviderText.TimedOut); }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpContent content, int limit, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            int count;
            while ((count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                if (memory.Length + count > limit) throw new AccountException(ProviderText.ResponseTooLarge);
                memory.Write(buffer, 0, count);
            }
            return memory.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(memory.GetBuffer());
        }
    }

    // GitHub answers a used-up rate limit with 403 as often as with 429, and says so in its headers.
    private static bool IsRateLimited(HttpResponseMessage response) => response.StatusCode == HttpStatusCode.TooManyRequests
        || response.StatusCode == HttpStatusCode.Forbidden && (response.Headers.RetryAfter is not null
            || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0");

    private static string FailureMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => ProviderText.TokenRevoked,
        HttpStatusCode.Forbidden => ProviderText.AccessDenied,
        _ => ProviderText.Failed
    };

    public void Dispose() => http.Dispose();
}
