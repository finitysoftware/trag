using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Application.Status;

// The repository's status: the staged and the unstaged files (StatusFiles), prepared for every snapshot the session
// shows before it is published, so every follower of SnapshotApplied finds them ready. The first snapshot's lists are built off the UI
// thread; a refresh or a write edits the shown lists as a delta, planned off the UI thread and applied on it in chunks of
// 64, so a 10k-file repository stays responsive. A screen keeps its selection through the events around an update.
// And what the status does: the diff of one file on one side, and the writes that stage, unstage, commit and discard.
public sealed class StatusModel
{
    private readonly IViewDispatcher dispatcher;
    private readonly ILiveRepositoryWorkspace? reader;

    public StatusModel(Repository repository, IViewDispatcher dispatcher, ILiveRepositoryWorkspace? reader)
    {
        Repository = repository;
        this.dispatcher = dispatcher;
        this.reader = reader;
    }

    public Repository Repository { get; }
    public StatusFiles Staged { get; private set; } = new([], true);
    public StatusFiles Unstaged { get; private set; } = new([], false);
    // While an update edits the lists or hands them over, what that does to a view's selection is not the user's.
    public bool IsEditing { get; private set; }

    // Replaced: new lists, before their snapshot is published. Editing: a delta is about to edit the shown lists.
    // Edited: the lists are the snapshot's, before it is published. Settled: the snapshot is published, with what the
    // change may have touched.
    public event Action? Replaced;
    public event Action? Editing;
    public event Action? Edited;
    public event Action<SnapshotChange>? Settled;

    // Staging while conflicts are resolved in Trag (a merge, a rebase, a revert or a cherry-pick, or what a stash left)
    // stages a resolution, so it needs a write or a resolution; unstaging, committing and discarding need an ordinary
    // write, and a discard leaves a merge's files to the merge view.
    public bool IsResolving => Repository.Snapshot is { HasSupportedResolution: true };
    public bool CanStage => Repository.CanWrite || Repository.CanResolve;
    public bool CanUnstage => Repository.CanWrite;
    public bool CanDiscard => Repository.CanWrite && !Repository.HasMerge;
    public bool CanCommit(string message) =>
        Repository.CanWrite && Staged.Count != 0 && CommitRequest.CanCommit(Repository.Snapshot, message);

    // Staging names what git add needs for each file (FileStatus.StagePaths); unstaging restores every name of a file, both
    // of a rename. A name two files share goes to Git once.
    public Task<GitWriteResult?> StageAsync(IReadOnlyList<FileStatus> files) =>
        Repository.Writer.RunAsync(expected => new StageRequest(expected, PathsOf(files, file => file.StagePaths), IsResolving));
    public Task<GitWriteResult?> UnstageAsync(IReadOnlyList<FileStatus> files) =>
        Repository.Writer.RunAsync(expected => new UnstageRequest(expected, PathsOf(files, file => file.Paths)));
    private static GitPath[] PathsOf(IReadOnlyList<FileStatus> files, Func<FileStatus, IReadOnlyList<GitPath>> paths) =>
        files.SelectMany(paths).DistinctBy(path => path.Key).ToArray();
    public Task<GitWriteResult?> CommitAsync(string message) => Repository.Writer.RunAsync(expected => new CommitRequest(expected, message));
    public Task<GitWriteResult?> DiscardAsync(IReadOnlyList<GitPath> paths) => Repository.Writer.RunAsync(expected => new DiscardRequest(expected, paths));
    // Submodules not at the commits the branch records, and the write that checks those commits out.
    public IReadOnlyList<string> SubmodulesToUpdate => Repository.Snapshot?.SubmodulesToUpdate ?? [];
    public bool CanUpdateSubmodules => Repository.CanWrite && SubmodulesToUpdate.Count != 0;
    public Task<GitWriteResult?> UpdateSubmodulesAsync() => Repository.Writer.RunAsync(expected => new SubmoduleUpdateRequest(expected));

