using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Merge;

// The repository's merge. The one in progress, as the snapshot tells it: what is left of it and what keeps Finish back
// (MergeOverview, worked out once per snapshot over the repository's conflicts), Git's message without its conflict list,
// and the writes that finish or abort it. And one to start: what Git says merging one branch into another would do, read
// for the branches a dialog shows, and the merge itself as the writes it takes (MergeFlow).
public sealed class MergeModel
{
    private readonly IBranchWorkspace? reader;
    private readonly MergeFlow flow;
    // What the preview was read for, the branches with their tips; a snapshot, a choice or a tip that changes it reads again.
    private (string? Head, string? Branch, BranchInfo? Into, BranchInfo? From) previewKey;

    public MergeModel(Repository repository, IBranchWorkspace? reader, MergeFlow flow)
    {
        Repository = repository;
        this.reader = reader;
        this.flow = flow;
        repository.SnapshotApplied += (snapshot, previous) =>
        {
            Overview = new(snapshot, repository.Conflicts.Files);
            IsNew = snapshot.MergeIdentity != previous?.MergeIdentity;
            Began = repository.HasMerge && IsNew;
        };
    }

    public Repository Repository { get; }
    public MergeOverview Overview { get; private set; } = MergeOverview.Empty;
    // The merge of the latest snapshot is not the one of the snapshot before: one began, ended, or another took its place.
    public bool IsNew { get; private set; }
    // A merge began with the latest snapshot, or another took the place of the one before.
    public bool Began { get; private set; }
    // Git's message for the merge in progress, without its conflict list.
    public string Message => MergeMessages.Clean(Repository.Snapshot?.Merge?.Message ?? "");
    // A read the repository moved under is read again by the snapshot that moved it, so it has nothing to say itself.
    public Loader<MergePreview> Preview { get; } = new(MergePreview.None) { ChangedMessage = "", FailedMessage = Failures.MergePreviewNotRead };

    // Finish waits for every conflict and every file staged with markers, and needs an identity and a message.
    public bool CanFinish(string message) =>
        Repository.CanMerge && !Overview.IsBlocked && CommitRequest.CanCommit(Repository.Snapshot, message);

    public Task<GitWriteResult?> FinishAsync(string message) =>
        CanFinish(message) ? Repository.Writer.RunAsync(expected => new CommitRequest(expected, message, FinishesMerge: true)) : RepositoryWriter.NotRun;

    public Task<GitWriteResult?> AbortAsync() => Repository.CanMerge ? Repository.Writer.RunAsync(expected => new MergeAbortRequest(expected)) : RepositoryWriter.NotRun;

    // What merging the chosen branches would do, read over the shown snapshot once both are chosen.
    public void ReadPreview(MergeChoice choice)
    {
        var key = (Repository.Snapshot?.Head, Repository.Snapshot?.Branch, choice.Into, choice.From);
        if (key == previewKey && Preview.Value.IsRead) return;
        previewKey = key;
        if (reader is null || Repository.Snapshot is not { } snapshot || choice is not { IsComplete: true, Into: { } into, From: { } from })
        {
            Preview.Clear();
            return;
        }
        Preview.LoadAsync(async ct => await reader.ReadMergePreviewAsync(snapshot, into.Ref, from.Ref, ct), MergePreview.None);
    }

    public void ClearPreview()
    {
        Preview.Clear();
        previewKey = default;
    }

    // The merge a dialog confirmed, over the preview it showed: its writes run as one operation, busy until the last of
    // them, and a second start meanwhile is refused.
    public Operation Starting { get; } = new();

    public Task<GitWriteResult?> StartAsync(MergeChoice choice, MergePreview preview) => Starting.RunAsync(_ => flow.RunAsync(choice, preview));
}
