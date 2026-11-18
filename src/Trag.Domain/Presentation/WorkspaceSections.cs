using Trag.Domain.Status;

namespace Trag.Domain.Presentation;

// Which section belongs to what. The open repository goes on in the view of its merge, rebase, revert or cherry-pick in
// progress, else in the file status, which also reaches the conflicts a stash left. A decided file's diff shows in the
// views of those operations (the conflict view of a stash lists no decided files), and the working tree's diff in them
// and in the file status.
public static class WorkspaceSections
{
    public static WorkspaceSection Open(RepositoryOperation operation) => operation switch
    {
        RepositoryOperation.Merge => WorkspaceSection.Merge,
        RepositoryOperation.Rebase => WorkspaceSection.Rebase,
        RepositoryOperation.Revert or RepositoryOperation.CherryPick => WorkspaceSection.Pick,
        _ => WorkspaceSection.FileStatus
    };

    // The view of an operation exists only while that operation is in progress; every other section always does.
    public static bool Exists(WorkspaceSection section, RepositoryOperation operation) => section switch
    {
        WorkspaceSection.Merge => operation == RepositoryOperation.Merge,
        WorkspaceSection.Rebase => operation == RepositoryOperation.Rebase,
        WorkspaceSection.Conflicts => operation == RepositoryOperation.Conflict,
        WorkspaceSection.Pick => operation is RepositoryOperation.Revert or RepositoryOperation.CherryPick,
        _ => true
    };

    public static bool ShowsDecidedDiff(WorkspaceSection section) => section is WorkspaceSection.Merge or WorkspaceSection.Rebase or WorkspaceSection.Pick;

    public static bool ShowsWorkingDiff(WorkspaceSection section) => section == WorkspaceSection.FileStatus || ShowsDecidedDiff(section);
}
