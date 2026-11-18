using Trag.Application.Live;
using Trag.ViewModels.Commit;
using Trag.ViewModels.Merge;

namespace Trag.ViewModels.Shell;

// What the merge view binds (see FileStatusPage).
public sealed record MergePage(MergeViewModel Merge, ConflictsPage Conflicts, CommitViewModel Commit, Repository Repository, Navigator Navigator);
