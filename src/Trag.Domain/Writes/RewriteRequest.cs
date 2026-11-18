using Trag.Domain.Rebase;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// The current branch's commits after the base rewritten by the plan: reordered, reworded, joined or dropped.
public sealed record RewriteRequest(RepositorySnapshot Expected, string? Base, IReadOnlyList<RewriteStep> Steps) : GitWriteRequest(Expected);
