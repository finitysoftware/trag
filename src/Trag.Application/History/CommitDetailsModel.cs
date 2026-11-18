using CommunityToolkit.Mvvm.ComponentModel;
using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Diff;
using Trag.Domain.History;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.History;

// The commit the commit pane shows, a commit of the history or a stash: read with its files when it is shown, and the
// diff of one of its files when the pane asks for it. A read the repository moved under says so; the shell's Cancel
// reaches the read in flight.
public sealed class CommitDetailsModel : ObservableObject
{
    private readonly IHistoryWorkspace? reader;

    public CommitDetailsModel(Repository repository, IHistoryWorkspace? reader)
    {
        Repository = repository;
        this.reader = reader;
        Commit = new(ShownCommit.None, repository.Cancels)
        {
            ChangedMessage = CommitText.Changed,
            FailedMessage = CommitText.NotRead,
            CanceledMessage = CommitText.LoadCanceled
        };
    }

    public Repository Repository { get; }
    // The commit asked for, whether it is still loading or could not be read. It is announced on every Show, the same
    // commit shown again included: whatever showed the previous one starts over.
    public HistoryCommit? Subject { get; private set; }
    public Loader<ShownCommit> Commit { get; }
    public bool IsReading => Commit.IsBusy;
    // Whether the pane shows that commit with its details read: a read that was dropped or failed shows a line instead.
    public bool Shows(HistoryCommit commit) => Subject?.Id == commit.Id && Commit.Value.Details is not null;

    // Raised when the reads in flight are dropped, a file diff the pane reads among them.
    public event Action? Interrupted;

    public Task Show(HistoryCommit? commit)
    {
        Commit.Clear();
        Subject = commit;
        OnPropertyChanged(nameof(Subject));
        if (commit is null || Repository.Snapshot is not { } snapshot || reader is null) return Task.CompletedTask;
        return Commit.LoadAsync(async ct => await reader.ReadCommitAsync(snapshot, commit.Id, ct) is { } details ? ShownCommit.Of(details) : null,
            interim: ShownCommit.Loading);
    }

    public void Clear() => Show(null);

    public void Cancel()
    {
        Commit.Cancel();
        Interrupted?.Invoke();
    }

    // The diff of one file of the shown commit, read over the snapshot it is asked in; null when nothing can read it.
    public Func<bool, CancellationToken, Task<FileDiff?>>? DiffOf(CommitDetails details, FileDiffMetadata file) =>
        Repository.Snapshot is { } snapshot && reader is not null
            ? (preview, ct) => reader.ReadCommitDiffAsync(snapshot, details, file, preview, ct)
            : null;
}
