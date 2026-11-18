using Trag.Domain.Merge;
using Trag.Domain.Writes;

namespace Trag.Domain.Status;

// Upstream is the upstream as git status names it (origin/main), null without one or while its remote-tracking ref is
// not here yet; the configuration behind it is TrackingRemote and UpstreamBranchRef.
public sealed record RepositorySnapshot(string Root, string GitVersion, string? Branch, string? Head,
    string? Upstream, string? TrackingRemote, IReadOnlyList<RepositoryRemote> Remotes,
    RepositoryOperation Operation, IReadOnlyList<string> Limitations, IReadOnlyList<FileStatus> Files)
{
    public string? GitDirectory { get; init; }
    // The working tree of the repository this one is a submodule of; null for any other repository.
    public string? Superproject { get; init; }
    // Submodules not at the commit the index records: moved in their own working tree, or never checked out.
    public IReadOnlyList<string> SubmodulesToUpdate { get; init; } = [];
    // Git LFS's filter is configured, so Git stages an LFS file as its pointer; the index holds files Git LFS keeps.
    public bool LfsInstalled { get; init; }
    public bool HasLfsFiles { get; init; }
    public string? CommonDirectory { get; init; }
    public string? UpstreamBranchRef { get; init; }
    public string IndexFingerprint { get; init; } = "";
    public GitIdentity? Author { get; init; }
    public GitIdentity? Committer { get; init; }
    public CommitSigning Signing { get; init; }
    // Git signs every tag (tag.gpgSign), which makes each one an annotated tag with a message.
    public bool SignsTags { get; init; }
    // The kind of key Git signs with when it signs anything (gpg.format).
    public CommitSigning SigningKey { get; init; }
    public RepositoryMerge? Merge { get; init; }
    public RepositoryRebase? Rebase { get; init; }
    // The commit a revert or a cherry-pick in progress applies (REVERT_HEAD, CHERRY_PICK_HEAD); null outside one.
    public string? PickHead { get; init; }
    public string? Theirs => RepositoryOperations.Theirs(Operation, Merge?.IncomingHead, Rebase?.Stopped, PickHead);
    // What the operation in progress has resolved and what is left of its open conflicts; empty outside one.
    public MergeResolutions Resolutions { get; init; } = MergeResolutions.None;
    public bool IsUnborn => Head is null;

    public bool HasSameHistory(RepositorySnapshot? other) =>
        other is not null && Root == other.Root && Head == other.Head && Branch == other.Branch;

    // Whether this is still the repository and the HEAD a pending choice was made for.
    public bool IsSameHead(string root, string? head) => Root == root && Head == head;

    // Commits reachable from a detached HEAD but from no branch, remote branch or tag; always zero on a branch.
    public int DetachedCommits { get; init; }
    // The commit refs/stash points at, or null without stashes; the sidebar list follows this value.
    public string? StashHead { get; init; }
    // A fingerprint of every remote-tracking ref and its commit: it changes when a fetch, by Trag or any other program,
    // moved one. Empty without remote-tracking refs.
    public string RemoteRefs { get; init; } = "";
    // A fingerprint of every local branch and its commit: it changes when a commit, a branch created, deleted or reset,
    // by Trag or any other program, moved one. Empty without local branches.
    public string LocalRefs { get; init; } = "";
    // A fingerprint of every tag and what it points at: it changes when a tag is created, deleted or moved, by Trag or any
    // other program. Empty without tags.
    public string TagRefs { get; init; } = "";
    // The commit the upstream's remote-tracking ref points at, or null without one.
    public string? UpstreamHead { get; init; }
    public bool HasSameRemotes(RepositorySnapshot? other) => other is not null && RemoteRefs == other.RemoteRefs;
    public bool HasSameTags(RepositorySnapshot? other) => other is not null && TagRefs == other.TagRefs;
    public bool HasSameLocals(RepositorySnapshot? other) => other is not null && LocalRefs == other.LocalRefs;
    // What the branch list follows: the checked-out branch, every local and remote-tracking ref, and the remotes' names
    // and push addresses, from which a branch's request page is made. Two snapshots with the same key list the same
    // branches.
    public object BranchesKey => (Branch, LocalRefs, RemoteRefs, string.Join('\n', Remotes.Select(remote => remote.Name + '\t' + remote.PushAddress)));
    public bool IsDetached => Branch is null;
    // A HEAD detached at a commit, which an unborn repository without a branch is not, and neither is a rebase: Git
    // detaches HEAD while it replays the commits, and the branch moves when the rebase finishes.
    public bool IsDetachedAtCommit => IsDetached && Head is not null && Operation != RepositoryOperation.Rebase;
    // Moving HEAD away from here would leave commits on no branch.
    public bool LeavesCommitsBehind => IsDetachedAtCommit && DetachedCommits > 0;
    // A commit, a merge commit included, needs both of Git's identities.
    public bool HasCommitIdentity => Author is not null && Committer is not null;
    // The uncommitted changes Git's merges, rebases and picks would have to keep: staged, unstaged, untracked or
    // conflicted. A submodule's own working tree, moved to another commit or with files of its own, is its own
    // repository's, and Git leaves it alone there.
    public int ChangedFiles => Files.Count(file => !(file.IsSubmodule && file.Index == '.'));
    public bool HasChanges => ChangedFiles != 0;
    public IEnumerable<FileStatus> Conflicts => Files.Where(file => file.IsConflict);
    // Sync and publish need a branch; staging, committing and moving between commits also work on a detached HEAD.
    public bool HasUnsupportedState => IsDetached || HasUnsupportedWriteState;
    // A merge or a rebase starts on a branch with a commit, with no operation in progress.
    public bool CanStartOperation => !HasUnsupportedState && !IsUnborn;
    public bool HasUnsupportedWriteState => Operation != RepositoryOperation.None || Limitations.Count != 0;
    public bool HasSupportedMerge => !IsDetached && Head is not null && Limitations.Count == 0
        && Operation == RepositoryOperation.Merge && Merge is { IncomingHead: not null, Limitation.Length: 0 }
        && Merge.OriginalHead == Head;

    public bool HasSupportedRebase => Head is not null && Limitations.Count == 0
        && Operation == RepositoryOperation.Rebase && Rebase is { Limitation.Length: 0 };
    // Conflicts with no merge or rebase in progress (a stash applied over changed files) are resolved file by file.
    public bool HasSupportedConflicts => Limitations.Count == 0 && Operation == RepositoryOperation.Conflict;
    // A revert or a cherry-pick stopped at its conflicts, which Trag resolves and then finishes or aborts.
    public bool HasSupportedPick => Head is not null && Limitations.Count == 0
        && Operation is RepositoryOperation.Revert or RepositoryOperation.CherryPick && PickHead is not null;
    // Conflicts are resolved in Trag while a merge, a rebase, a revert or a cherry-pick it can continue is in progress, or
    // when none is.
    public bool HasSupportedResolution => HasSupportedMerge || HasSupportedRebase || HasSupportedPick || HasSupportedConflicts;

    // One merge is one incoming commit onto one original HEAD in one repository; the same merge seen again keeps the screen.
    public string? MergeIdentity => Merge is { } merge ? Root + "\0" + Head + "\0" + merge.IncomingHead : null;
    // One rebase is one branch going from one commit onto another; every stop of it keeps the screen.
    public string? RebaseIdentity => Rebase is { } rebase ? Root + "\0" + rebase.Branch + "\0" + rebase.Onto + "\0" + rebase.OrigHead : null;
    // The commit a rebase stopped at: a new stop brings that commit's message.
    public string? RebaseStopIdentity => Rebase is { } rebase ? RebaseIdentity + "\0" + rebase.LinesDone + "\0" + rebase.Stopped : null;
    // One pick is one commit applied in one repository.
    public string? PickIdentity => PickHead is { } reverted ? Root + "\0" + reverted : null;

    // Publish replaces Push until the branch has a tracking remote.
    public bool NeedsPublish => Branch is not null && (Remotes.Count == 0 || TrackingRemote is null);
    // The remote listed under the name; null when none is, or no name is given.
    public RepositoryRemote? Remote(string? name) => Remotes.FirstOrDefault(remote => remote.Name == name);
    // The remote a dialog offers first: origin, or the first one listed.
    public RepositoryRemote? Origin => Remote("origin");
    public RepositoryRemote? PreferredRemote => Origin ?? Remotes.FirstOrDefault();
    // The chosen remote as this snapshot knows it, or the first remote once the chosen one is gone; nothing chosen stays nothing.
    public RepositoryRemote? KeepRemote(RepositoryRemote? selected) =>
        selected is null ? null : Remote(selected.Name) ?? Remotes.FirstOrDefault();
}
