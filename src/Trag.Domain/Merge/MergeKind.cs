using Trag.Domain.Presentation.Text;

namespace Trag.Domain.Merge;

// A merge per kind: the operation it names, the reflog action that makes Trag's own merges recognisable afterwards, and
// what it says about how it ended.
public sealed record MergeKind(string Operation, string Reflog, MergeOutcomeText Text)
{
    public static readonly MergeKind Pull = new("pull", "trag-pull-", MergeOutcomeText.Pull);
    public static readonly MergeKind Branch = new("merge", "trag-merge-", MergeOutcomeText.Branch);
}
