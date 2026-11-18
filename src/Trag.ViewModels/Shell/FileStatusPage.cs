using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.ViewModels.Commit;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Discard;
using Trag.ViewModels.Files;
using Trag.ViewModels.History;
using Trag.ViewModels.Merge;
using Trag.ViewModels.Status;
using Trag.ViewModels.Sync;

namespace Trag.ViewModels.Shell;

// What a section's view binds: the feature view models it shows and the shared facts it reads. The shell hands them
// out, so each view is typed on its own page instead of on the whole shell.
public sealed record FileStatusPage(FileStatusViewModel FileStatus, CommitViewModel Commit, DiscardViewModel Discard, WorkingDiffViewModel WorkingDiff,
    LayoutPreferences Layout, Repository Repository, IRelayCommand Open, ResolutionViewModel Resolution,
    ForcePushViewModel ForcePush, HistoryViewModel History, BlameViewModel Blame, FileActionsViewModel Files);
