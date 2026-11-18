using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The current branch moved back to one of its own commits, in the mode chosen; the commits after it leave the branch.
public sealed record ResetRequest(RepositorySnapshot Expected, string Commit, ResetMode Mode) : GitWriteRequest(Expected);
