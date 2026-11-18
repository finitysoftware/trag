using Trag.Domain.Rebase;

namespace Trag.Domain.Presentation.Text;

// What editing the current branch's commits says: what it edits, what each choice does, why a plan cannot run, what the
// upstream already has, and how it ended.
public static class RewriteText
{
    public const string NeedsBranch = "Commits are edited on a branch. Switch to a branch first.";
    public const string HasMerges = "These commits include a merge. Editing commits across a merge is not supported yet.";
    public const string Unchanged = "Nothing changes yet. Move a commit, or choose what happens to one.";
    public static string Title(string branch) => "Edit commits on " + branch;
    public static string NotOnBranch(string commit, string branch) =>
        "Commit " + commit + " is not in the history of " + branch + ". Only the current branch's own commits can be edited.";
    public static string ShallowStart(string commit) =>
        "The commit before " + commit + " is not in this shallow clone, so editing from here would start the branch anew without its older history. Choose a newer commit, or fetch the full history in Git.";
    public const string AllDropped = "Every commit would be dropped. To remove them all, reset the branch to the commit before them instead.";
    public const string FirstFolded = "The first commit kept cannot be squashed or fixed up: there is no commit before it to join.";
    public const string EmptyMessage = "A commit message cannot be empty.";
    public const string Unreadable = "The commits to edit could not be read. Refresh and try again.";
    public const string PlanStale = "The branch no longer has exactly the commits the plan lists. Refresh and try again.";
    public static string Rewritten(string branch) => "Edited the commits of " + branch + ".";
    public static string Subtitle(int count, string firstId) =>
        Counted.Of(count, "commit") + (count == 1 ? ": " + GitObjectId.Short(firstId) : ", from " + GitObjectId.Short(firstId) + " to the tip");

    // Every action, in the order the dialog offers them.
    public static readonly IReadOnlyList<RewriteChoice> Choices = [.. Enum.GetValues<RewriteAction>().Select(action => new RewriteChoice(action, Name(action), Hint(action)))];
    public static string Name(RewriteAction action) => action switch
    {
        RewriteAction.Reword => "Reword", RewriteAction.Squash => "Squash", RewriteAction.Fixup => "Fixup", RewriteAction.Drop => "Drop", _ => "Pick"
    };

    public static string Hint(RewriteAction action) => action switch
    {
        RewriteAction.Reword => "Keep the commit with a new message",
        RewriteAction.Squash => "Join it into the commit above, messages together",
        RewriteAction.Fixup => "Join it into the commit above, dropping its message",
        RewriteAction.Drop => "Leave the commit out",
        _ => "Keep the commit as it is"
    };

    public static string Pushed(int count, string upstream) => count == 1
        ? "1 of these commits is already on " + upstream + "; after the edit, a push will have to replace it there."
        : Counted.Number(count) + " of these commits are already on " + upstream + "; after the edit, a push will have to replace them there.";
}
