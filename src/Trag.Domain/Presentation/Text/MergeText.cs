using Trag.Domain.Merge;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What merges and the conflict views say: why Trag cannot continue a merge; the sides, their actions and the abort
// question; how a conflicted or a decided file is described and resolved; and what a merge the dialog started adds to
// Git's own result (the push, the way back).
public static class MergeText
{
    // Why Trag cannot continue the merge in progress; nothing without a merge.
    public static string Unsupported(RepositorySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.Operation != RepositoryOperation.Merge) return "";
        if (snapshot.Limitations.FirstOrDefault() is { } limitation) return limitation;
        if (snapshot.Merge?.Limitation is { Length: > 0 } reason) return reason;
        return "The original HEAD or repository state changed. Continue this merge in your Git tools.";
    }

    // A merge conflict's sides, mine (the branch checked out) and theirs, and what brings the conflict; a side's card
    // title, and the verb for the side a resolution used: a version already in HEAD is kept, the other one is taken.
    // The title of the merge's view and of its place in the sidebar, and of the view of conflicts no operation left.
    public const string InProgress = "Merge in progress";
    public const string ConflictResolution = "Conflict resolution";
    public const string MineWord = "mine";
    public const string TheirsWord = "theirs";
    public const string Change = "the merge";
    // The incoming side of a merge whose message names no branch and whose commit Git did not record.
    public const string IncomingCommit = "the incoming commit";
    // The sides of conflicts a stash left: the stash is the user's own work, the current branch the other side.
    public const string StashWord = "stash";
    public const string StashName = "the stash";
    public const string CurrentWord = "current";
    public static string SideTitle(string label, string name) => label + " · " + name;
    public static string SideVerb(bool kept) => kept ? "kept" : "took";
    // The conflict view's actions on a side: the whole file, and one block.
    public static string UseSide(string word) => "Use " + word;
    public static string UseSideTip(string word) => "Take " + word + " for the whole file, and stage it";
    public static string TakeSide(string word) => "Take " + word;
    public const string MarkersIncomplete = "The conflict markers in this file are incomplete. Open it in the editor, or use a side.";
    public const string NoBlockLeft = "No conflict block is left in this file. Mark it as resolved when its content is right.";

    // The question before the merge in progress is aborted.
    public const string AbortTitle = "Abort this merge?";
    public const string AbortQuestion = "Git will try to restore the pre-merge state. Uncommitted edits can be discarded. Save any content you want to keep elsewhere before continuing. If Git cannot abort, Trag will show the remaining state for review.";

    public const string DialogTitle = "Merge branches";
    public const string DialogSubtitle = "Bring the commits of one branch into another.";
    public const string MarkResolvedWaits = "Mark as resolved is available once no conflict block is left. Choose a side for each block, or edit the file.";
    public const string NotText = "This file cannot be shown as text. Use a side, the editor or the merge tool, then mark it as resolved.";

    public static string BlockTitle(int index, int count) => "Conflict " + Counted.Number(index + 1) + " of " + Counted.Number(count);
    public static string Blocks(int? blocks) => blocks switch { null => "", 0 => "no blocks left", int count => Counted.Of(count, "block") };

    // Sentences name the sides the way the view does: mine and theirs in a merge, yours and upstream in a rebase.
    public static string ConflictLine(MergeConflict conflict, ConflictSides sides) => conflict switch
    {
        { MissingOnMine: true, MissingOnTheirs: true } => "Conflict · " + sides.Ours.Short + " and " + sides.Theirs.Short + " both deleted this file",
        { DeletedByMine: true } => "Conflict · " + sides.Ours.Short + " deleted this file, " + sides.Theirs.Short + " changed it",
        { DeletedByTheirs: true } => "Conflict · " + sides.Theirs.Short + " deleted this file, " + sides.Ours.Short + " changed it",
        { MissingOnMine: true } => "Conflict · " + sides.Theirs.Short + " added this file, " + sides.Ours.Short + " has none",
        { MissingOnTheirs: true } => "Conflict · " + sides.Ours.Short + " added this file, " + sides.Theirs.Short + " has none",
        { Blocks: null } => "Conflict · not shown as text",
        _ => "Conflict · " + Blocks(conflict.Blocks) + (conflict.Blocks == 0 ? "" : " left")
            + " · " + sides.Yours.Word + " = " + sides.Yours.Short + " · " + sides.Other.Word + " = " + sides.Other.Short
    };

    // A file one side has none of has no blocks: the choice is whether it stays, and either side deletes it when both
    // have none.
    public static string DeletionChoice(MergeConflict conflict, ConflictSides sides)
    {
        if (conflict is { MissingOnMine: true, MissingOnTheirs: true })
            return "Use " + sides.Yours.Word + " or " + sides.Other.Word + " to delete the file, as both sides did.";
        var deleter = conflict.MissingOnMine ? sides.Ours : sides.Theirs;
        var reason = conflict.DeletedByMine || conflict.DeletedByTheirs ? " did" : " has none";
        string Choice(ConflictSide side) => "Use " + side.Word + (side == deleter ? " to delete the file, as " + side.Short + reason : " to keep the version from " + side.Short);
        return Choice(sides.Yours) + ", or " + Choice(sides.Other) + ".";
    }

    public static string Tag(MergeResolved file, ConflictSides sides) => sides.KindOf(file) switch
    {
        ResolvedKind.Markers => "markers left",
        ResolvedKind.Yours => sides.Yours.Word,
        ResolvedKind.Other => sides.Other.Word,
        ResolvedKind.Edited => "edited",
        _ => "auto"
    };

    public static string ResolvedTitle(MergeResolved file, ConflictSides sides) => sides.KindOf(file) switch
    {
        ResolvedKind.Markers => "Marked resolved · still has conflict markers",
        ResolvedKind.Yours => "Resolved · " + sides.Yours.Verb + " " + sides.Yours.Word,
        ResolvedKind.Other => "Resolved · " + sides.Other.Verb + " " + sides.Other.Word,
        ResolvedKind.Edited => "Resolved · edited",
        _ => "Merged automatically"
    };

    public static string ResolvedLine(MergeResolved file, ConflictSides sides)
    {
        if (file.HasMarkers) return "Staged with its conflict markers still in the file. Mark it as unresolved and resolve it again.";
        var (change, on) = (sides.Change, sides.Base);
        if (file.Staged is null) return char.ToUpperInvariant(change[0]) + change[1..] + " leaves this file as it is on " + on + ".";
        if (file.Staged.Index == 'D') return "Resolved by deleting the file. The diff shows what " + change + " removes from " + on + ".";
        var diff = " The diff shows what " + change + " changes on " + on + ".";
        return sides.Of(file.How) is { } side
            ? (side == sides.Yours ? "Resolved with your side." : "Resolved with the whole file from " + side.Short + ".") + diff
            : (file.How == Resolution.Edited ? "Resolved with your own edit." : "Git merged this file on its own.") + diff;
    }

    // What marking a file resolved, or taking a side for it, ends with: staged, or the same as on the base and so with
    // nothing to stage (ResolutionOutcome).
    public static string Resolved(string path) => path + " is resolved";
    public static string Staged(string sentence, bool marked) => sentence + (marked ? " and staged." : ".");
    public static string NothingToStage(string sentence, string onBase) => sentence + ". It is the same as on " + onBase + ", so there is nothing to stage for it.";

    // The files staged with markers still in them, three by name and the rest counted.
    public static string MarkersText(IReadOnlyList<MergeResolved> files) => files switch
    {
        [] => "",
        [var one] => one.Path.DisplayEscaped + " still contains <<<<<<< / >>>>>>> lines.",
        _ => Counted.Number(files.Count) + " files still contain <<<<<<< / >>>>>>> lines: " + string.Join(", ", files.Take(3).Select(file => file.Path.DisplayEscaped))
            + (files.Count > 3 ? " and " + Counted.Number(files.Count - 3) + " more" : "") + "."
    };

    public static string ReopenQuestion(MergeResolved file) =>
        "Bring the conflict back in " + file.Path.DisplayEscaped + "? Your edits to this file are replaced by the conflicted content.";

    public static string Pushed(string branch) => "Pushed " + branch + " to its upstream.";
    public static string NotPushed(string branch, string? reason) => reason is null
        ? branch + " has no upstream Trag can push to; publish or push it from the sidebar."
        : "The push of " + branch + " did not complete: " + reason;
    public static string SwitchedBack(string branch) => "Back on " + branch + ".";
    public static string NotSwitchedBack(string branch, string? reason) => "Trag could not switch back to " + branch + (reason is null ? "." : ": " + reason);
    public static string StaysForConflicts(string branch) => "You stay on " + branch + " until the merge is finished or aborted.";

    // Why a conflicted file cannot be shown as text in the merge view.
    public const string NotTextPath = "This path contains non-UTF-8 bytes, so Trag cannot name it to Git. Use your Git tools for this file.";
    public const string DeletedOnASide = "One side deleted this file. Use a side to keep it or to delete it.";
    public const string SubmoduleConflict = "Each side records its own commit of this submodule. Use a side to keep that commit; Update submodules then checks it out.";
    public static readonly string TooLarge = "This file is larger than " + Counted.Megabytes(ConflictText.ByteLimit) + ". Use a side, the editor or the merge tool.";

    // Why a merge in progress can only be finished in the user's Git tools.
    public const string UnsupportedMerge = "Only a merge with one incoming commit and a valid original HEAD can be continued in Trag. Use your Git tools.";
    public const string MergeAutostash = "This merge uses an autostash. Continue or abort it in your Git tools.";
}
