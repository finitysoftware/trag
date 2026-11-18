using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What the file panes say about a row and its actions: which side it is on and the version its diff is read from, the
// word for its status letter (a conflict is unresolved until its resolution is staged), and the hunk, line and unstage
// actions.
public static class StatusText
{
    public static string Source(bool fromIndex) => fromIndex ? "Staged · index" : "Unstaged · working tree";
    // The side a diff of the row is read from.
    public static string Version(bool fromIndex) => fromIndex ? "index" : "working tree";
    public static string HunkAction(bool fromIndex) => fromIndex ? "Unstage hunk" : "Stage hunk";
    public const string DiscardHunk = "Discard hunk";
    public const string UnstageTip = "Remove selected whole files from the index; keep working files";
    public static string LinesAction(bool fromIndex) => fromIndex ? "Unstage lines" : "Stage lines";
    public static string NotInWorkingTree(string path) => path + " is not in the working tree.";
    public const string DiscardLines = "Discard lines";
    public static string OriginalPath(FileStatus file) => file.OriginalPath?.DisplayEscaped ?? "—";
    public static string Code(FileStatus file, bool fromIndex) => file.IsConflict ? "!" : (fromIndex ? file.Index : file.Worktree).ToString();
    public static string Description(FileStatus file, bool fromIndex) =>
        file.IsConflict ? "Unresolved conflict" : Word(fromIndex ? file.Index : file.Worktree);

    // The word for one of Git's status letters, as the file panes show it.
    public static string Word(char code) => code switch
    {
        'M' => "Modified", 'A' => "Added", 'D' => "Deleted", 'R' => "Renamed", 'C' => "Copied",
        'T' => "Type changed", '?' => "Untracked", '.' => "Unchanged", '!' => "Ignored", _ => "Changed"
    };

    // The status line: how many files are on each side, and the conflicts on their own.
    public static string Line(RepositorySnapshot snapshot)
    {
        if (snapshot.Files.Count == 0) return "Working tree clean";
        var line = Counted.Number(snapshot.Files.Count(file => file.IsStaged)) + " staged · " + Counted.Number(snapshot.Files.Count(file => file.IsWorkingTreeChange)) + " unstaged";
        var conflicts = snapshot.Conflicts.Count();
        return conflicts == 0 ? line : line + " · " + Counted.Of(conflicts, "conflict");
    }
}
