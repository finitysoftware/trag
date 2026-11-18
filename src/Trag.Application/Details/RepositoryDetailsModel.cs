using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Details;
using Trag.Domain.Presentation.Text;

namespace Trag.Application.Details;

// The repository's details as the dialog reads them each time it opens, in the background, over the shown snapshot.
public sealed class RepositoryDetailsModel(Repository repository, IHistoryWorkspace? reader)
{
    public Repository Repository => repository;
    public Loader<RepositoryDetails> Details { get; } = new(repository, reader is null ? null : reader.ReadDetailsAsync, RepositoryDetails.None)
    {
        ChangedMessage = Failures.DetailsChanged,
        FailedMessage = Failures.DetailsNotRead
    };
    // What the dialog shows of the details, over the shown snapshot; nothing until a read answers.
    public RepositoryOverview? Overview => Details.Value != RepositoryDetails.None && repository.Snapshot is { } snapshot
        ? RepositoryOverview.Of(snapshot, Details.Value) : null;
}
