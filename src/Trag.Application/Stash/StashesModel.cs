using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Stash;
using Trag.Domain.Writes;

namespace Trag.Application.Stash;

// The repository's stashes: the list, which follows refs/stash (it reads when that ref moves and empties when it is
// gone), and the writes that set changes aside, bring an entry back and drop one, each through the repository's writer.
public sealed class StashesModel
{
    public StashesModel(Repository repository, IBranchWorkspace? reader)
    {
        Repository = repository;
        List = new(repository, reader is null ? null : reader.ReadStashesAsync, empty: [], key: snapshot => snapshot.StashHead)
        {
            FailedMessage = Failures.StashesNotRead
        };
    }

    public Repository Repository { get; }
    // The clock the entries' times are told by.
    public TimeProvider Time => Repository.Time;
    public Loader<IReadOnlyList<StashEntry>> List { get; }
    // The snapshot says whether there is a stash before the list arrives.
    public bool HasStashes => Repository.Snapshot?.StashHead is not null;
    // Setting changes aside needs changes and a repository that takes an ordinary write.
    public bool CanPush => Repository.CanWrite && Repository.Snapshot is { HasChanges: true };

    public Task<GitWriteResult?> PushAsync(StashOperation operation) => WriteAsync(StashAction.Push, operation);
    public Task<GitWriteResult?> ApplyAsync(string id) => WriteAsync(StashAction.Apply, new(Id: id));
    public Task<GitWriteResult?> DropAsync(string id) => WriteAsync(StashAction.Drop, new(Id: id));

    private Task<GitWriteResult?> WriteAsync(StashAction action, StashOperation operation) =>
        Repository.Writer.RunAsync(expected => new StashRequest(expected, operation, action));
}
