using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Branches;

// The branch picker's moves as Git writes: switching to a row, and deleting one before and after the question.
public static class BranchMoves
{
    // A local branch is switched to. A remote branch is reached through its local counterpart: a switch when that exists,
    // otherwise a new local branch that tracks the remote one. Null when the counterpart is already the current branch.
    // Git keeps a branch as a file whose name ignores case on Windows, so the counterpart's name ignores case as well.
    public static BranchMove? SwitchTo(BranchInfo branch, BranchCatalog catalog)
    {
        if (!branch.IsRemote) return new(BranchAction.Switch, new(branch.Name));
        var local = branch.LocalName;
        if (string.Equals(catalog.Current?.Name, local, StringComparison.OrdinalIgnoreCase)) return null;
        if (catalog.Local.FirstOrDefault(candidate => string.Equals(candidate.Name, local, StringComparison.OrdinalIgnoreCase)) is { } counterpart)
            return new(BranchAction.Switch, new(counterpart.Name));
        return new(BranchAction.Create, new(local, null, true, branch.Ref));
    }

    // A branch on a remote is deleted by talking to that remote; a local branch Git refuses as unmerged goes by force once
    // the user confirmed, up to the tip the list showed.
    public static GitWriteRequest Delete(RepositorySnapshot expected, BranchInfo branch, bool confirmed) =>
        branch is { IsRemote: true, Remote: { } remote } ? new RemoteBranchDeleteRequest(expected, remote, branch.LocalName)
            : new BranchRequest(expected, new(branch.Name, ForceAt: confirmed ? branch.Head : null), BranchAction.Delete);
}
