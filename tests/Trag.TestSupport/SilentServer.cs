using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Trag.TestSupport;

// A server that takes every connection on the loopback address and then says nothing, as a stalled proxy or a host
// behind a dead VPN does. Connected completes once the first client is in, so a test can stop a transfer while Git, Git
// LFS or a submodule clone waits for an answer that never comes.
public sealed class SilentServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentBag<TcpClient> held = [];
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource stop = new();
    private readonly Task accepting;

    public SilentServer()
    {
        listener.Start();
        accepting = Task.Run(AcceptAsync);
    }

    public string Url(string path) => "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/" + path;

    public Task Connected => connected.Task;

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                held.Add(await listener.AcceptTcpClientAsync(stop.Token));
                connected.TrySetResult();
            }
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
        accepting.GetAwaiter().GetResult();
        foreach (var client in held) client.Dispose();
        stop.Dispose();
    }
}
