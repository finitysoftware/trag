using Trag.Application.Ports.Git;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;

namespace Trag.TestSupport;

public sealed class FakeGitSync : IGitSyncReader, IGitSyncWriter
{
    public Func<RepositorySnapshot, string, string?, RepositorySyncState> State { get; set; } = DefaultState;
    public List<(string Remote, string? Branch)> Reads { get; } = [];
    public GitWriteResult Result { get; set; } = new(GitWriteOutcome.Succeeded, "Synced");
    // Thrown by a write instead of its result, as a defect would be.
    public Exception? Failure { get; set; }
    public AsyncGate ReadGate { get; } = new();
    public List<GitWriteRequest> Writes { get; } = [];
    // Holds a write until the test opens it; a write held here still ends when its token is cancelled.
    public AsyncGate WriteGate { get; } = new();

    public async Task<RepositorySyncState> ReadAsync(RepositorySnapshot snapshot, string remote, string? branch, CancellationToken ct)
    {
        Reads.Add((remote, branch));
        await ReadGate.WaitAsync(ct).ConfigureAwait(false);
        return State(snapshot, remote, branch);
    }

    public async Task<GitWriteResult> ExecuteAsync(GitWriteRequest request, CancellationToken ct)
    {
        Writes.Add(request);
        await WriteGate.WaitAsync(ct).WaitAsync(ct).ConfigureAwait(false);
        if (Failure is { } failure) throw failure;
        return Result;
    }

    // A tracking branch under the snapshot's own name, with zero counts once a branch is chosen.
    public static RepositorySyncState DefaultState(RepositorySnapshot snapshot, string remote, string? branch)
    {
        var name = snapshot.Branch ?? "main";
        var item = new SyncBranch("refs/heads/" + name, "refs/remotes/" + remote + "/" + name);
        var chosen = branch ?? (snapshot.TrackingRemote == remote ? snapshot.UpstreamBranchRef : null);
        var selected = chosen is null ? null : item;
        return new(new(remote, "https://fetch.example.invalid/" + remote, "https://publish.example.invalid/" + remote, "config"), [item], selected,
            selected is null ? null : 0, selected is null ? null : 0, "Local comparison");
    }
}
