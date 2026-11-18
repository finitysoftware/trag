using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A remote taken out of this repository's configuration, with its remote-tracking branches; the server keeps everything.
public sealed record RemoteRemoveRequest(RepositorySnapshot Expected, string Name) : GitWriteRequest(Expected);
