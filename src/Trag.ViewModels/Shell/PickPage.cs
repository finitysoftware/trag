using Trag.ViewModels.Revert;

namespace Trag.ViewModels.Shell;

// What the view of a pick in progress binds (see MergePage): the conflict panels it shares with the other conflict
// views, and the pick's own Finish and Abort.
public sealed record PickPage(PickViewModel Pick, ConflictsPage Conflicts, Navigator Navigator);
