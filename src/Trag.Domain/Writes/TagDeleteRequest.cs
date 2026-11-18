using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A tag deleted here; the commit it marked, and a tag of the name on any remote, stay as they are.
public sealed record TagDeleteRequest(RepositorySnapshot Expected, string Name) : GitWriteRequest(Expected);
