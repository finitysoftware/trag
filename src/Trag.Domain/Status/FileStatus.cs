using Trag.Domain.Paths;

namespace Trag.Domain.Status;

// A submodule is moved when its own working tree is at another commit than the index records, and has changes of its own
// when files inside it are modified or untracked. The working tree has no file at the path when it holds a submodule's
// entry there or nothing, whichever side the change is on. A submodule is checked out when its folder holds its repository;
// Git's status lists one never checked out with the same fields.
public sealed record FileStatus(GitPath Path, char Index, char Worktree, GitPath? OriginalPath = null,
    bool IsConflict = false, bool IsSubmodule = false, bool IsSubmoduleMoved = false, bool SubmoduleHasOwnChanges = false,
    bool WorkingTreeHasNoFile = false, bool SubmoduleIsCheckedOut = false)
{
    public bool IsStaged => !IsConflict && Index is not ('.' or '?' or '!');
    public bool IsUnstaged => IsConflict || Worktree is not ('.' or '!');
    public bool IsUntracked => Worktree == '?';
    // git add -N recorded the path of a new file and none of its content, so the index has nothing to bring it back to.
    public bool IsIntentToAdd => Index == '.' && Worktree == 'A';
    // Git lists every untracked file on its own, so the one untracked entry it names as a folder ("inner/") is a folder
    // holding a repository of its own.
    public bool IsRepositoryOfItsOwn => IsUntracked && Path.Bytes is [.., (byte)'/'];
    // A file Git tracks has a history, by a name that is UTF-8, since paths reach Git as text; its blame needs the file
    // there to read, deleted on neither side: blame reads the working tree's file, which a submodule's commit is not.
    public bool HasHistory => !IsUntracked && Path.IsDisplayable;
    public bool CanBlame => HasHistory && Index != 'D' && Worktree != 'D' && !WorkingTreeHasNoFile;
    // A change of the working tree that can be staged or discarded; a conflict is listed and resolved apart.
    public bool IsWorkingTreeChange => IsUnstaged && !IsConflict;
    // Both names of a rename, the new one first; every other file has one.
    public IReadOnlyList<GitPath> Paths => OriginalPath is null ? [Path] : [Path, OriginalPath];
    // What staging the file names to Git. A rename or copy the index already holds has nothing left under its old name,
    // which git add refuses as a name that matches nothing; the old name is staged only while the rename is the
    // working tree's and the index still holds it.
    public IReadOnlyList<GitPath> StagePaths => Index is 'R' or 'C' ? [Path] : Paths;

    // A path names one row, except that Git lists a deletion the index holds apart from an untracked file of the same name
    // (git rm --cached leaves the file on disk).
    public (string Path, bool IsUntracked) RowKey => (Path.Key, IsUntracked);

    // These details decide whether an existing row needs replacing.
    public bool HasSameChange(FileStatus other) => Index == other.Index && Worktree == other.Worktree
        && IsConflict == other.IsConflict && IsSubmodule == other.IsSubmodule && SubmoduleIsCheckedOut == other.SubmoduleIsCheckedOut
        && GitPath.Same(OriginalPath, other.OriginalPath);
}
