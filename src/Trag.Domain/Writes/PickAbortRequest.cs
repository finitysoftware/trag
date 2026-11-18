using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The revert or cherry-pick in progress given up: the branch and its files back as they were before it.
public sealed record PickAbortRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
