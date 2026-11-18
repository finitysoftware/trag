using Trag.Domain.Writes;

namespace Trag.Domain.Branches;

// One move of the branch picker as a Git write.
public sealed record BranchMove(BranchAction Action, BranchOperation Operation);
