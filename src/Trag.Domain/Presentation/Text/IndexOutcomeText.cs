using Trag.Domain.Writes;

namespace Trag.Domain.Presentation.Text;

// What staging, unstaging and discarding say: how the index changed, and why it did not.
public static class IndexOutcomeText
{
    public const string NotUpdated = "Git could not update the selected files. The original index is unchanged.";
    public const string Staged = "Selected whole files staged. Review the staged result before committing.";
    public const string Unstaged = "Selected files unstaged; working files kept.";
    // A hunk, or chosen lines that are not a whole one, taken apart from the rest of the file, and why Git would not.
    public static string Taken(PatchAction action, bool lines) => (lines ? "Lines" : "Hunk") + action switch
    {
        PatchAction.Stage => " staged; the rest of the file is unchanged.",
        PatchAction.Unstage => " unstaged; the working file is kept.",
        _ => " discarded; the rest of the file and what is staged are kept."
    };
    public static string NotTaken(PatchAction action, bool lines) =>
        "Git could not " + (action == PatchAction.Discard ? "take " + Part(lines) + " out of the working file" : "apply " + Part(lines) + " to the index")
        + "; the file changed since its diff was shown. Refresh and try again.";
    private static string Part(bool lines) => lines ? "those lines" : "that hunk";
    public const string AttributesNotVerified = "Git could not verify the selected file attributes. Refresh before trying again.";
    public static string SubmoduleChangesKept(string paths) => "Submodules with changes of their own: " + paths + ". Open each to discard them there; nothing was discarded.";
    public const string LfsNotInstalled = "Git LFS is not set up for Git, so Git would stage these files themselves instead of their pointers. Install Git LFS if it is missing, run git lfs install, then stage them again.";
    public const string IndexNotVerified = "The updated index could not be verified. The original index is unchanged.";
    public static string SubmoduleChangesNotStaged(string paths) => "Only files inside these submodules changed: " + paths + ". Open each to stage and commit them there; the original index is unchanged.";
    public const string SubmodulesNotStaged ="A folder that is a repository of its own is staged only as a submodule .gitmodules lists. The original index is unchanged.";

    public const string NotRestored = "Git could not restore the selected files from the index. Nothing else was changed.";
    public static string Discarded(int files, bool untracked) => "Discarded changes in " + Counted.Of(files, "file") + "."
        + (untracked ? " Untracked files were deleted." : "") + " Staged content was kept.";
    public const string NameUnsafe = "A selected file has a name Trag cannot pass to Git safely. Discard it in a terminal or your file manager; nothing was discarded.";
    public static string UntrackedInTheWay(string paths) => "Untracked files are where a discarded file comes back: " + paths
        + ". Git would delete them; move or delete them first. Nothing was discarded.";
    public static string RepositoriesKept(string paths) => "Folders that are Git repositories of their own: " + paths + ". Trag does not delete a repository; nothing was discarded.";
    public const string UntrackedNotDeleted = "Git could not delete the selected untracked files. Tracked files were already restored.";
}
