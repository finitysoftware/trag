using Trag.Application.Ports.Git;
using Trag.Domain.Status;

namespace Trag.TestSupport;

public sealed class FakeRepositoryReader : IRepositoryReader
{
    // Either replace Snapshot as a whole or adjust the pieces most tests vary; Read overrides everything.
    public RepositorySnapshot Snapshot { get; set; } = Snapshots.Create();
    public Func<string, CancellationToken, Task<RepositorySnapshot>>? Read { get; set; }
    public Exception? Failure { get; set; }
    public AsyncGate Gate { get; } = new();
    public int Calls { get; private set; }
    public string? LastPath { get; private set; }

    public async Task<RepositorySnapshot> ReadAsync(string path, CancellationToken cancellationToken)
    {
        Calls++;
        LastPath = path;
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Failure is not null) throw Failure;
        if (Read is not null) return await Read(path, cancellationToken).ConfigureAwait(false);
        // A fresh instance per read, like a real reader: the workspace compares snapshots by reference.
        return Snapshot with { Root = path };
    }
}
