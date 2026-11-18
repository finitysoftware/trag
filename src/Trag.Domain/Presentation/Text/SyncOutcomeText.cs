using Trag.Domain.Naming;

namespace Trag.Domain.Presentation.Text;

// What a fetch, a pull or a push says: why it is refused before Git starts, what it does meanwhile, how it ended, and
// why a remote cannot be synchronized in Trag at all.
public static class SyncOutcomeText
{
    public const string TargetMissing = "Choose a remote and review its synchronization target.";
    public const string Checking = "Checking repository and synchronization target…";
    public const string CheckingRepository = "Checking the repository…";
    // A write that finds the repository changed, or in a state it does not handle; doing is what it was about to do.
    public static string StateUnsupported(string doing) => "The repository changed or has an unsupported state. Refresh and review before " + doing + ".";
    public const string ConfigurationChanged = "The remote configuration changed since it was last read. Review the synchronization target and retry.";
    public const string AccountNeedsReview = "The account selection changed or needs review. Choose and save the account before syncing.";
    public const string UpstreamMissing = "Choose an upstream branch for the current committed branch. No synchronization target is inferred.";
    public const string RemoteAhead = "The remote branch has commits this branch never had. Pull them first; nothing was sent.";
    public const string PullNeedsCleanTree = "Pull needs a clean working tree and index. Commit or handle your local changes in Git first.";
    public const string PullAfterRewrite = "This branch was rewritten after it was pushed, so a pull would merge its old commits back in. Force push it instead.";
    public const string Canceled = "Synchronization canceled. Fetched references may already have changed; status is being refreshed.";
    public static readonly string TimedOut = "The server sent nothing for " + Counted.Number(GitLimits.TransferQuietMinutes)
        + " minutes, so Trag stopped waiting. Check the connection or the sign-in, then try again.";
    public const string NotFinished = "Synchronization could not finish. Check the repository, remote configuration, network and existing Git credentials, then refresh.";

    // The account a remote uses, and what a transfer needs to ask Git for credentials through Trag.
    public const string AccountSelectionUnavailable = "Account selection is unavailable in this session.";
    public const string AccountSelectionSaved = "Account selection saved for this remote. Your Git commit author is unchanged.";
    public const string AccountChangedDuringSync = "Account selection changed during synchronization. Review it before retrying.";
    public const string AskpassMissing = "The Trag askpass executable is unavailable. Restart the published application.";

    public const string Fetching = "Fetching branch references…";
    public const string Fetched = "Fetch completed. Local branch references are up to date for this fetch.";
    public const string FetchedUpstreamGone = "Fetch completed, but the selected upstream branch is unavailable. Choose another branch.";
    public const string UpstreamDeleted = "The upstream branch is no longer on the remote. Nothing was fetched or merged; choose another upstream branch, or push to create it there again.";
    public const string FetchedNothingToMerge = "Fetch completed. Your branch already contains the upstream commits.";
    public const string FetchedUpstreamRewritten = "Fetch completed, but the upstream was rewritten by a force push and this branch still holds commits it dropped, so a pull would merge them back in. Nothing was merged: reset the branch to the upstream, or rebase your own commits onto it.";
    public const string MergingFetched = "Merging the fetched branch…";
    public const string FetchingLfs = "Downloading Git LFS files…";
    public const string PushingLfs = "Uploading Git LFS files…";
    public const string LfsNotPushed = "Git LFS could not upload the content of the pushed files, so nothing was pushed. Git LFS's details are below.";
    public const string LfsNotFetched = "Git LFS could not download the content of the incoming files, so nothing was merged. Git LFS's details are below.";

    public const string CheckingDestination = "Checking the push destination…";
    public const string VerifyingRemote = "Verifying the remote…";
    public static string DestinationUnreadable(string explanation) => "The push destination could not be read. " + explanation + " Nothing was sent.";
    public const string AlreadyThere = "The push destination already has this commit.";
    public const string RemoteMoved = "The remote branch moved since your last fetch. Fetch and review it before replacing it. Nothing was sent.";
    public const string TargetChanged = "The branch, index or push destination changed. Review the refreshed target before sending.";
    public static string Pushing(bool force) => force ? "Replacing the remote branch…" : "Pushing the displayed branch target…";
    public const string VerifyingDestination = "Verifying the push destination…";
    public static string PushNotStarted(string reason) => "The push did not start: " + reason;
    public const string PushNotVerified = "Push stopped or completed, but its destination could not be verified. Check the remote in your Git tools before retrying.";
    public const string PushConfigurationChanged = "Push finished or stopped, but its configuration changed. Review the original destination before retrying.";
    public const string Replaced = "The remote branch was replaced and the destination commit was verified.";
    public const string Pushed = "Push completed and the destination commit was verified.";
    public const string PushedAfterInterruption = "Push reached the destination and was verified after interruption.";
    // A push that arrived at a push address of the remote's own leaves the remote-tracking branch to the fetch address.
    public static string ComparedWithFetch(string pushed, string fetchAddress) =>
        pushed + " Ahead and behind still compare with " + fetchAddress + " and change after a fetch from there.";
    public const string PushStopped = "Push stopped; verification found the destination unchanged. Check connectivity before retrying.";
    public const string DestinationChanged = "The destination changed during push. Fetch and review it before sending again.";

    // Why a remote cannot be synchronized in Trag.
    public const string MultipleUrls = "Remotes with multiple fetch or push URLs are not supported. Choose a remote with one destination.";
    public const string Mirror = "Mirror remotes cannot be synchronized in Trag.";
    public const string UnsupportedMapping = "This remote uses an unsupported fetch mapping. Use branch mappings into its own remote-tracking namespace.";
    public static readonly string MappingCount = "A remote needs between 1 and " + GitLimits.FetchMappings + " supported branch fetch mappings.";
    public static string OverlappingNames(string remote, string other) => "The names of remotes " + remote + " and " + other
        + " overlap, so their remote-tracking branches mix. Rename one of them before synchronizing.";
    public const string Shallow = "Shallow history must be completed in Git before synchronization.";
    public const string SshVariant = "Choose an OpenSSH or Plink SSH configuration before synchronizing.";
}
