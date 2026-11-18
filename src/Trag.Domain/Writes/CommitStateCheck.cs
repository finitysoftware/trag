using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Writes;

// AmendedParents are the parents of the commit an amend replaces, which its replacement keeps; null for a new commit.
public sealed record CommitStateCheck(RepositorySnapshot Before, string? Head, string? Branch, string? AmendedParents = null)
{
    public string Parents => AmendedParents ?? (Before.Merge is { } merge ? Before.Head + " " + merge.IncomingHead : Before.Head ?? "");
    public bool HasMoved => Head != Before.Head || Branch != Before.Branch;
    public bool CanUseHead(int? exitCode) => exitCode == 0 && Head != Before.Head && Branch == Before.Branch;

    // tree: the index's tree as Git wrote it, null when Git could not.
    public bool MatchesIndex((string Parent, string Tree) commit, string? tree) => tree is not null && commit.Parent == Parents && commit.Tree == tree;

    public bool MatchesCommit(string committed) => Head == committed && Branch == Before.Branch;

    public string SuccessMessage(bool canceled)
    {
        if (canceled) return CommitOutcomeText.CreatedAfterCancel;
        if (AmendedParents is not null) return CommitOutcomeText.Amended;
        return Before.Merge is null ? CommitOutcomeText.Created : CommitOutcomeText.MergeCreated;
    }

    public string Refusal(string details) => CommitSignings.Refused(Before.Signing, details) ? CommitOutcomeText.NotSigned(Before.Signing) : CommitOutcomeText.Rejected;
}
