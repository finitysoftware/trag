using Trag.Domain.Errors;
using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// What the status bar says about the session's repository: before one is open, while one opens or refreshes, when that
// is canceled or fails and why, when it closes, and when its location could not be restored, remembered or cleared.
public static class SessionText
{
    public const string Ready = "Ready · Open a local repository to begin";
    public const string NotRestored = "The last repository could not be restored. Open a folder to continue.";
    public const string NotRemembered = "Repository opened, but its location could not be remembered.";
    public const string Closed = "Repository closed";
    public const string NotForgotten = "Repository closed, but the saved location could not be cleared.";

    public static string Reading(bool refresh) => refresh ? "Refreshing repository…" : "Opening repository…";
    // A refresh keeps the repository shown before it; an open has none to keep.
    public static string Canceled(bool shown) => shown
        ? "Refresh canceled · showing the previous status"
        : "Opening canceled · choose Refresh to retry";
    public static string Failed(bool shown) => shown
        ? "Refresh failed · showing the previous status"
        : "Repository could not be opened";

    public static string Error(RepositoryError? error) => error switch
    {
        RepositoryError.PathUnavailable => "This folder is no longer available. Open an existing local folder.",
        RepositoryError.NotRepository => "This folder is not a Git repository. Choose an existing working copy.",
        RepositoryError.UnsafeDirectory => "Git does not trust this folder's ownership. Review it in your Git tools, then refresh.",
        RepositoryError.BareRepository => "Bare repositories have no working tree and are not supported. Open a working copy.",
        RepositoryError.GitMissing => "Git could not be found. Install Git " + GitLimits.MinimumGit.ToString(2) + " or newer, or choose the git.exe you have in Settings → Git.",
        RepositoryError.GitTooOld => "This Git version is too old. Install Git " + GitLimits.MinimumGit.ToString(2) + " or newer, or choose a newer git.exe in Settings → Git.",
        RepositoryError.InvalidGitVersion => "The Git version could not be recognized. Check your Git installation.",
        RepositoryError.OutputTooLarge => "This repository returned too much data to display safely. Narrow its untracked files using your Git tools, then refresh.",
        RepositoryError.ChangedDuringRead => "The repository changed while it was being read. Wait for the other Git operation, then refresh.",
        _ => "Git could not read this repository. Check folder access and Git configuration, then refresh."
    };
}
