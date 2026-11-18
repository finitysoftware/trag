using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A local branch given a new name; Git moves its upstream and reflog with it, and HEAD when it is the current one.
public sealed record BranchRenameRequest(RepositorySnapshot Expected, string Name, string NewName) : GitWriteRequest(Expected);
