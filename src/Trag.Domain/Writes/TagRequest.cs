using Trag.Domain.Status;
using Trag.Domain.Tags;

namespace Trag.Domain.Writes;

// A tag created at a commit of the history; HEAD, the branches and the working tree stay as they are.
public sealed record TagRequest(RepositorySnapshot Expected, TagOperation Tag) : GitWriteRequest(Expected);
