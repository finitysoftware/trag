using Trag.Application.Live;
using Trag.Application.Ports.Git;
using Trag.Application.Ports.Watching;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Blame;
using Trag.Domain.Branches;
using Trag.Domain.Details;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Rebase;
using Trag.Domain.Stash;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Tags;
using Trag.Domain.Writes;

namespace Trag.Application.Workspace;

// Composes repository lifecycles over one shared state. Optional backends allow read-only and preview workspaces.
public sealed class RepositoryWorkspace : IRepositoryWorkspace, ILiveRepositoryWorkspace, IHistoryWorkspace, IBranchWorkspace,
    ISyncWorkspace
{
    private readonly WorkspaceState state = new();
    private readonly RepositoryOpener opener;
    private readonly LiveChangeStream live;
    private readonly IFileDiffReader? diffs;
    private readonly RepositoryWriteService? writes;
    private readonly IHistoryReader? historyReader;
    private readonly SnapshotGuard guard;
    private readonly IBranchReader? branches;
    private readonly IStashReader? stashes;
    private readonly IGitSyncReader? sync;
    // The quiet fetch running now. The shell cancels it when a write begins, and the write waits for it to end, so its
    // Git is gone, with the ref locks it held, before the write's Git starts.
    private Task quietFetch = Task.CompletedTask;

    public RepositoryWorkspace(IRepositoryReader reader, IRepositoryWatcher? watcher = null, IFileDiffReader? diffReader = null,
        IGitWriter? writer = null, IHistoryReader? historyReader = null, IGitSyncReader? synchronizer = null,
        IBranchReader? branchReader = null, IStashReader? stashReader = null, Action<Exception>? defects = null)
    {
        var watchers = new WatcherAttachment(watcher, state);
        guard = new(state);
        opener = new(reader, state, watchers);
        live = new(reader, state, watchers);
        diffs = diffReader;
        if (writer is not null)
            writes = new(writer, state, new WriteVerification(reader, state), defects ?? (_ => { }));
        this.historyReader = historyReader;
        branches = branchReader;
        stashes = stashReader;
        sync = synchronizer;
    }

    public Task<RepositorySnapshot?> OpenAsync(string path, CancellationToken ct) => opener.OpenAsync(path, ct);
    public Task<RepositorySnapshot?> RefreshAsync(CancellationToken ct) => opener.RefreshAsync(ct);
    public Task CloseAsync() => opener.CloseAsync();

    public IAsyncEnumerable<RepositoryUpdate> WatchAsync(CancellationToken ct) => live.WatchAsync(ct);
    public bool IsCurrent(RepositorySnapshot snapshot) => state.Holds(snapshot);
    public Task<FileDiff?> ReadDiffAsync(FileStatus file, bool fromIndex, bool preview, CancellationToken ct) =>
        ReadWorkingAsync((reader, root, token) => reader.ReadAsync(root, file, fromIndex, preview, token), ct);

    public Task<ConflictText?> ReadConflictAsync(FileStatus file, CancellationToken ct) =>
        ReadWorkingAsync((reader, root, token) => reader.ReadConflictAsync(root, file, token), ct);

    // A read of a working file, while the workspace accepts one; null without a reader.
    private Task<T?> ReadWorkingAsync<T>(Func<IFileDiffReader, string, CancellationToken, Task<T>> read, CancellationToken ct) where T : class =>
        diffs is { } reader && state.BeginWorkingRead() is { } working
            ? working.ReadAsync((root, token) => read(reader, root, token), ct) : Task.FromResult<T?>(null);

    public async Task<GitWriteResult> WriteAsync(GitWriteRequest request, CancellationToken ct)
    {
        if (writes is null)
            return GitWriteResult.Rejected(WriteText.WritingUnavailable);
        await quietFetch.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return await writes.WriteAsync(request, ct).ConfigureAwait(false);
    }

    // History pages, commit details, commit diffs and blamed files, valid only for the snapshot the caller is showing.
    // The current branch is shown with its upstream: what others pushed and this branch lacks shows above HEAD. An unborn
    // branch has nothing of its own, while every branch's history still has the other branches' commits.
    public Task<HistoryPage?> ReadHistoryAsync(RepositorySnapshot expected, HistoryQuery query, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => query.AllBranches || expected.Head is not null
            ? reader.ReadPageAsync(expected.Root, expected.Head, query.AllBranches ? query : query with { Upstream = expected.UpstreamHead }, token)
            : Task.FromResult(new HistoryPage([], false, query.PageIndex)), ct);
    public Task<CommitDetails?> ReadCommitAsync(RepositorySnapshot expected, string id, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => reader.ReadCommitAsync(expected.Root, id, token), ct);
    public Task<FileDiff?> ReadCommitDiffAsync(RepositorySnapshot expected, CommitDetails commit, FileDiffMetadata file, bool preview, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => reader.ReadDiffAsync(expected.Root, commit, file, preview, token), ct);
    public Task<FileBlame?> ReadBlameAsync(RepositorySnapshot expected, string? revision, string path, BlameOptions options, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => reader.ReadBlameAsync(expected.Root, revision, path, options, token), ct);
    // Before the first commit there is nobody to read.
    public Task<IReadOnlyList<GitIdentity>?> ReadAuthorsAsync(RepositorySnapshot expected, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => expected.IsUnborn ? Task.FromResult<IReadOnlyList<GitIdentity>>([]) : reader.ReadAuthorsAsync(expected.Root, token), ct);

    public Task<RepositoryDetails?> ReadDetailsAsync(RepositorySnapshot expected, CancellationToken ct) =>
        guard.ReadAsync(historyReader, expected, (reader, token) => reader.ReadDetailsAsync(expected.Root, expected.IsUnborn, expected.HasLfsFiles, token), ct);

    // A commit's file written where the user chose: a commit does not move with the repository, so no guard compares the
    // snapshot, and whatever fails is the caller's to say, which is why this throws where the reads answer null.
    public Task SaveFileAsync(RepositorySnapshot expected, string commit, GitPath path, string destination, CancellationToken ct) =>
        (historyReader ?? throw new InvalidOperationException()).SaveFileAsync(expected.Root, commit, path, destination, ct);

    public Task<BranchSet?> ReadBranchesAsync(RepositorySnapshot expected, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.ReadBranchesAsync(expected.Root, token), ct);
    public Task<IReadOnlyList<TagInfo>?> ReadTagsAsync(RepositorySnapshot expected, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.ReadTagsAsync(expected.Root, token), ct);
    public Task<bool?> ReadContainsAsync(RepositorySnapshot expected, string commit, CancellationToken ct) =>
        guard.ReadValueAsync(branches, expected, async (reader, token) => (bool?)await reader.ContainsAsync(expected.Root, commit, token), ct);
    // A distance, a range or an address may itself be absent: the branch lacks the commit, or there is no such remote.
    public Task<int?> ReadDistanceAsync(RepositorySnapshot expected, string commit, CancellationToken ct) =>
        guard.ReadValueAsync(branches, expected, (reader, token) => reader.DistanceAsync(expected.Root, commit, token), ct);
    public Task<IReadOnlyList<GitPath>?> ReadNotInIndexAsync(RepositorySnapshot expected, string commit, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.ReadNotInIndexAsync(expected.Root, commit, token), ct);
    public Task<RewriteRange?> ReadRewriteAsync(RepositorySnapshot expected, string commit, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.ReadRewriteAsync(expected.Root, commit, token), ct);
    public Task<string?> ReadRemoteUrlAsync(RepositorySnapshot expected, string remote, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.RemoteUrlAsync(expected.Root, remote, token), ct);
    public Task<MergePreview?> ReadMergePreviewAsync(RepositorySnapshot expected, string into, string from, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.PreviewMergeAsync(expected.Root, into, from, token), ct);
    public Task<RebasePreview?> ReadRebasePreviewAsync(RepositorySnapshot expected, string onto, CancellationToken ct) =>
        guard.ReadAsync(branches, expected, (reader, token) => reader.PreviewRebaseAsync(expected.Root, onto, token), ct);
    public Task<IReadOnlyList<StashEntry>?> ReadStashesAsync(RepositorySnapshot expected, CancellationToken ct) =>
        guard.ReadAsync(stashes, expected, (reader, token) => reader.ReadStashesAsync(expected.Root, token), ct);
    public Task<RepositorySyncState?> ReadSyncAsync(RepositorySnapshot expected, string remote, string? branch, CancellationToken ct) =>
        guard.ReadAsync(sync, expected, (reader, token) => reader.ReadAsync(expected, remote, branch, token), ct);

    // The synchronizer sends it when it can write too, as Git's does; it takes no read slot from the views, and the
    // session's end cancels it.
    public async Task<GitWriteResult?> FetchQuietlyAsync(RepositorySnapshot expected, RepositorySyncState target, string? accountId, CancellationToken ct)
    {
        if (sync is not IGitSyncWriter writer || !state.TryBeginHistoryRead(expected, ct, out _, out var linked))
            return null;
        using (linked)
        {
            var fetch = writer.ExecuteAsync(new SyncRequest(expected, target, SyncAction.Fetch, accountId), linked.Token);
            quietFetch = fetch;
            return await fetch.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await state.BeginDispose().ConfigureAwait(false);
        await opener.CloseAsync().ConfigureAwait(false);
    }
}
