using Trag.Domain.Accounts;
using Trag.Domain.Branches;
using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// What the branch picker, its dialogs and the go-to-commit flow say: the group header, the delete question for a local
// or a remote branch, the tooltip with the upstream, the create dialog's subtitle, and why a move does nothing.
public static class BranchText
{
    public const string AlreadyOnCommit = "You are already on this commit.";
    public const string ChooseAgain = "Repository changed. Cancel and choose the branch again.";
    public const string MovedWhileReading = "The repository changed while its branches were read. Double-click the commit again.";
    // The commits of a detached HEAD were kept on a branch, then the move after it ended as its outcome says.
    public static string Kept(string branch, string outcome) => "The commits are kept on " + branch + ". " + outcome;
    // The branch list holds as many branches as Git is asked for.
    public static readonly string ListCut = "Showing the first " + Counted.Grouped(GitLimits.BranchCount) + " branches.";

    public static string GroupTitle(string remote) => "REMOTE · " + remote;
    // A local branch's menu starts the provider's own request on its page: a pull request on GitHub, a merge request on GitLab.
    public const string CreatePullRequest = "Create pull request";
    public const string CreateMergeRequest = "Create merge request";
    public static string CreateRequest(AccountProvider? provider) => provider == AccountProvider.GitLab ? CreateMergeRequest : CreatePullRequest;
    public static string AlreadyOn(string branch) => "You are already on " + branch + ".";
    // Why a dialog waits for uncommitted changes: the go-to, the merge and the rebase dialog say it the same way.
    public const string ChangesInTheWay = "Commit or stash your changes first";

    public static string DeleteTitle(BranchInfo branch) => branch.IsRemote ? "Delete remote branch" : "Delete branch";
    // The rename dialog says what moves with the name, and what stays.
    public static string RenameSubtitle(string branch) => "Give " + branch + " a new name. Its commits and its upstream stay as they are.";

    public static string DeleteSubtitle(BranchInfo branch) => branch.IsRemote
        ? "Remove " + branch.Name + " from " + (branch.Remote ?? "the remote") + "."
        : "Remove the local branch " + branch.Name + ".";

    public static string DeleteText(BranchInfo branch) => branch.IsRemote
        ? "The branch is deleted on " + (branch.Remote ?? "the remote") + " for everyone who uses it. Local branches that track it keep their commits."
        : branch.Name + " is not fully merged. Removing it discards commits that exist on no other branch; Git keeps them in the reflog for a while.";

    public static string Tooltip(BranchInfo branch) => branch.IsRemote ? branch.Ref
        : branch.Upstream is { } upstream ? branch.Name + " · tracks " + RefNames.RemoteTrackingName(upstream) : branch.Name;

    public static string MergeTip(BranchInfo branch) => "Merge " + branch.Name + " into the current branch";

    // The start point in the create dialog that is the current HEAD, on its branch.
    public static string StartAtHead(string? branch) => (branch ?? "HEAD") + " · current HEAD";

    public static string CreateSubtitle(StartPoint? start) => start?.Ref is null
        ? "Create a local branch from the current branch."
        : "Create a local branch from " + start.Display + ".";
}
