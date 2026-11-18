using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;

namespace Trag.Domain.Publish;

// The publish dialog's fields as Domain judges them: what the chosen kind needs, whether the fields of a new remote are
// right, and the operation they describe. Defaults come from the snapshot: an existing remote when the repository has
// one, otherwise a new repository named after the folder; push when there is a commit to push. A new remote is origin
// unless that is taken, and then is named after the repository.
public sealed record PublishInput(PublishKind Kind, string RepositoryName, string RemoteName, string RemoteUrl,
    string? ExistingRemote, string? TargetBranch, bool Push, bool IsPrivate = true)
{
    // The name a remote the publish adds gets unless another is typed.
    public const string DefaultRemote = "origin";

    public static PublishInput Defaults(RepositorySnapshot? snapshot, string repositoryName)
    {
        var name = PublishNames.Suggest(repositoryName);
        return new(snapshot is { Remotes.Count: > 0 } ? PublishKind.ExistingRemote : PublishKind.CreateRepository,
            name, NamesRemoteAfterRepository(snapshot) ? name : DefaultRemote, "", snapshot?.PreferredRemote?.Name, null, snapshot is { IsUnborn: false });
    }

    private static bool NamesRemoteAfterRepository(RepositorySnapshot? snapshot) =>
        RemoteNames.Taken(DefaultRemote, snapshot?.Remotes.Select(item => item.Name) ?? []) is not null;

    // While the new remote is named after the repository, it keeps that name as the user types the repository's, until the
    // user names the remote otherwise.
    public static string RemoteNameFollowing(RepositorySnapshot? snapshot, string remoteName, string oldName, string newName) =>
        NamesRemoteAfterRepository(snapshot) && remoteName == oldName ? newName : remoteName;

    // Only a remote the publish adds is judged: its address when the user types one, its name against the remotes there are.
    // An empty address is not an error yet; it only keeps the input invalid.
    public string RemoteError(IReadOnlyList<RepositoryRemote> remotes)
    {
        if (Kind == PublishKind.ExistingRemote) return "";
        var url = RemoteUrl.Trim();
        if (Kind == PublishKind.AddRemote && url.Length != 0 && !RemoteUrls.IsValidAddress(url))
            return PublishText.AddressInvalid;
        if (Kind == PublishKind.AddRemote && RemoteUrls.HasCredentials(url))
            return RemoteText.UrlHasPassword;
        return RemoteNames.Error(RemoteName.Trim(), remotes.Select(remote => remote.Name));
    }

    public bool IsValid(RepositorySnapshot? snapshot) => (!Push || snapshot is { IsUnborn: false }) && Kind switch
    {
        PublishKind.CreateRepository => PublishNames.IsValid(RepositoryName) && RemoteError(snapshot?.Remotes ?? []).Length == 0,
        PublishKind.AddRemote => RemoteUrl.Trim().Length != 0 && RemoteError(snapshot?.Remotes ?? []).Length == 0,
        PublishKind.ExistingRemote => ExistingRemote is not null && TargetBranch is not null,
        _ => false
    };

    // The account creates the repository or authenticates the typed address; the owner is an organization, or null for
    // the account's own namespace. An existing remote keeps its own configuration.
    public PublishOperation ToOperation(string branch, string? accountId, string? ownerId) => Kind switch
    {
        PublishKind.CreateRepository => new(Kind, RemoteName.Trim(), branch, Push, AccountId: accountId, OwnerId: ownerId,
            RepositoryName: RepositoryName.Trim(), IsPrivate: IsPrivate),
        PublishKind.AddRemote => new(Kind, RemoteName.Trim(), branch, Push, RemoteUrl: RemoteUrl.Trim(), AccountId: accountId),
        _ => new(Kind, ExistingRemote ?? throw new InvalidOperationException("Choose a remote to publish to."), branch, Push, RemoteBranch: TargetBranch)
    };
}
