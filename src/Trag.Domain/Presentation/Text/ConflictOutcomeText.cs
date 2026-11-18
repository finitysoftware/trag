using Trag.Domain.Merge;

namespace Trag.Domain.Presentation.Text;

// What a conflict write says: how a file or a block was resolved or reopened, why a choice was refused, and what the
// external tool and the editor a conflict opens in report.
public static class ConflictOutcomeText
{
    public static string ResolvedWith(string path, ConflictChoice side) => path + " resolved with " + Side(side) + ".";
    public static string ResolvedAsDeleted(string path, ConflictChoice side) => path + " resolved as deleted, as " + Side(side) + " had it.";
    public static string InConflictAgain(string path) => path + " is in conflict again.";
    public static string BlockUses(int block, int blocks, string path, ConflictChoice side) =>
        "Conflict " + Counted.Number(block) + " of " + Counted.Number(blocks) + " in " + path + " now uses " + Side(side) + ".";
    public static string MarkersLeft(string path) => path + " still contains conflict markers. Choose a side for each block or edit the file, then mark it as resolved.";

    public const string PathNotNameable = "This path cannot be named to Git as text. Use your Git tools for this file.";
    public const string SidesNotListed = "Git could not list the sides of this conflict. The original index is unchanged.";
    public const string SideNotTaken = "Git could not resolve the file with that side. The original index is unchanged.";
    public const string NotReopened = "Git could not bring the conflict back. Only a conflict resolved in this merge can be opened again.";
    public const string NotPlainFile = "The conflicted file is not a plain file any more. Refresh and look at it again.";
    public const string FileChanged = "The file changed since it was shown. Look at its conflicts again before choosing.";
    public const string ConflictsChanged = "The conflicts in this file changed. Look at them again before choosing.";

    // An external tool on one conflicted file.
    public const string ToolOnLink = "A symbolic link or junction cannot be opened as a conflict file. Use your Git tools.";
    public const string ToolWithoutFile = "This conflict has no working file. Restore the desired version or stage the deletion; a tool needs an existing file.";
    public const string ToolNotConfigured = "Configure Git merge.guitool or merge.tool before opening a merge tool.";
    public const string ToolNeedsTerminal = "The configured merge tool needs a terminal. Configure a graphical Git merge.guitool.";
    public const string ToolNotVerified = "The repository could not be verified after the tool returned. Review the working files and merge state before continuing.";
    public const string ToolStateChanged = "Repository state changed while the tool was open. Review the refreshed state before continuing.";
    public const string ToolInterrupted = "The tool was interrupted. Working-file edits are kept; review them before staging.";
    public const string ToolReturned = "The tool returned. Review the working file, then explicitly stage its resolution.";
    public const string ToolFailed = "The tool failed. Review any working-file edits and the Git tool configuration before retrying.";

    // The editor a conflicted file opens in.
    public const string EditorUnreadable = "Git could not read its editor configuration.";
    public const string EditorMissing = "Configure a graphical Git core.editor with its wait option (for example, code --wait), then try again.";
    public const string EditorNeedsTerminal = "The configured editor needs a terminal. Configure a graphical Git core.editor with its wait option.";

    private static string Side(ConflictChoice choice) => choice switch { ConflictChoice.Mine => "mine", ConflictChoice.Theirs => "theirs", _ => "both sides" };
}
