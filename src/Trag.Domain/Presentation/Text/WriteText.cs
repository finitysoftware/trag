using Trag.Domain.Writes;
using Trag.Domain.Naming;
using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// What the status line says while a write runs, one sentence for each kind of write, and what a result adds when the
// repository could not be read after it.
public static class WriteText
{
    // A write that makes commits or a tag Git signs may wait for the key's passphrase, in a window of its own.
    public static string Progress(GitWriteRequest request) =>
        SigningWrites.Key(request) == CommitSigning.None ? Plain(request) : Plain(request) + " Your key may ask for its passphrase.";
    // A sync and a publish, and the writes that finish or leave a merge, a rebase or a conflict, also report in the sync
    // panel.
    public static bool ReportsInSyncPanel(GitWriteRequest request) => request is SyncRequest or PublishRequest
        || WriteNeeds.Of(request) is WriteNeed.Merge or WriteNeed.Rebase or WriteNeed.Resolution;
    public const string PassphraseWindowLeft = "If a window still asks for your key's passphrase, close it: nothing waits for it any more.";

    private static string Plain(GitWriteRequest request) => request switch
    {
        StageRequest => "Staging selected whole files…", UnstageRequest => "Unstaging selected files…", DiscardRequest => "Discarding changes…",
        MergeAbortRequest => "Aborting merge and verifying the result…", MergeToolRequest => "Waiting for the external tool. Save and close its file to continue…",
        CommitRequest { Expected.Signing: not CommitSigning.None } => "Signing the commit…",
        CommitRequest { FinishesMerge: true } => "Finishing merge and verifying both parents…", CommitRequest { Amend: true } => "Amending the last commit…", ResetRequest => "Resetting the branch…",
        PatchRequest patch => patch.Action switch { PatchAction.Stage => "Staging", PatchAction.Unstage => "Unstaging", _ => "Discarding" }
            + (patch.Lines ? " the lines…" : " the hunk…"),
        RewriteRequest => "Editing the commits…",
        RemoteAddRequest => "Adding the remote…", RemoteEditRequest => "Saving the remote…", RemoteRemoveRequest => "Removing the remote…",
        IgnoreRequest => "Ignoring the file…",
        SubmoduleUpdateRequest => SubmoduleText.Updating,
        LfsDownloadRequest => SyncOutcomeText.FetchingLfs,
        RestoreFileRequest => "Restoring the file…",
        RevertFileRequest => "Undoing the file's changes…",
        ConflictSideRequest => "Resolving the file with one side…", ConflictBlockRequest => "Resolving the conflict block…",
        ReopenConflictRequest => "Bringing the conflict back…",
        MergeRequest merge => "Merging " + merge.FromName + " and verifying the result…",
        RebaseRequest rebase => "Rebasing onto " + rebase.OntoName + " and verifying the result…",
        RebaseContinueRequest => "Continuing the rebase…", RebaseSkipRequest => "Skipping the commit and continuing the rebase…",
        RebaseAbortRequest => "Aborting the rebase and verifying the result…",
        BranchRequest { Action: BranchAction.Switch } => "Switching branch…", BranchRequest { Action: BranchAction.Create } => "Creating branch…",
        BranchRequest { Action: BranchAction.Checkout } => "Moving to the commit…",
        BranchRequest { Action: BranchAction.Delete } or RemoteBranchDeleteRequest => "Deleting branch…",
        TagRequest => "Creating tag…", TagDeleteRequest => "Deleting tag…", RevertRequest => "Reverting the commit…", CherryPickRequest => "Cherry-picking the commit…",
        PickContinueRequest finish => PickText.Finishing(finish.Expected.Operation), PickAbortRequest abort => PickText.Aborting(abort.Expected.Operation), RemoteTagRequest { Action: RemoteTagAction.Push } => "Pushing the tag…",
        RemoteTagRequest { Action: RemoteTagAction.Delete } => "Deleting the tag on the remote…",
        StashRequest { Action: StashAction.Push } => "Stashing changes…", StashRequest { Action: StashAction.Apply } => "Applying the stash…",
        StashRequest { Action: StashAction.Drop } => "Dropping the stash…", PublishRequest => "Publishing…",
        SyncRequest { Action: SyncAction.SelectAccount } => "Saving the account selection…", SyncRequest { Action: SyncAction.Fetch } => "Fetching…",
        SyncRequest { Action: SyncAction.Pull } => "Fetching and merging…", SyncRequest { Action: SyncAction.Push or SyncAction.ForcePush } => SyncOutcomeText.CheckingDestination,
        _ => "Committing staged content…"
    };

    public static string WithoutFinalStatus(string message) => message + " The final status could not be read; refresh before continuing.";

