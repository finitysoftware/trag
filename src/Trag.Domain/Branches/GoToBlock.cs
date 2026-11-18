namespace Trag.Domain.Branches;

// What keeps a double-click in History from moving HEAD: nothing, uncommitted changes, or a merge or rebase still in
// progress.
public enum GoToBlock { None, Changes, Merge, Rebase }
