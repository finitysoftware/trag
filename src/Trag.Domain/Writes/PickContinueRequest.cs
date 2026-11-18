using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The revert or cherry-pick in progress finished: its resolved changes committed with the message Git prepared for it.
public sealed record PickContinueRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
