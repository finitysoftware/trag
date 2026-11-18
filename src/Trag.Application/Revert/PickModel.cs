using Trag.Application.Live;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Application.Revert;

// A commit Git applies to the current branch, stopped at its conflicts: finished once the last one is resolved, or
// aborted, each through the repository's writer; and whether one began with the latest snapshot, which opens its view.
public sealed class PickModel
{
    private readonly Repository repository;

    public PickModel(Repository repository)
    {
        this.repository = repository;
        repository.SnapshotApplied += (snapshot, previous) => Began = repository.HasPick && snapshot.PickIdentity != previous?.PickIdentity;
    }

    public Repository Repository => repository;
    // What the view says, named for the operation Git applies; a revert until Git says otherwise.
    public PickWords Words => new(repository.Snapshot?.Operation ?? Domain.Status.RepositoryOperation.Revert);
    // A pick began with the latest snapshot, or another took the place of the one before.
    public bool Began { get; private set; }
    // Finish waits for the last conflict to be resolved, and for no resolved file to hold markers, as a merge does.
    public bool CanFinish => repository.CanFinishPick && !repository.Conflicts.Files.IsBlocked;

    public Task<GitWriteResult?> FinishAsync() => repository.Writer.RunAsync(expected => new PickContinueRequest(expected));
    public Task<GitWriteResult?> AbortAsync() => repository.Writer.RunAsync(expected => new PickAbortRequest(expected));
}
