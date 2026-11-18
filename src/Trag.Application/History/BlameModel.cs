using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Blame;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Application.History;

// A file blamed for the blame view over the shown snapshot, as its rows: at a commit, or with none as the working tree
// has it. The rows, the file's code colored by its language among them, are built off the screen's thread as part of the
// read. A read the repository moved under or that failed says so, and a binary file that it has no lines; the shell's
// Cancel reaches the read in flight. While a file is blamed, the blame follows the repository: a read that a moved history
// or a write dropped is read again once the repository settles, and a file blamed in the working tree is read again
// whenever it may have changed, with its lines kept on screen meanwhile. The shell's Cancel during a read stops that
// until a file is blamed anew.
public sealed class BlameModel
{
    private readonly Repository repository;
    private readonly IHistoryWorkspace? reader;
    private (string Path, string? Revision)? shown;
    private bool following;
    // The latest read, and the latest that answered with rows, a message or a failure; they differ while one is in flight
    // or after the repository dropped it.
    private long started, answered;

    public BlameModel(Repository repository, IHistoryWorkspace? reader)
    {
        this.repository = repository;
        this.reader = reader;
        Blame = new([], repository.Cancels)
        {
            ChangedMessage = BlameText.Changed, FailedMessage = BlameText.NotRead,
            DescribeFailure = error => (error as BlameException)?.Message
                ?? (repository.Snapshot?.Limitations.Contains(LimitationText.PartialClone) == true ? BlameText.NotDownloaded : null)
        };
        repository.Cancels.Register(() => false, () => following &= answered == started);
        repository.Status.Settled += Follow;
    }

    public Loader<IReadOnlyList<BlameRow>> Blame { get; }
    // Git blames the working tree from HEAD, which a repository has from its first commit on.
    public bool CanBlameWorkingTree => repository.Snapshot is { IsUnborn: false };
    public Requery Requery => repository.Requery;
    // How Git looks past a change, for the shown file and every one blamed after it.
    public BlameOptions Options { get; private set; } = BlameOptions.None;

    public Task OpenAsync(string path, string? revision)
    {
        (shown, following) = ((path, revision), true);
        return ReadAsync(interim: []);
    }

    // Other options blame the shown file again, its lines kept on screen meanwhile.
    public Task UseAsync(BlameOptions options)
    {
        Options = options;
        return ReadAsync(interim: null);
    }

    // The view closed: no change reads the file again until one is blamed anew.
    public void Close() => shown = null;

    private void Follow(SnapshotChange change)
    {
        if (shown is { } file && following && (change.IsEverything && answered != started || file.Revision is null && change.Touches(file.Path)))
            _ = ReadAsync(interim: null);
    }

    private Task ReadAsync(IReadOnlyList<BlameRow>? interim)
    {
        if (repository.Snapshot is not { } snapshot || reader is null || shown is not { } file) return Task.CompletedTask;
        var (read, options) = (++started, Options);
        return Blame.LoadAsync(async ct =>
        {
            try
            {
                var blamed = await reader.ReadBlameAsync(snapshot, file.Revision, file.Path, options, ct).ConfigureAwait(false);
                if (read == started) answered = read;
                if (blamed is { IsBinary: true }) throw new BlameException(BlameText.Binary);
                return blamed is null ? null : BlameRows.Of(blamed, file.Path, repository.Time.GetUtcNow());
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                if (read == started) answered = read;
                throw;
            }
        }, interim);
    }
}
