using Trag.Domain.Merge;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Writes;

// Marking a file resolved, or taking one side for it, can leave the file exactly as it is on the branch. Then it has
// nothing to stage and leaves the file status without a row, so the result says why; every other result keeps its own.
public static class ResolutionOutcome
{
    public static GitWriteResult Described(GitWriteRequest request, GitWriteResult result)
    {
        if (result is not { Outcome: GitWriteOutcome.Succeeded, Snapshot: { } after }) return result;
        var (path, sentence) = request switch
        {
            StageRequest { Resolution: true, Paths: [var one] } => (one, MergeText.Resolved(one.DisplayEscaped)),
            ConflictSideRequest side => (side.Path, result.Message.TrimEnd('.')),
            _ => (null, "")
        };
        if (path is null) return result;
        var staged = after.Files.Any(file => file.IsStaged && file.Path.IsSame(path));
        return result with { Message = staged ? MergeText.Staged(sentence, request is StageRequest) : MergeText.NothingToStage(sentence, ConflictSides.For(after).Base) };
    }
}
