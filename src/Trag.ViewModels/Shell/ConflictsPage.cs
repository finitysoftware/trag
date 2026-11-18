using Trag.Application.Live;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Merge;

namespace Trag.ViewModels.Shell;

// What the conflict panels bind, the same in the merge view, the rebase view and the view of conflicts a stash left: the
// lists and the selected file's actions, its blocks, the diff of a decided file, and the way back to the staged files.
public sealed record ConflictsPage(ResolutionViewModel Resolution, ConflictViewModel Conflict, WorkingDiffViewModel WorkingDiff,
    LayoutPreferences Layout, Navigator Navigator);
