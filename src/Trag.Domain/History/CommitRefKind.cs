namespace Trag.Domain.History;

// What a name on a commit is, in the order the history shows them: HEAD itself when it is detached there, the branch
// checked out, other local branches, remote-tracking branches, tags.
public enum CommitRefKind { DetachedHead, CurrentBranch, Branch, RemoteBranch, Tag }
