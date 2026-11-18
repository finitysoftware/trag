using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// Part of one file's changes, as a patch git apply takes, staged into the index or taken back out of it: a whole hunk, or
// chosen lines that are not one, which is how the write names it.
public sealed record PatchRequest(RepositorySnapshot Expected, GitPath Path, string Patch, PatchAction Action, bool Lines) : GitWriteRequest(Expected);
