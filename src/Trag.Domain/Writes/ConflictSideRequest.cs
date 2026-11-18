using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A conflicted file resolved as a whole with our side or theirs, and staged. A side that deleted the file resolves it as
// a deletion.
public sealed record ConflictSideRequest(RepositorySnapshot Expected, GitPath Path, ConflictChoice Side) : GitWriteRequest(Expected);
