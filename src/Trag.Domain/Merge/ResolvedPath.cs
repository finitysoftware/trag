using Trag.Domain.Paths;

namespace Trag.Domain.Merge;

// A conflict of this merge that was already resolved and staged, how, whether what was staged still holds conflict
// markers (staged in another tool, or by hand with the markers left in), and whether Git can bring it back: checkout -m
// merges both sides' files again, so not a conflict one side had no file for.
public sealed record ResolvedPath(GitPath Path, Resolution How, bool HasMarkers = false, bool Reopens = true);
