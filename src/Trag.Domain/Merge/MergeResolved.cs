using Trag.Domain.Paths;
using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// A file of the merge whose content is decided: how, whether conflict markers were staged with it, its staged change
// against our branch (none when the merge leaves the file as it was on our side), and whether Git can bring its conflict
// back.
public sealed record MergeResolved(GitPath Path, Resolution How, bool HasMarkers, FileStatus? Staged, bool Reopens = true)
{
    // Only a conflict that was resolved can be opened again; what Git merged on its own never was one.
    public bool CanReopen => How != Resolution.Automatic && Reopens;
}
