using Trag.Domain.Status;

namespace Trag.Domain.Publish;

// OwnerId is null for the account's own namespace; otherwise a provider organization or group id.
// RemoteBranch names the branch on the remote; null keeps the local name.
public sealed record PublishOperation(PublishKind Kind, string RemoteName, string Branch, bool Push,
    string? RemoteUrl = null, string? AccountId = null, string? OwnerId = null, string? RepositoryName = null, bool IsPrivate = true, string? RemoteBranch = null)
{
    // The branch pushed to a remote it may not have yet, and set to track it there; null keeps the local name.
    public static PublishOperation PushTo(string remote, string branch, string? remoteBranch) =>
        new(PublishKind.ExistingRemote, remote, branch, true, RemoteBranch: remoteBranch);

    // Whether the snapshot after a publish records what this operation set out to configure: an existing remote is the
    // branch's tracking remote, a new one is listed. A push that failed after that can be retried from Sync.
    public bool IsConfiguredIn(RepositorySnapshot? snapshot) => snapshot is not null && (Kind == PublishKind.ExistingRemote
        ? snapshot.TrackingRemote == RemoteName
        : snapshot.Remote(RemoteName) is not null);

    // The publish dialog is done when the write succeeded or recorded the remote and upstream; a failed push after that
    // is retried from Sync.
    public bool IsDone(Writes.GitWriteResult result) => result.Outcome == Writes.GitWriteOutcome.Succeeded || IsConfiguredIn(result.Snapshot);
}
