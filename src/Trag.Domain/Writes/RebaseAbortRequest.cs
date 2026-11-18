using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Give up the rebase in progress and let Git put the branch back where it was.
public sealed record RebaseAbortRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
