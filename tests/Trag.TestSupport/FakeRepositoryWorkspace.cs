using Trag.Application.Ports.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

// The shell's own view of a repository: it opens and refreshes to Snapshot, records every write and answers it
// behind the gate. Every answer carries the snapshot of that moment, the way the real workspace re-reads status
// before it reports; a result that already names a snapshot keeps the one it names.
public sealed class FakeRepositoryWorkspace : IRepositoryWorkspace
{
    private RepositorySnapshot snapshot = Snapshots.Create();

    // Read replaces Snapshot for a test whose backend computes the status from its own state instead of holding one.
    public RepositorySnapshot Snapshot { get => Read is null ? snapshot : Read(); set => snapshot = value; }
    public Func<RepositorySnapshot>? Read { get; set; }
    public GitWriteResult Result { get; set; } = new(GitWriteOutcome.Succeeded, "Done");
    // A Handle delegate wins over Result: it is how a test lets a write change the state the next read answers with.
    public Func<GitWriteRequest, GitWriteResult>? Handle { get; set; }
    public List<GitWriteRequest> Requests { get; } = [];
    public GitWriteRequest? LastRequest => Requests.Count == 0 ? null : Requests[^1];
    public Exception? Failure { get; set; }
    public AsyncGate Gate { get; } = new();
    // Runs before an open or a refresh answers: a test holds a read there, or ends it canceled or failed.
    public Func<CancellationToken, Task>? Reading { get; set; }

    public Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken cancellationToken) => AnswerAsync(cancellationToken);
    public Task<RepositorySnapshot?> RefreshAsync(CancellationToken cancellationToken) => AnswerAsync(cancellationToken);
    public Task CloseAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<RepositorySnapshot?> AnswerAsync(CancellationToken cancellationToken)
    {
        if (Reading is not null) await Reading(cancellationToken).ConfigureAwait(false);
        return Snapshot;
    }

    public async Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Failure is not null) throw Failure;
        var result = Handle?.Invoke(request) ?? Result;
        return result.Snapshot is null ? result with { Snapshot = Snapshot } : result;
    }
}
