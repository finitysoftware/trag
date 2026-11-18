using Trag.Domain.Status;

namespace Trag.Domain.Merge;

// A file of the merge still in conflict, with the number of conflict blocks left in it (null: not readable as text). When
// one side has no such file, the conflict is whether to keep it: Git marks "deleted by us" in the index column and
// "deleted by them" in the working tree column, and a file renamed apart on both sides "added by us" (AU) or "added by
// them" (UA) under each new name, and "deleted by both" (DD) under the old one.
public sealed record MergeConflict(FileStatus File, int? Blocks)
{
    public bool DeletedByMine => File.Index == 'D';
    public bool DeletedByTheirs => File.Worktree == 'D';
    public bool MissingOnMine => DeletedByMine || File is { Index: 'U', Worktree: 'A' };
    public bool MissingOnTheirs => DeletedByTheirs || File is { Index: 'A', Worktree: 'U' };
    public bool IsDeletion => MissingOnMine || MissingOnTheirs;
    // A side deleted the file: a keep-or-delete question, never blocks of text.
    public static bool DeletesOnASide(FileStatus file) => file.Index == 'D' || file.Worktree == 'D';
    // Marking the file as resolved waits for its last block; a file that is not text has no blocks to wait for.
    public bool CanMarkResolved => Blocks is null or 0;
    // A path that is not its own UTF-8 cannot be named to Git as text nor handed to a tool; only Git's tools resolve it.
    public bool TakesSides => File.Path.IsDisplayable;
    // Git's merge tool merges a file both sides have; for any other conflict it asks on its input which side to keep.
    public bool TakesMergeTool => !IsDeletion && !File.IsSubmodule;
}
