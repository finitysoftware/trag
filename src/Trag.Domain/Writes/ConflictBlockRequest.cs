using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// One conflict block of a working file rewritten as our side, theirs or both. The hash is of the bytes the choice was
// made on: a file that changed since is left alone. The file stays a conflict until it is marked as resolved.
public sealed record ConflictBlockRequest(RepositorySnapshot Expected, GitPath Path, int Block, ConflictChoice Choice, string ContentHash)
    : GitWriteRequest(Expected);
