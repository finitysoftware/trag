using Trag.Application.Ports.Workspace;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

// The workspace as the sync section sees it: everything FakeRepositoryWorkspace answers, plus the sync read. It is
// a type of its own rather than one more port on the base fake because the shell asks the workspace object which
// ports it implements, so a test that has no sync must not be handed one.
public sealed class FakeSyncWorkspace : IRepositoryWorkspace, ISyncWorkspace
{
    public FakeRepositoryWorkspace Workspace { get; } = new();
    public RepositorySnapshot Snapshot { get => Workspace.Snapshot; set => Workspace.Snapshot = value; }
    public Func<RepositorySnapshot>? Read { get => Workspace.Read; set => Workspace.Read = value; }
    public GitWriteResult Result { get => Workspace.Result; set => Workspace.Result = value; }
    public Func<GitWriteRequest, GitWriteResult>? Handle { get => Workspace.Handle; set => Workspace.Handle = value; }
    public List<GitWriteRequest> Requests => Workspace.Requests;
    public GitWriteRequest? LastRequest => Workspace.LastRequest;
    public AsyncGate Gate => Workspace.Gate;

    public Func<RepositorySnapshot, string, string?, RepositorySyncState?> ReadSync { get; set; } = FakeGitSync.DefaultState;
    public List<(string Remote, string? Branch)> SyncReads { get; } = [];
    public AsyncGate SyncGate { get; } = new();

    public Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken cancellationToken) => Workspace.OpenAsync(path, cancellationToken);
    public Task<RepositorySnapshot?> RefreshAsync(CancellationToken cancellationToken) => Workspace.RefreshAsync(cancellationToken);
    public Task CloseAsync() => Workspace.CloseAsync();
    public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    public Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken cancellationToken) => Workspace.WriteAsync(request, cancellationToken);

    public async Task<RepositorySyncState?> ReadSyncAsync(RepositorySnapshot expected, string remote, string? branch, CancellationToken ct)
    {
        SyncReads.Add((remote, branch));
        await SyncGate.WaitAsync(ct).ConfigureAwait(false);
        return ReadSync(expected, remote, branch);
    }

    // The quiet fetches asked for, and what each one answers; by default a fetch that succeeded.
    public List<SyncRequest> QuietFetches { get; } = [];
    public Func<SyncRequest, GitWriteResult?> QuietFetch { get; set; } = _ => new(GitWriteOutcome.Succeeded, "Fetch completed.");
    public AsyncGate QuietGate { get; } = new();

    public async Task<GitWriteResult?> FetchQuietlyAsync(RepositorySnapshot expected, RepositorySyncState target, string? accountId, CancellationToken ct)
    {
        var request = new SyncRequest(expected, target, SyncAction.Fetch, accountId);
        QuietFetches.Add(request);
        // A held fetch ends when its token is cancelled, as Git's process would.
        await QuietGate.WaitAsync(ct).WaitAsync(ct).ConfigureAwait(false);
        return QuietFetch(request);
    }
}
