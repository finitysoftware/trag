using Trag.Domain.Paths;
using Trag.Domain.Writes;

namespace Trag.Domain.Presentation.Text;

// What resetting the current branch to one of its commits says: the question, the modes' consequences, what keeps a
// reset back, and how it ended.
public static class ResetText
{
    public const string CommitGone = "The commit to reset to is no longer available. Refresh and choose it again.";
    public const string Failed = "Git did not reset the branch. See the details below.";
    public const string NotVerified = "The reset could not be verified. Review the branch and the working tree before continuing.";
    public const string Interrupted = "The reset was interrupted before anything changed.";

    public const string NeedsBranch = "A reset moves a branch back. Switch to a branch first.";
    public static string DistanceUnreadable(string branch) => "How far " + branch + " would go back could not be read. Refresh and try again.";
    public static string HeadMoved(string branch) => branch + " moved since this question was asked, so the commits that would leave it are not the ones counted. Close it and choose the commit again.";
    public static string Title(string branch, string commit) => "Reset " + branch + " to " + commit + "?";
    public static string AlreadyHere(string branch, string commit) => branch + " is already at " + commit + ".";
    public static string NotOnBranch(string commit, string branch) =>
        "Commit " + commit + " is not in the history of " + branch + ". A reset moves " + branch + " back to one of its own commits.";

    public static string Summary(string branch, int distance, string from, string to) =>
        branch + " moves back " + Counted.Of(distance, "commit") + ", from " + from + " to " + to + ".";

    public static string Pushed(int count, string upstream) => count == 1
        ? "1 of these commits is already on " + upstream + "; a Pull would bring it back."
        : Counted.Number(count) + " of these commits are already on " + upstream + "; a Pull would bring them back.";

    // overwritten: the untracked files the commit's files take the place of, three by name and the rest counted.
    public static string HardLoss(int changedFiles, IReadOnlyList<GitPath> overwritten) => changedFiles switch
    {
        0 => "No uncommitted change is lost. ",
        1 => "1 changed file loses its uncommitted changes for good. ",
        _ => Counted.Of(changedFiles, "changed file") + " lose their uncommitted changes for good. "
    } + overwritten switch
    {
        [] => "Untracked files stay.",
        [var one] => "The untracked file " + one.DisplayEscaped + " is lost for good: the commit has a file in its place. Other untracked files stay.",
        _ => Counted.Number(overwritten.Count) + " untracked files are lost for good, as the commit has files in their place: "
            + string.Join(", ", overwritten.Take(3).Select(path => path.DisplayEscaped))
            + (overwritten.Count > 3 ? " and " + Counted.Number(overwritten.Count - 3) + " more" : "") + ". Other untracked files stay."
    };

    public static string Done(string branch, string commit, ResetMode mode) => branch + " reset to " + commit + mode switch
    {
        ResetMode.Soft => "; the changes after it are staged.",
        ResetMode.Mixed => "; the changes after it are in the working tree.",
        _ => "; the working tree matches it."
    };
}
