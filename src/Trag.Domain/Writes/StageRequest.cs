using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Whole files from the working tree into the index. Resolution says the same thing is being staged as the resolution
// of a conflict, which is allowed during a merge where an ordinary stage is not.
public sealed record StageRequest(RepositorySnapshot Expected, IReadOnlyList<GitPath> Paths, bool Resolution = false) : GitWriteRequest(Expected);
