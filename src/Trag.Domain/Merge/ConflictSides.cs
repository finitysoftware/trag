using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// The two sides of a conflict as the user sees them: their own work first, then the other side. A merge's own side is
// Git's ours, the branch checked out; a rebase replays the user's commit as Git's theirs onto the upstream, which is
// Git's ours. A stash applied over changed files is the user's work as Git's theirs, on the current branch as Git's ours.
// A revert or a cherry-pick brings the commit's undoing or its changes as Git's theirs onto the current branch, the
// user's work, as Git's ours.
// Change names what brings the conflict ("the merge", "this commit", "the stash") and Base where it lands.
public sealed record ConflictSides(ConflictSide Yours, ConflictSide Other, string Change, string Base)
{
    public ConflictSide Ours => Yours.Git == ConflictChoice.Mine ? Yours : Other;
    public ConflictSide Theirs => Yours.Git == ConflictChoice.Mine ? Other : Yours;

    // The side a resolution took, in Git's terms: Mine is ours, Theirs is theirs.
    public ConflictSide? Of(Resolution how) => how switch { Resolution.Mine => Ours, Resolution.Theirs => Theirs, _ => null };
    public ResolvedKind KindOf(MergeResolved file) => file.HasMarkers ? ResolvedKind.Markers
        : Of(file.How) is { } side ? (side == Yours ? ResolvedKind.Yours : ResolvedKind.Other)
        : file.How == Resolution.Edited ? ResolvedKind.Edited : ResolvedKind.Automatic;

    // A conflict block's lines in the order the cards show them: yours, then the other side.
    public (IReadOnlyList<ConflictLine> Yours, IReadOnlyList<ConflictLine> Other) Arrange(ConflictPart part) =>
        Yours.Git == ConflictChoice.Mine ? (part.Mine ?? [], part.Theirs ?? []) : (part.Theirs ?? [], part.Mine ?? []);

    public static ConflictSides For(RepositorySnapshot? snapshot)
    {
        if (snapshot is { Operation: RepositoryOperation.Rebase, Rebase: { } rebase })
        {
            var step = RebaseText.Step(rebase.Step, rebase.Total);
            var stop = rebase.Current is { } current ? RebaseText.Stop(step, current.ShortId, current.Subject) : step;
            var upstream = RebaseText.Upstream(rebase.OntoText, rebase.Applied);
            return new(new(RebaseText.YoursWord, stop, step, ConflictChoice.Theirs),
                new(RebaseText.UpstreamWord, upstream, rebase.OntoText, ConflictChoice.Mine), RebaseText.Change, rebase.OntoText);
        }
        var branch = snapshot?.Branch ?? "";
        if (snapshot is { Operation: RepositoryOperation.Revert or RepositoryOperation.CherryPick, PickHead: { } picked })
        {
            var operation = snapshot.Operation;
            var pick = PickText.SideName(operation, GitObjectId.Short(picked));
            return new(new(MergeText.CurrentWord, branch, branch, ConflictChoice.Mine),
                new(PickText.Name(operation), pick, pick, ConflictChoice.Theirs), PickText.Change(operation), branch);
        }
        if (snapshot is { Operation: RepositoryOperation.Conflict })
            return new(new(MergeText.StashWord, MergeText.StashName, MergeText.StashName, ConflictChoice.Theirs),
                new(MergeText.CurrentWord, branch, branch, ConflictChoice.Mine), MergeText.StashName, branch);
        var incoming = MergeMessages.IncomingName(snapshot?.Merge);
        return new(new(MergeText.MineWord, branch, branch, ConflictChoice.Mine),
            new(MergeText.TheirsWord, incoming, incoming, ConflictChoice.Theirs), MergeText.Change, branch);
    }
}
