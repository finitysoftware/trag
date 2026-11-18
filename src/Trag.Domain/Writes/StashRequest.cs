using Trag.Domain.Stash;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Changes put aside, taken back, or thrown away.
public sealed record StashRequest(RepositorySnapshot Expected, StashOperation Stash, StashAction Action) : GitWriteRequest(Expected);
