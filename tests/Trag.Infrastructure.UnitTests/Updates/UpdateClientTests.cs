using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Trag.Infrastructure.Updates;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Updates;

// The update service over HTTPS: what goes out (the version and the platform, nothing else), and that anything but the
// agreed answer, a service that is not there yet included, is no answer.
public sealed class UpdateClientTests
{
    [Fact]
    public async Task WhenAsked_ShouldSendTheVersionAndThePlatformToTheUpdateService()
    {
        HttpRequestMessage? sent = null;
        var client = new UpdateClient(new Handler(request => { sent = request; return Json("""{"updateAvailable":false,"latestVersion":"1.0.0"}"""); }));

        await client.CheckAsync("1.0.0");

        Assert.Equal("https://downloads.finitysoftware.io/api/trag/update?version=1.0.0&platform=" + RuntimeInformation.RuntimeIdentifier, sent!.RequestUri!.AbsoluteUri);
        Assert.Equal(HttpMethod.Get, sent.Method); Assert.Equal("Trag/1.0.0", sent.Headers.UserAgent.ToString());
        Assert.Equal("application/json", sent.Headers.Accept.ToString());
    }

    [Fact]
    public async Task WhenTheServiceOffersANewerRelease_ShouldAnswerWithItsDownload()
    {
        var client = new UpdateClient(new Handler(_ =>
            Json("""{"updateAvailable":true,"latestVersion":"1.1.0","downloadUrl":"https://downloads.finitysoftware.io/trag/1.1.0"}""")));

        var offer = await client.CheckAsync("1.0.0");

        Assert.Equal((true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0"), (offer!.IsAvailable, offer.LatestVersion, offer.Download!.AbsoluteUri));
    }

    [Fact]
    public async Task WhenTheServiceSaysTragIsTheNewest_ShouldAnswerThatNoneIsOut()
    {
        var client = new UpdateClient(new Handler(_ => Json("""{"updateAvailable":false,"latestVersion":"1.0.0"}""")));
        var offer = await client.CheckAsync("1.0.0");
        Assert.False(offer!.IsAvailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, """{"updateAvailable":false}""")]
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.OK, "<html>not the service</html>")]
    [InlineData(HttpStatusCode.OK, "{}")]
    [InlineData(HttpStatusCode.OK, """{"updateAvailable":true,"latestVersion":"1.1.0","downloadUrl":"https://example.com/trag.exe"}""")]
    public async Task WhenTheServiceDoesNotGiveTheAgreedAnswer_ShouldBeNoAnswer(HttpStatusCode status, string body)
    {
        var client = new UpdateClient(new Handler(_ => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
        Assert.Null(await client.CheckAsync("1.0.0"));
    }

    // No network, a refused connection, or no answer within the deadline (HttpClient reports it as a canceled task).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheServiceCannotBeReached_ShouldBeNoAnswer(bool timesOut)
    {
        var client = new UpdateClient(new Handler(_ => throw (timesOut ? new TaskCanceledException("deadline") : new HttpRequestException("unreachable"))));
        Assert.Null(await client.CheckAsync("1.0.0"));
    }

    [Fact]
    public async Task WhenTheAnswerIsLargerThanAnyAnswerCouldBe_ShouldBeNoAnswer()
    {
        var client = new UpdateClient(new Handler(_ => Json("""{"updateAvailable":false,"latestVersion":"1.0.0","padding":""" + "\"" + new string('x', 64 * 1024) + "\"}")));
        Assert.Null(await client.CheckAsync("1.0.0"));
    }

    // The handler Trag sends with, behind a server on this machine: a redirect is the answer, and it is no answer.
    [Fact]
    public async Task WhenTheServiceRedirects_ShouldNotFollowItAndBeNoAnswer()
    {
        using var server = new AnsweringServer(path => path == "/api/trag/update"
            ? Raw("302 Found\r\nLocation: /moved", "")
            : Raw("200 OK\r\nContent-Type: application/json", """{"updateAvailable":false,"latestVersion":"1.0.0"}"""));

        Assert.Null(await OnThisMachine(server.Port).CheckAsync("1.0.0"));

        Assert.Equal(["/api/trag/update"], server.Paths);
    }

    // The handler Trag sends with keeps no cookies: what the service sets with one answer does not go back with the next
    // check.
    [Fact]
    public async Task WhenTheServiceSetsACookie_ShouldNotSendItWithTheNextCheck()
    {
        using var server = new AnsweringServer(_ =>
            Raw("200 OK\r\nContent-Type: application/json\r\nSet-Cookie: id=1; Path=/", """{"updateAvailable":false,"latestVersion":"1.0.0"}"""));
        var client = OnThisMachine(server.Port);

        Assert.NotNull(await client.CheckAsync("1.0.0")); Assert.NotNull(await client.CheckAsync("1.0.0"));

        Assert.DoesNotContain(server.Headers.Last(), line => line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase));
    }

    // A server that takes the connection and says nothing, as a stalled proxy does: the check gives up at its deadline.
    [Fact]
    public async Task WhenTheServiceTakesTheConnectionButNeverAnswers_ShouldGiveUpAtTheDeadline()
    {
        using var server = new SilentServer();

        var check = OnThisMachine(new Uri(server.Url("")).Port).CheckAsync("1.0.0");

        await server.Connected;
        Assert.Null(await check.WaitAsync(TimeSpan.FromSeconds(20)));
    }

    private static string Raw(string status, string body) => "HTTP/1.1 " + status + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body;

    private static UpdateClient OnThisMachine(int port) => new(new ToThisMachine(port) { InnerHandler = UpdateClient.Handler() });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(answer(request));
    }

    // The request as Trag makes it, sent to the loopback address instead of Finity Software's site.
    private sealed class ToThisMachine(int port) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri = new UriBuilder(request.RequestUri!) { Scheme = Uri.UriSchemeHttp, Host = "127.0.0.1", Port = port }.Uri;
            return base.SendAsync(request, ct);
        }
    }

    // An HTTP server on the loopback address that answers each request with the raw response for its path, and keeps the
    // paths it was asked for and the header lines of each request.
    private sealed class AnsweringServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly Func<string, string> answer;

        public AnsweringServer(Func<string, string> answer)
        {
            this.answer = answer;
            listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        public ConcurrentQueue<string> Paths { get; } = [];
        public ConcurrentQueue<string[]> Headers { get; } = [];

        private async Task AcceptAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync(stop.Token);
                    var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                    var path = (await reader.ReadLineAsync(stop.Token) ?? "").Split(' ') is [_, var target, ..] ? target.Split('?')[0] : "";
                    var headers = new List<string>();
                    while (await reader.ReadLineAsync(stop.Token) is { Length: > 0 } line) headers.Add(line);
                    Paths.Enqueue(path); Headers.Enqueue([.. headers]);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(answer(path)), stop.Token);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException or IOException) { }
        }

        public void Dispose()
        {
            stop.Cancel();
            listener.Stop();
            stop.Dispose();
        }
    }
}
