using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Commit the resolved stop with this message and let Git replay the commits still waiting.
public sealed record RebaseContinueRequest(RepositorySnapshot Expected, string Message) : GitWriteRequest(Expected);
