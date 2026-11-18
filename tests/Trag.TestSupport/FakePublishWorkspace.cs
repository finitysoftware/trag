using Trag.Application.Ports.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Tags;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

// The workspace the publish flow needs: the sync read plus the branch read. It is a type of its own for the same
// reason FakeSyncWorkspace is — the shell asks the workspace object which ports it implements.
public sealed class FakePublishWorkspace : IRepositoryWorkspace, ISyncWorkspace, IBranchWorkspace
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
    public Func<RepositorySnapshot, BranchSet?> ReadBranches { get; set; } = _ => new([], [], null);
    public List<(string Remote, string? Branch)> SyncReads { get; } = [];
    public AsyncGate SyncGate { get; } = new();

    public Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken cancellationToken) => Workspace.OpenAsync(path, cancellationToken);
    public Task<RepositorySnapshot?> RefreshAsync(CancellationToken cancellationToken) => Workspace.RefreshAsync(cancellationToken);
    public Task CloseAsync() => Workspace.CloseAsync();
    public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    public Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken cancellationToken) => Workspace.WriteAsync(request, cancellationToken);
    public Task<BranchSet?> ReadBranchesAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult(ReadBranches(expected));
    public Task<IReadOnlyList<TagInfo>?> ReadTagsAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<IReadOnlyList<TagInfo>?>([]);
    public Task<IReadOnlyList<StashEntry>?> ReadStashesAsync(RepositorySnapshot expected, CancellationToken ct) => Task.FromResult<IReadOnlyList<StashEntry>?>([]);
    public Task<bool?> ReadContainsAsync(RepositorySnapshot expected, string commit, CancellationToken ct) => Task.FromResult<bool?>(false);
    public Task<int?> ReadDistanceAsync(RepositorySnapshot expected, string commit, CancellationToken ct) => Task.FromResult<int?>(null);
    public Task<IReadOnlyList<GitPath>?> ReadNotInIndexAsync(RepositorySnapshot expected, string commit, CancellationToken ct) => Task.FromResult<IReadOnlyList<GitPath>?>([]);
    public Task<RewriteRange?> ReadRewriteAsync(RepositorySnapshot expected, string commit, CancellationToken ct) => Task.FromResult<RewriteRange?>(null);
    public Task<string?> ReadRemoteUrlAsync(RepositorySnapshot expected, string remote, CancellationToken ct) => Task.FromResult<string?>(null);
    public Task<MergePreview?> ReadMergePreviewAsync(RepositorySnapshot expected, string into, string from, CancellationToken ct) => Task.FromResult<MergePreview?>(null);
    public Task<RebasePreview?> ReadRebasePreviewAsync(RepositorySnapshot expected, string onto, CancellationToken ct) => Task.FromResult<RebasePreview?>(null);

    public Task<GitWriteResult?> FetchQuietlyAsync(RepositorySnapshot expected, RepositorySyncState target, string? accountId, CancellationToken ct) =>
        Task.FromResult<GitWriteResult?>(null);

    public async Task<RepositorySyncState?> ReadSyncAsync(RepositorySnapshot expected, string remote, string? branch, CancellationToken ct)
    {
        SyncReads.Add((remote, branch));
        await SyncGate.WaitAsync(ct).ConfigureAwait(false);
        return ReadSync(expected, remote, branch);
    }
}
