using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;

namespace Trag.Domain.Publish;

// A hosted repository a push found missing, which the account its remote's address names can create: private, with the
// branch then published to it as the push would have.
public sealed record MissingRepository(AccountRepository Repository, PublishOperation Publish)
{
    public string Text => PublishText.RepositoryMissing(Repository);
    public string Note => PublishText.CreatesMissing(Repository, Publish.Branch);

    // The publish that retries a write the host answered as missing: a publish goes to the same remote branch, a push to
    // the branch it tracks. Null for any other write, and for a publish that created the repository already: creating it
    // again cannot help.
    public static PublishOperation? Retry(GitWriteRequest request) => request switch
    {
        PublishRequest { Publish: { RepositoryName: null } publish } => PublishOperation.PushTo(publish.RemoteName, publish.Branch, publish.RemoteBranch),
        SyncRequest { Action: SyncAction.Push, Sync.Branch: { } target, Expected.Branch: { } branch } push => PublishOperation.PushTo(push.Sync.Remote.Name, branch, target.Display),
        _ => null
    };

    // The offer for the retry, when the remote's address in the snapshot names a repository under one of the accounts.
    public static MissingRepository? For(PublishOperation retry, RepositorySnapshot snapshot, IReadOnlyList<AccountInfo> accounts) =>
        snapshot.Remote(retry.RemoteName) is { } remote && AccountRepository.At(remote.PushAddress, accounts) is { } repository
            ? new(repository, retry with { AccountId = repository.Account.Id, RepositoryName = repository.Name }) : null;

    // Whether the remote's address in the snapshot still names this repository under the same account.
    public bool IsNamedBy(RepositorySnapshot snapshot) => For(Publish, snapshot, [Repository.Account]) == this;
}
