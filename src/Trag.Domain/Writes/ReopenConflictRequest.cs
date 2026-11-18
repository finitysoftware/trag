using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// A resolved conflict brought back: Git recreates the conflicted file with its markers and unstages the resolution.
public sealed record ReopenConflictRequest(RepositorySnapshot Expected, GitPath Path) : GitWriteRequest(Expected);
