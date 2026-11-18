using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Leave out the commit the rebase stopped at and replay the rest.
public sealed record RebaseSkipRequest(RepositorySnapshot Expected) : GitWriteRequest(Expected);
