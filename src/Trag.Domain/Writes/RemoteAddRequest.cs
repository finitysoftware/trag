using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A remote added to this repository's configuration under a name, at an address; nothing is fetched or sent.
public sealed record RemoteAddRequest(RepositorySnapshot Expected, string Name, string Url) : GitWriteRequest(Expected);
