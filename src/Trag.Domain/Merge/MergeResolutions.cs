namespace Trag.Domain.Merge;

// What a merge in progress has decided beyond the index status: the conflicts already resolved and how, and how much
// of each open conflict is left. Read with every snapshot while a merge, a rebase, a revert or a cherry-pick is in
// progress; empty otherwise.
public sealed record MergeResolutions(IReadOnlyList<ResolvedPath> Resolved, IReadOnlyList<OpenConflict> Open)
{
    public static readonly MergeResolutions None = new([], []);

    // How a resolved conflict was resolved, by the blob staged for it against each side's: a side's own blob is that side;
    // nothing staged keeps the side that had no file, a deletion; anything else is the user's edit.
    public static Resolution How(string? mine, string? theirs, string? staged) => staged is null
        ? mine is null ? Resolution.Mine : theirs is null ? Resolution.Theirs : Resolution.Edited
        : staged == mine ? Resolution.Mine : staged == theirs ? Resolution.Theirs : Resolution.Edited;
}
