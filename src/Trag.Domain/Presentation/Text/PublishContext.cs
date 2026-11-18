using Trag.Domain.Status;

namespace Trag.Domain.Presentation.Text;

// Facts shared by the toolbar, sidebar and publish draft, as the flags and texts the views bind over the snapshot.
// Replaced when the shown repository changes.
public sealed record PublishContext(RepositorySnapshot? Snapshot, string RepositoryName)
{
    public static readonly PublishContext Empty = new(null, "");
    public IReadOnlyList<RepositoryRemote> Remotes => Snapshot?.Remotes ?? [];
    public string BranchName => Snapshot?.Branch ?? "the current branch";
    public bool HasRemotes => Remotes.Count != 0;
    public bool HasMultipleRemotes => Remotes.Count > 1;
    private bool IsDetachedHead => Snapshot is { IsDetachedAtCommit: true };
    public bool CanPush => Snapshot is { IsUnborn: false };
    public bool CanOpen => Snapshot is { HasUnsupportedState: false };
    public bool ShowPublish => Snapshot is { NeedsPublish: true };
    public bool ShowBranchCard => ShowPublish && HasRemotes;
    public bool ShowSyncSelectors => HasRemotes && !ShowBranchCard && !IsDetachedHead;
    public bool ShowNoRemoteCard => !HasRemotes && !IsDetachedHead;
    public string BranchCardText => BranchName + " is not published yet";
    public string Tip => HasRemotes ? "Push " + BranchName + " to a remote and set it as the upstream"
        : "Publish this repository to GitHub, GitLab or an existing remote";
    public string PushLabel => "Push " + BranchName + " after publishing";

    public RepositoryRemote? PreferredRemote => Snapshot?.PreferredRemote;
    public RepositoryRemote? KeepRemote(RepositoryRemote? selected) => Snapshot?.KeepRemote(selected);
}
