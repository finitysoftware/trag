using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Trag.TestSupport;

// A folder of repositories served by Git's smart HTTP protocol over TLS on the loopback address, behind Basic
// authentication: a request without the one user name and password it accepts is answered 401, and the credentials
// every request carried are kept in order. Git's own http-backend answers each accepted request as CGI, except Git LFS
// batch requests and object downloads, which the server answers itself from the repository's lfs/objects folder. The
// certificate is made in this process and written to CertificateFile, so Git trusts it only where a test points
// http.sslCAInfo at that file; nothing is installed anywhere.
public sealed class HttpsGitServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<string?> credentials = new();
    private readonly X509Certificate2 certificate = CreateCertificate();
    private readonly string root;
    private readonly string accepted;

    public HttpsGitServer(string root, string userName, string password)
    {
        this.root = Path.GetFullPath(root);
        accepted = userName + ":" + password;
        CertificateFile = Path.Combine(this.root, "server-certificate.pem");
        File.WriteAllText(CertificateFile, certificate.ExportCertificatePem());
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public string CertificateFile { get; }

    // "user:password" as each request sent it, or null for a request without credentials.
    public IReadOnlyList<string?> Credentials => credentials.ToArray();

    public string Url(string path) => "https://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/" + path;

    // Every Git this process starts trusts the certificate until the result is disposed, through settings that join the
    // GIT_CONFIG_COUNT ones the run may already have; certificate verification stays on. Git for Windows' schannel
    // backend, which its installer sets in the system configuration, ignores http.sslCAInfo, so OpenSSL is named there;
    // elsewhere Git has the one backend it was built with, as Ubuntu's GnuTLS, and that one reads http.sslCAInfo.
    public IDisposable TrustedByGit() => new ProcessGitConfig(OperatingSystem.IsWindows()
        ? [("http.sslBackend", "openssl"), ("http.sslCAInfo", CertificateFile)]
        : [("http.sslCAInfo", CertificateFile)]);

    private sealed class ProcessGitConfig : IDisposable
    {
        private readonly Dictionary<string, string?> previous = new();

        public ProcessGitConfig((string Key, string Value)[] settings)
        {
            var count = int.TryParse(Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT"), out var given) ? given : 0;
            Set("GIT_CONFIG_COUNT", (count + settings.Length).ToString());
            foreach (var (key, value) in settings)
            {
                Set("GIT_CONFIG_KEY_" + count, key);
                Set("GIT_CONFIG_VALUE_" + count++, value);
            }
        }

        private void Set(string variable, string value)
        {
            previous[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, value);
        }

        public void Dispose()
        {
            foreach (var (variable, value) in previous) Environment.SetEnvironmentVariable(variable, value);
        }
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
        // Windows' TLS takes a server key only from a key container, which a PKCS#12 import gives it for as long as the
        // certificate lives.
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pkcs12), null);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await listener.AcceptTcpClientAsync(stop.Token));
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    // One request per connection: the answer closes it, and Git opens another for its next request.
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                await using var tls = new SslStream(client.GetStream());
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                var head = (await ReadHeadAsync(tls)).Split("\r\n");
                var headers = head.Skip(1).Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
                var body = new byte[headers.TryGetValue("Content-Length", out var length) ? int.Parse(length) : 0];
                await tls.ReadExactlyAsync(body, stop.Token);
                var sent = headers.TryGetValue("Authorization", out var authorization) && authorization.StartsWith("Basic ", StringComparison.Ordinal)
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..])) : null;
                credentials.Enqueue(sent);
                if (sent != accepted)
                {
                    // Credentials it refuses come back in the plain text Git prints as the remote's message: the header as it
                    // was sent and, for Basic, the user name and password it holds.
                    var echo = authorization is null ? [] : Encoding.UTF8.GetBytes("Refused " + authorization + (sent is null ? "" : " for " + sent) + "\n");
                    await tls.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"trag\"\r\nContent-Type: text/plain\r\nContent-Length: "
                        + echo.Length + "\r\nConnection: close\r\n\r\n"), stop.Token);
                    await tls.WriteAsync(echo, stop.Token);
                    return;
                }
                var (method, target) = head[0].Split(' ') is [var verb, var path, ..] ? (verb, path) : ("", "");
                await tls.WriteAsync(target.Split("/info/lfs/objects/") is [var repository, var lfs] ? Lfs(repository, lfs, body)
                    : await BackendAsync(method, target, headers, body), stop.Token);
            }
            catch (Exception error) when (error is IOException or AuthenticationException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    // The request line and headers, up to the empty line, read a byte at a time so the body stays in the stream.
    private async Task<string> ReadHeadAsync(Stream stream)
    {
        var head = new List<byte>();
        var one = new byte[1];
        while (head.Count < 4 || head[^4] != '\r' || head[^3] != '\n' || head[^2] != '\r' || head[^1] != '\n')
        {
            if (await stream.ReadAsync(one, stop.Token) == 0) throw new IOException("The request ended before its headers did.");
            head.Add(one[0]);
        }
        return Encoding.ASCII.GetString(head.ToArray(), 0, head.Count - 4);
    }

    // Git LFS's batch API, for downloads only, and the content each answer points to, at an address behind the same
    // authentication: both read the lfs/objects folder of the repository, where Git LFS keeps a bare repository's content.
    private byte[] Lfs(string repository, string request, byte[] body)
    {
        var objects = Path.Combine(root, Uri.UnescapeDataString(repository).TrimStart('/'), "lfs", "objects");
        if (request != "batch") return Response("application/octet-stream", File.ReadAllBytes(Path.Combine(objects, request[..2], request[2..4], request)));
        var answer = JsonDocument.Parse(body).RootElement.GetProperty("objects").EnumerateArray().Select(asked => new
        {
            oid = asked.GetProperty("oid").GetString()!,
            size = asked.GetProperty("size").GetInt64(),
            actions = new { download = new { href = Url(repository.TrimStart('/') + "/info/lfs/objects/" + asked.GetProperty("oid").GetString()) } }
        });
        return Response("application/vnd.git-lfs+json", JsonSerializer.SerializeToUtf8Bytes(new { transfer = "basic", objects = answer }));
    }

    private static byte[] Response(string type, byte[] content) =>
        [.. Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: " + type + "\r\nContent-Length: " + content.Length + "\r\nConnection: close\r\n\r\n"), .. content];

    // git http-backend as CGI: the request goes in as variables and the body, and its headers come back as the response's.
    private async Task<byte[]> BackendAsync(string method, string target, Dictionary<string, string> headers, byte[] body)
    {
        var parts = target.Split('?', 2);
        var start = new ProcessStartInfo("git") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("http-backend");
        start.Environment["GIT_PROJECT_ROOT"] = root;
        start.Environment["GIT_HTTP_EXPORT_ALL"] = "1";
        // An authenticated user is what lets http-backend accept a push.
        start.Environment["REMOTE_USER"] = accepted.Split(':')[0];
        start.Environment["REQUEST_METHOD"] = method;
        start.Environment["PATH_INFO"] = Uri.UnescapeDataString(parts[0]);
        start.Environment["QUERY_STRING"] = parts.Length == 2 ? parts[1] : "";
        start.Environment["CONTENT_TYPE"] = headers.GetValueOrDefault("Content-Type") ?? "";
        start.Environment["CONTENT_LENGTH"] = body.Length.ToString();
        using var process = Process.Start(start)!;
        var output = new MemoryStream();
        var reading = process.StandardOutput.BaseStream.CopyToAsync(output, stop.Token);
        await process.StandardInput.BaseStream.WriteAsync(body, stop.Token);
        process.StandardInput.Close();
        await reading;
        await process.WaitForExitAsync(stop.Token);
        var bytes = output.ToArray();
        var end = bytes.AsSpan().IndexOf("\r\n\r\n"u8);
        var cgi = Encoding.ASCII.GetString(bytes, 0, end).Split("\r\n");
        var status = cgi.FirstOrDefault(line => line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase))?[7..].Trim() ?? "200 OK";
        var response = "HTTP/1.1 " + status + "\r\n" + string.Concat(cgi.Where(line => !line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)).Select(line => line + "\r\n"))
            + "Content-Length: " + (bytes.Length - end - 4) + "\r\nConnection: close\r\n\r\n";
        return [.. Encoding.ASCII.GetBytes(response), .. bytes.AsSpan(end + 4)];
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
        certificate.Dispose();
    }
}
