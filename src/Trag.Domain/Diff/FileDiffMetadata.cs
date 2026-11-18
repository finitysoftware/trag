using Trag.Domain.Paths;

namespace Trag.Domain.Diff;

public sealed record FileDiffMetadata(GitPath Path, string Status, string OldMode, string NewMode, string OldObject, string NewObject, GitPath? OriginalPath = null)
{
    // An all-zero object ID is Git's way of saying there is no object: an added file has no old one, a deleted file
    // no new one, and an untracked file has neither.
    public bool HasOldObject => !GitObjectId.IsZero(OldObject);
    public bool HasNewObject => !GitObjectId.IsZero(NewObject);
    // A commit of another repository in this one's tree, on either side of the change.
    public bool IsSubmodule => OldMode == SubmoduleMode || NewMode == SubmoduleMode;
    // A commit's file is blamed at that commit: the commit has to keep it as a file, not a submodule's entry, by a name
    // that reaches Git as text.
    public bool CanBlame => Path.IsDisplayable && !Status.StartsWith('D') && NewMode != SubmoduleMode;
    // The content the commit leaves the file with, which a version is saved or restored from by the file's name as text:
    // a deleted file has none, and a submodule's entry names a commit, not a file.
    public bool HasVersion => HasNewObject && NewMode != SubmoduleMode && Path.IsDisplayable;
    // A file's history is asked of Git by its name as text.
    public bool HasHistory => Path.IsDisplayable;
    // What the commit did to the file is undone by names that reach Git as text, both of a rename's; a submodule's entry
    // names a commit, not lines.
    public bool CanRevert => Path.IsDisplayable && OriginalPath is not { IsDisplayable: false } && !IsSubmodule;

    // The object whose content a bounded preview shows: the new one where it exists, otherwise the old one.
    public string? PreviewObject => HasNewObject ? NewObject : HasOldObject ? OldObject : null;

    public IEnumerable<string> Objects
    {
        get
        {
            if (HasOldObject) yield return OldObject;
            if (HasNewObject) yield return NewObject;
        }
    }

    // Paths compare by their bytes; the record itself would compare the arrays by reference.
    public bool HasSameContent(FileDiffMetadata other) => Path.IsSame(other.Path) && Status == other.Status && OldMode == other.OldMode
        && NewMode == other.NewMode && OldObject == other.OldObject && NewObject == other.NewObject
        && GitPath.Same(OriginalPath, other.OriginalPath);

    private const string SubmoduleMode = "160000";
}
