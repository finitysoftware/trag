using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Deleting a branch on the remote. It is a push of nothing, not a local branch operation, so it is its own request
// and the router no longer has to look inside a payload to decide where to send it.
public sealed record RemoteBranchDeleteRequest(RepositorySnapshot Expected, string Remote, string Name) : GitWriteRequest(Expected);
