using Trag.Domain.Branches;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A local branch created, switched to, deleted, or a commit checked out with HEAD detached there. Which of the four
// decides the order of the checks the branch writer runs, so it travels with the request rather than being guessed.
public sealed record BranchRequest(RepositorySnapshot Expected, BranchOperation Branch, BranchAction Action) : GitWriteRequest(Expected);
