using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Trag.TestSupport;

// A folder served read-only over HTTP on the loopback address, as Git's dumb HTTP protocol reads a repository: each
// request is one file, and nothing is written. A submodule cloned from it takes a transport Git allows for submodules,
// unlike a local path. A repository needs git update-server-info before it is served.
public sealed class LoopbackGitServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly string root;

    public LoopbackGitServer(string root)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public string Url(string path) => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/" + path;

    private async Task AcceptAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await listener.AcceptTcpClientAsync(stop.Token));
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    // The request line names the file; the headers are read and ignored, and the answer closes the connection.
    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                var request = await reader.ReadLineAsync(stop.Token) ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                var target = request.Split(' ') is [_, var path, ..] ? Uri.UnescapeDataString(path.Split('?')[0].TrimStart('/')) : "";
                var file = Path.GetFullPath(Path.Combine(root, target));
                var found = file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(file);
                var body = found ? await File.ReadAllBytesAsync(file, stop.Token) : [];
                var head = (found ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") + "\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), stop.Token);
                await stream.WriteAsync(body, stop.Token);
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        stop.Dispose();
    }
}