    // What a write is refused with before Git starts.
    public const string RebaseOntoMissing = "Choose a branch to rebase onto.";
    public static readonly string CommitMessageInvalid = MessageInvalid("commit message");
    public static readonly string MergeMessageInvalid = MessageInvalid("merge commit message");
    public const string NothingStaged = "Stage at least one file before committing.";
    public const string SubjectMissing = "Write the commit's subject above its Co-authored-by lines.";
    public const string NothingToAmend = "There is no commit to amend yet.";
    public const string HunkGone = "That change is no longer in the file. Refresh and try again.";
    public const string PartNotUtf8 = "This file is not UTF-8 text, so its changes can only be staged, unstaged or discarded whole.";
    public const string MergeBranchMissing = "Choose a branch to merge.";
    public const string BothSidesChosen = "Choose one side for the whole file.";
    public const string ConflictBlockMissing = "Choose a conflict block of the file shown.";
    public const string AlreadyInConflict = "This file is already in conflict.";
    public const string ConflictedFileMissing = "Select one conflicted file of the merge.";
    public const string ToolFileMissing = "Select one conflicted file to open a tool.";
    public const string ToolNeedsBothSides = "The merge tool works on a file both sides changed. Choose the side to keep instead.";
    public static readonly string SelectionTooLarge = "Select files before updating the index; the selection must fit within "
        + Counted.Megabytes(GitLimits.SelectionPathBytes) + " of path data.";
    public const string SelectionStale = "The selection no longer matches the displayed status. Refresh and select the files again.";
    public const string UnknownWrite = "Unknown write operation.";

    private static string MessageInvalid(string message) => "Enter a " + message + " of at most " + Counted.Kilobytes(GitLimits.CommitMessageBytes) + " without NUL characters.";

    // What the engine says when a write never reached Git, or Git's outcome could not be confirmed.
    public const string WritingUnavailable = "Writing is not available in this workspace.";
    public const string SessionChanged = "The repository session changed. Open it again before writing.";
    public const string Interrupted = "The operation was interrupted. Review the refreshed repository before trying again.";
    public const string StoppedPartway = "The operation was stopped after Git had already changed files, and nothing was undone. Review the refreshed status before going on.";
    public const string NotVerified = "The operation could not be verified. Review the refreshed repository before trying again.";

    // Where the index Trag kept for review is, in the details of the result that kept it.
    public static string RecoveryIndex(string path) => "Recovery index: " + path;

    // Git found the index locked by something else; when Trag tried to take the lock itself, it also left it alone. A lock
    // a running Git holds goes away by waiting, one an interrupted Git left behind only when it is removed, so both are said.
    public const string IndexLocked = "The Git index is locked. If a Git program is still running, wait for it to finish; if none is, an interrupted one left index.lock in the repository's Git folder: remove that file, then try again.";
    public const string IndexLockHeld = IndexLocked + " Trag never removes it on its own.";

    // What the status line says when the shell's writer refuses a write before it starts, another write running aside.
    public const string ReviewFirst = "The last operation's outcome is not verified. Refresh (F5) and review the repository before changing it.";
    public const string ReadInProgress = "The repository is still being read. Try again once its status is shown.";

    // What the router refuses a write with, or says when it ended outside the writer.
    public const string SyncUnavailable = "Synchronization is unavailable.";
    public const string Canceled = "Operation canceled. The original index is unchanged.";
    public const string TooMuchOutput = "Git produced too much output. The original index is unchanged; review your Git tools before retrying.";
    public const string NotCompleted = "Git could not complete this operation. Check folder access and repository state, then refresh.";
    public const string StateUnsupported = "This operation is unavailable for the current repository state. Review the repository warning and pending merge.";
    public const string ChangedOutside = "The branch or staged content changed outside Trag. Review the refreshed status before trying again.";
    public const string IdentityMissing = "Git needs an author and committer identity. Configure user.name and user.email, then refresh. Your message is kept.";
    public const string IdentityChanged = "The Git identity changed. Review the refreshed author and committer before committing.";
    public const string ChangedDuring = "The repository changed during this operation. Its original index was kept. Refresh and review the changes.";
    public const string RebaseConflictsLeft = "Resolve and mark every conflicted file as resolved before continuing the rebase.";
    public const string MergeConflictsLeft = "Resolve and stage every conflicted file before finishing the merge.";
    // The actions that would commit markers: finishing a merge or a pick, continuing a rebase.
    public const string FinishAction = "Finish";
    public const string ContinueAction = "Continue";
    public static string WouldCommitMarkers(string action, string paths) => action + " would commit conflict markers in " + paths + ". Mark the file as unresolved and resolve it again.";
}
