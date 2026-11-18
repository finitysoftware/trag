using Trag.Application.Live;
using Trag.ViewModels.Branches;
using Trag.ViewModels.CherryPick;
using Trag.ViewModels.Files;
using Trag.ViewModels.Reset;
using Trag.ViewModels.Commit;
using Trag.ViewModels.Details;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Discard;
using Trag.ViewModels.History;
using Trag.ViewModels.Merge;
using Trag.ViewModels.Publish;
using Trag.ViewModels.Rebase;
using Trag.ViewModels.Revert;
using Trag.ViewModels.Stash;
using Trag.ViewModels.Status;
using Trag.ViewModels.Sync;
using Trag.ViewModels.Tags;

namespace Trag.ViewModels.Shell;

// Everything the workspace shows for one repository: the repository and the feature view models, composed by
// the factory for that repository and gone with it. Detach undoes what links them to the application (the navigator's
// section and reselection), which the factory set up.
public sealed record WorkspaceFeatures(Repository Repository, FileStatusViewModel FileStatus, WorkingDiffViewModel WorkingDiff,
    CommitViewModel Commit, DiscardViewModel Discard, StashViewModel Stash, BranchesViewModel Branches, TagsViewModel Tags, RevertViewModel Revert, CherryPickViewModel CherryPick, ResetViewModel Reset, RewriteViewModel Rewrite, RemoteSettingsViewModel RemoteSettings, RepositoryDetailsViewModel RepositoryDetails, PickViewModel Pick, GoToCommitViewModel GoTo,
    ResolutionViewModel Resolution, ConflictViewModel Conflict, MergeViewModel Merge, RebaseViewModel Rebase, MergeBranchesViewModel MergeBranches,
    RebaseBranchViewModel RebaseBranch, MergeDialogViewModel MergeDialog, HistoryViewModel History, BlameViewModel Blame, FileActionsViewModel Files, SyncViewModel Sync, ForcePushViewModel ForcePush,
    PublishViewModel Publish, Action Detach);
