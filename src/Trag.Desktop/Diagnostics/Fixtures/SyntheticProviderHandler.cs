using System.Net;

namespace Trag.Desktop.Diagnostics.Fixtures;

// Answers the identity requests of the GitHub device flow and GitLab token sign-in without any network; everything else is refused.
internal sealed class SyntheticProviderHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/login/device/code") return Json("{\"device_code\":\"synthetic-device-code\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}");
        if (path == "/login/oauth/access_token")
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("client_secret", StringComparison.Ordinal)) throw new InvalidOperationException("A client secret must not be supplied.");
            return Json(body.Contains("grant_type=refresh_token", StringComparison.Ordinal)
                ? "{\"access_token\":\"synthetic-renewed\",\"refresh_token\":\"synthetic-refresh-next\",\"expires_in\":3600,\"refresh_token_expires_in\":7200,\"token_type\":\"bearer\",\"scope\":\"repo,read:user\"}"
                : "{\"access_token\":\"synthetic-github\",\"refresh_token\":\"synthetic-refresh\",\"expires_in\":1,\"refresh_token_expires_in\":7200,\"token_type\":\"bearer\",\"scope\":\"repo,read:user\"}");
        }
        if (path == "/user") return Json("{\"id\":7,\"login\":\"alice\"}");
        if (request.RequestUri!.AbsolutePath.EndsWith("/api/v4/user", StringComparison.Ordinal))
            return Json(request.Headers.Authorization?.Parameter == "fixture-bob" ? "{\"id\":2,\"username\":\"bob\"}" : "{\"id\":1,\"username\":\"alice\"}");
        throw new InvalidOperationException("The account fixture only handles synthetic identity requests.");
    }

    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
}
