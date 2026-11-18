using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A tag sent to a remote, or deleted there. It talks to the remote, so the synchronizer answers it; the local tag stays
// as it is either way.
public sealed record RemoteTagRequest(RepositorySnapshot Expected, string Remote, string Name, RemoteTagAction Action) : GitWriteRequest(Expected);
