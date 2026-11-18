using Trag.Domain.Diff;

namespace Trag.Domain.History;

public sealed record CommitDetails(HistoryCommit Commit, string Message, IReadOnlyList<FileDiffMetadata> Files, string FileNotice = "")
{
    public IReadOnlyList<string> Parents => Commit.Parents;
    public CommitSignature Signature { get; init; } = CommitSignature.None;
    public bool IsShallowBoundary { get; init; }
}
