using Trag.Domain.Diff;
using Trag.Domain.Merge;
using Trag.Domain.Status;

namespace Trag.Application.Ports.Git;

public interface IFileDiffReader
{
    Task<FileDiff> ReadAsync(string repository, FileStatus file, bool fromIndex, bool preview, CancellationToken ct);
    // A conflicted working file as text, whole, for the merge view's conflict blocks.
    Task<ConflictText> ReadConflictAsync(string repository, FileStatus file, CancellationToken ct);
}
