namespace Trag.Domain.Presentation.Text;

// What publishing a branch says: why it is refused before Git starts, what it does meanwhile, what it had changed when
// it stopped, and how the push ended.
public static class PublishOutcomeText
{
    public const string RemoteNameInvalid = "Enter a valid remote name such as origin: no spaces and not starting with '-'.";
    public const string TargetNameInvalid = "Enter a valid name for the branch on the remote: no spaces, no '..' and not starting with '-'.";
    public const string BranchChanged = "The current branch changed. Refresh and open Publish again.";
    public const string CommitNeeded = "Create a commit before pushing. You can still add the remote without pushing.";
    public const string RemoteGone = "This remote no longer exists. Refresh and choose another remote.";
    public static string RemoteTaken(string remote, string? branch) => "A remote named " + remote + " already exists. Choose another name, or publish " + branch + " to that remote instead.";
    public const string AddressMissing = "Enter the remote address, for example https://github.com/you/repository.git.";
    public const string SignInNeeded = "Sign in to a GitHub or GitLab account in Settings to create the repository from Trag, or use an existing remote URL.";

    // What publishing does meanwhile, and what it had already changed when it stopped.
    public static string Adding(string remote) => "Adding remote " + remote + "…";
    public static string Creating(string repository) => "Creating " + repository + " on the provider…";
    public static string Recording(string upstream) => "Recording " + upstream + " as the upstream…";
    public static string Pushing(string? branch, string upstream) => "Pushing " + branch + " to " + upstream + "…";
    public static string RemoteAdded(string remote) => "Remote " + remote + " was added.";
    public static string CreatedAndAdded(string repository, string remote) => repository + " was created and added as " + remote + ".";
    public static string CreatedOnProvider(string repository) => repository + " was created on the provider.";
    public static string NowTracks(string? branch, string upstream) => branch + " now tracks " + upstream + ".";

    public static string NotPushed(string changed) => changed + " Use Push to send your commits.";
    public const string Canceled = "Publishing canceled. Nothing was changed.";
    public const string CanceledBeforePush = "Publishing was canceled before the push. Use Push to send your commits.";
    public static string CanceledBeforeRemote(string address, string remote) =>
        "Publishing was canceled before the remote was added. Add " + address + " as " + remote + " in Remotes, then push.";
    public const string NotFinished = "Publishing could not finish. Check the repository, the address, your network and Git credentials, then refresh.";
    public const string RemoteNotAdded = "Git could not add the remote. Check the address and the repository configuration.";
    public static string CreatedButNotAdded(string repository, string address, string remote) =>
        repository + " was created, but Git could not add the remote. Add " + address + " as " + remote + " in your Git tools.";
    public const string UpstreamNotRecorded = "The upstream could not be recorded. Check the repository configuration lock, then choose the upstream branch in the sidebar.";
    public const string ConfigurationUnreadable = "The remote configuration could not be read. Review it in your Git tools.";
    public static string NotVerified(string? explanation) => explanation is null
        ? "The push result could not be verified. Fetch to check the remote before retrying."
        : "The push result could not be verified: " + explanation + " Fetch to check the remote before retrying.";
    public static string NotStarted(string reason) => "The push did not start: " + reason + " Use Push to retry.";
    public static string Published(string? branch, string destination, string upstream) => "Published " + branch + " to " + destination + ". " + upstream + " is now its upstream.";
    public const string Interrupted = "The push was interrupted before the destination received the branch. Use Push to retry.";
    public static string NotCompleted(string explanation) => "The push did not complete: " + explanation + " Use Push to retry.";
    public static string DifferentTarget(string target) => "The destination already holds a different " + target + ". Fetch and review it before pushing.";
}
