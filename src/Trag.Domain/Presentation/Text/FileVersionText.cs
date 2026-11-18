namespace Trag.Domain.Presentation.Text;

// What taking a commit's version of a file says: saved where the user chose, restored into the working tree, or the
// question first when the file's own changes would go; and what undoing a commit's changes to a file says.
public static class FileVersionText
{
    public const string SaveTitle = "Save this version";
    public static string Saved(string path, string commit, string destination) => "Saved " + path + " from " + commit + " as " + destination + ".";
    public static string NotSaved(string path) => path + " could not be saved.";
    public static string Restored(string path, string commit) => "Restored " + path + " from " + commit + ".";
    public static string AlreadyRestored(string path, string commit) => path + " already has the content it has in " + commit + "; nothing changed.";
    public const string NotRestored = "The file could not be restored.";
    public static string FilesInTheWay(string paths) => "Files with changes are where the restored file goes: " + paths
        + ". Git would delete them; move or delete them first.";
    public const string CommitGone = "The commit to restore from is no longer available. Refresh and choose it again.";
    public static string Reverted(string path, string commit) => "Undid the changes " + commit + " made to " + path + ".";
    public const string NotReverted = "The changes could not be undone: the file has changed in the same lines since.";
    public static string AlreadyReverted(string path, string commit) => path + " no longer has the changes " + commit + " made; nothing changed.";
    public static string ExecutableBitNotKept(string path, string commit) => commit + " only set or cleared the executable bit of " + path
        + ", which this repository does not keep in the working tree (core.fileMode is false); nothing changed.";
    public static string FolderInTheWay(string path) => path + " is a folder now, so the changes could not be undone. Move or rename the folder first.";
    public const string RevertCommitGone = "The commit whose changes to undo is no longer available. Refresh and choose it again.";
    public const string NameNotText = "This file's name is not UTF-8 text, so Git cannot be given it.";
    public static string RestoreQuestion(string path, string commitId) =>
        path + " has changes that are not committed. Restoring it from " + GitObjectId.Short(commitId) + " replaces them.";
}
