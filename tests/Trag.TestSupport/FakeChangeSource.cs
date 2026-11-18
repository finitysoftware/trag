using System.Threading.Channels;
using Trag.Application.Live;
using Trag.Application.Ports.Watching;

namespace Trag.TestSupport;

public sealed class FakeChangeSource : IRepositoryChangeSource
{
    private readonly Channel<RepositoryChange> changes = Channel.CreateUnbounded<RepositoryChange>();
    private readonly TaskCompletionSource reading = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource reconciled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsPolling { get; set; }
    public int Reconciles { get; private set; }
    // Complete once the stream has started reading this source, and once it has asked it to reconcile for the first time.
    public Task Reading => reading.Task;
    public Task Reconciled => reconciled.Task;
    public bool IsDisposed { get; private set; }
    public CancellationToken LastToken { get; private set; }

    public void Push(params string[] paths) => changes.Writer.TryWrite(new(paths, false));
    public void PushReconcile() => changes.Writer.TryWrite(new([], true));

    public async Task<RepositoryChange> ReadAsync(CancellationToken cancellationToken)
    {
        LastToken = cancellationToken;
        reading.TrySetResult();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return await changes.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Reconcile() { Reconciles++; reconciled.TrySetResult(); }
    public void Dispose() { IsDisposed = true; changes.Writer.TryComplete(); }
}
