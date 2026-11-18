using Trag.Domain.Status;

namespace Trag.Infrastructure.Git.Parsing;

// Everything one git status --porcelain=v2 -z run said: the branch header, when --branch was asked for, and the files.
// "(detached)" and "(initial)" have already been read as the absence of a branch and of a commit.
internal sealed record StatusRecords(string? Branch, string? Head, string? Upstream, bool HasHeader, IReadOnlyList<FileStatus> Files)
{
    // A whole-repository read asks for the header and cannot do without it; a read of a few paths never has one.
    public (string? Branch, string? Head, string? Upstream) Header() =>
        HasHeader ? (Branch, Head, Upstream) : throw new FormatException("Missing branch metadata.");
}
