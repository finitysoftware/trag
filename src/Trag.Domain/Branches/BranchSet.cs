namespace Trag.Domain.Branches;

public sealed record BranchSet(IReadOnlyList<BranchInfo> Local, IReadOnlyList<BranchInfo> Remote, string? Current, bool Truncated = false);
