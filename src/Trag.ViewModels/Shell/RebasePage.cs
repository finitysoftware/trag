using Trag.Application.Live;
using Trag.ViewModels.Rebase;

namespace Trag.ViewModels.Shell;

// What the rebase view binds (see FileStatusPage).
public sealed record RebasePage(RebaseViewModel Rebase, ConflictsPage Conflicts, Repository Repository, Navigator Navigator);
