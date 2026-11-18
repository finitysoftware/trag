using Trag.Domain.Paths;

namespace Trag.Domain.Merge;

// A conflict still open, with the number of conflict blocks left in its working file; null when the file cannot be read
// as text (binary, too large, deleted on one side).
public sealed record OpenConflict(GitPath Path, int? Blocks);
