using Trag.Application.Live;
using Trag.Application.Ports.Workspace;
using Trag.Domain.Naming;
using Trag.Domain.Writes;

namespace Trag.Application.Status;

// The people a co-author of the commit being written is chosen from: the repository's authors, the one with most
// commits first and without the user, read again whenever the choice opens.
public sealed class CoAuthorsModel(Repository repository, IHistoryWorkspace? reader)
{
    public Loader<IReadOnlyList<GitIdentity>> People { get; } = new(repository, reader is null ? null : async (snapshot, ct) =>
        await reader.ReadAuthorsAsync(snapshot, ct) is { } authors ? CoAuthorChoices.Rank(authors, snapshot.Author) : null, empty: []);
}
