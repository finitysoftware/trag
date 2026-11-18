using Trag.Domain.Branches;
using Trag.Domain.Merge;
using Trag.Domain.Paths;
using Trag.Domain.Rebase;
using Trag.Domain.Tags;

namespace Trag.Application.Ports.Git;

public interface IBranchReader
{
    Task<BranchSet> ReadBranchesAsync(string root, CancellationToken ct);
    // Whether HEAD already has the commit: it is HEAD or one HEAD was built on.
    Task<bool> ContainsAsync(string root, string commit, CancellationToken ct);
    // How many commits HEAD has after the commit, which a reset to it takes off the branch; null when HEAD does not have it.
    Task<int?> DistanceAsync(string root, string commit, CancellationToken ct);
    // The files the commit tracks that the index does not: where a hard reset to it writes over untracked files.
    Task<IReadOnlyList<GitPath>> ReadNotInIndexAsync(string root, string commit, CancellationToken ct);
    // The commits a rewrite from the commit would take, after the commit before it; null when HEAD does not have it.
    Task<RewriteRange?> ReadRewriteAsync(string root, string commit, CancellationToken ct);
    // A remote's address as its configuration keeps it; null when there is no such remote.
    Task<string?> RemoteUrlAsync(string root, string remote, CancellationToken ct);
    // The tags of commits, highest version first.
    Task<IReadOnlyList<TagInfo>> ReadTagsAsync(string root, CancellationToken ct);
    // What merging the from ref into the into ref would do; both are full ref names the branch list read.
    Task<MergePreview> PreviewMergeAsync(string root, string into, string from, CancellationToken ct);
    // What rebasing the current branch onto the onto ref would do; the onto ref is a full ref name the branch list read.
    Task<RebasePreview> PreviewRebaseAsync(string root, string onto, CancellationToken ct);
}
