using Trag.Domain.Accounts;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

// A push the host answered as missing is retried as a publish, which the account the remote's address names can make
// once it created the repository there.
public sealed class MissingRepositoryTests
{
    private const string Address = "https://github.com/nadia/sample-app";
    private static readonly AccountInfo Account = FakeAccountService.GitHub(user: "nadia");
    private static readonly RepositorySnapshot Snapshot = Snapshots.Create(branch: "trunk", trackingRemote: "sample-app",
        upstreamBranchRef: "refs/heads/trunk", remotes: [new("sample-app", Address, Address)]);
    private static readonly PublishOperation PushToTrunk = PublishOperation.PushTo("sample-app", "trunk", "trunk");

    [Fact]
    public void WhenAPublishIsAnsweredMissing_ShouldRetryItToTheSameRemoteBranch()
    {
        var publish = new PublishOperation(PublishKind.ExistingRemote, "sample-app", "trunk", true, RemoteBranch: "hybrid");
        Assert.Equal(publish, MissingRepository.Retry(new PublishRequest(Snapshot, publish)));
    }

    [Fact]
    public void WhenAPushIsAnsweredMissing_ShouldRetryAsAPublishToTheBranchItTracks()
    {
        var push = new SyncRequest(Snapshot, FakeGitSync.DefaultState(Snapshot, "sample-app", "trunk"), SyncAction.Push);
        Assert.Equal(PushToTrunk, MissingRepository.Retry(push));
    }

    // A fetch sends nothing; a publish that created the repository cannot be helped by creating it again.
    [Fact]
    public void WhenAnotherWriteIsAnsweredMissing_ShouldRetryNothing()
    {
        var fetch = new SyncRequest(Snapshot, FakeGitSync.DefaultState(Snapshot, "sample-app", "trunk"), SyncAction.Fetch);
        var created = new PublishRequest(Snapshot, PushToTrunk with { RepositoryName = "sample-app" });
        Assert.Null(MissingRepository.Retry(fetch)); Assert.Null(MissingRepository.Retry(created));
    }

    [Fact]
    public void WhenTheRemotesAddressNamesARepositoryUnderAnAccount_ShouldOfferToCreateItAndPublishThere()
    {
        var offer = MissingRepository.For(PushToTrunk, Snapshot, [Account]);
        var publish = PushToTrunk with { AccountId = Account.Id, RepositoryName = "sample-app" };
        Assert.Equal(new MissingRepository(new(Account, "sample-app"), publish), offer);
        Assert.True(offer!.IsNamedBy(Snapshot));
    }

    [Fact]
    public void WhenNoAccountOwnsTheAddressOrTheRemoteIsGone_ShouldOfferNothing()
    {
        Assert.Null(MissingRepository.For(PushToTrunk, Snapshot, [FakeAccountService.GitHub(user: "someone")]));
        Assert.Null(MissingRepository.For(PushToTrunk, Snapshot with { Remotes = [] }, [Account]));
    }

    [Fact]
    public void WhenTheRemotesAddressNamesAnotherRepository_ShouldNoLongerBeNamedByTheSnapshot()
    {
        const string moved = "https://github.com/nadia/other";
        var offer = MissingRepository.For(PushToTrunk, Snapshot, [Account])!;
        Assert.False(offer.IsNamedBy(Snapshot with { Remotes = [new("sample-app", moved, moved)] }));
    }
}