    // One hunk of a file's shown diff, or chosen lines of it, staged from the working tree's or unstaged from the index's,
    // or discarded from the working tree, as an ordinary write.
    public bool CanTakeHunk(FileStatus file, bool fromIndex) => Repository.CanWrite && HunkPatch.Fits(file, fromIndex);
    public Task<GitWriteResult?> TakeHunkAsync(FileStatus file, bool fromIndex, IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen) =>
        PatchAsync(file, lines, chosen, fromIndex ? PatchAction.Unstage : PatchAction.Stage);
    public Task<GitWriteResult?> DiscardHunkAsync(FileStatus file, IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen) =>
        PatchAsync(file, lines, chosen, PatchAction.Discard);

    private Task<GitWriteResult?> PatchAsync(FileStatus file, IReadOnlyList<PatchLine> lines, IReadOnlyList<int> chosen, PatchAction action) =>
        Repository.Writer.RunAsync(expected => new PatchRequest(expected, file.Path, HunkPatch.For(lines, chosen, file.Path, action), action, !DiffBlocks.IsWhole(lines, chosen)));

    // The diff of one file on one side, read over the shown working tree; null when nothing can read it.
    public Func<bool, CancellationToken, Task<FileDiff?>>? DiffOf(FileStatus file, bool fromIndex) =>
        reader is not null && Repository.HasRepository ? (preview, ct) => reader.ReadDiffAsync(file, fromIndex, preview, ct) : null;

    // The lists for a snapshot, as a delta over the shown ones when asked; publish runs once they are its lists, and the
    // change says which files may differ.
    public async Task ShowAsync(RepositorySnapshot snapshot, bool delta, CancellationToken ct, Action publish, SnapshotChange change)
    {
        if (delta)
        {
            await UpdateAsync(snapshot, ct, publish, change);
            return;
        }
        var lists = await Task.Run(() => (Staged: new StatusFiles(snapshot.Files, true), Unstaged: new StatusFiles(snapshot.Files, false)), ct);
        ct.ThrowIfCancellationRequested();
        (Staged, Unstaged) = lists;
        Replaced?.Invoke();
        publish();
    }

    // Edits the shown lists towards the snapshot, then publishes it.
    private async Task UpdateAsync(RepositorySnapshot snapshot, CancellationToken ct, Action publish, SnapshotChange change)
    {
        var staged = Staged;
        var unstaged = Unstaged;
        var beforeStaged = staged.Snapshot();
        var beforeUnstaged = unstaged.Snapshot();
        var plan = await Task.Run(() =>
        {
            var nextStaged = new StatusFiles(snapshot.Files, true);
            var nextUnstaged = new StatusFiles(snapshot.Files, false);
            return (Staged: StatusDelta.Plan(beforeStaged, nextStaged), Unstaged: StatusDelta.Plan(beforeUnstaged, nextUnstaged));
        }, ct);
        ct.ThrowIfCancellationRequested();
        Editing?.Invoke();
        try
        {
            await ApplyEditsAsync(staged, plan.Staged, ct);
            await ApplyEditsAsync(unstaged, plan.Unstaged, ct);
            ct.ThrowIfCancellationRequested();
            IsEditing = true;
            Edited?.Invoke();
            publish();
        }
        catch (OperationCanceledException)
        {
            // Cancel stopped the edits part way, and the previous status stays published: the lists are that status again.
            (Staged, Unstaged) = (new StatusFiles(beforeStaged, true), new StatusFiles(beforeUnstaged, false));
            Replaced?.Invoke();
            throw;
        }
        finally { IsEditing = false; }
        Settled?.Invoke(change);
    }

    private async Task ApplyEditsAsync(StatusFiles list, IReadOnlyList<StatusDelta.Edit> edits, CancellationToken ct)
    {
        foreach (var chunk in edits.Chunk(64))
        {
            ct.ThrowIfCancellationRequested();
            await dispatcher.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                    return;
                IsEditing = true;
                try { list.ApplyBatch(chunk); }
                finally { IsEditing = false; }
            });
        }
    }
}
