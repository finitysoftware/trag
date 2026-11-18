using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

public sealed class PublishInputTests
{
    private static PublishInput Input(PublishKind kind, string remoteName = "origin", string url = "") =>
        new(kind, "trag", remoteName, url, "origin", "main", Push: true);

    // Both kinds that add a remote judge the name it gets.
    [Fact]
    public void WhenTheRemoteToAddHasABadName_ShouldSaySo()
    {
        Assert.NotEmpty(Input(PublishKind.CreateRepository, "bad name").RemoteError([]));
        Assert.NotEmpty(Input(PublishKind.AddRemote, "bad name", "https://github.com/alice/trag").RemoteError([]));
    }

    [Fact]
    public void WhenAnExistingRemoteIsChosen_ShouldNotJudgeTheRemoteName()
    {
        Assert.Empty(Input(PublishKind.ExistingRemote, "bad name").RemoteError([]));
    }

    [Fact]
    public void WhenTheRemoteToAddIsNamedLikeAnExistingOne_ShouldSayItAlreadyExists()
    {
        Assert.Contains("already exists", Input(PublishKind.CreateRepository).RemoteError([new("origin", "url", "url")]));
    }

    [Fact]
    public void WhenARemoteToAddHasNoAddressYet_ShouldShowNoRemoteError()
    {
        Assert.Empty(Input(PublishKind.AddRemote).RemoteError([]));
    }

    [Fact]
    public void WhenTheAddedAddressIsNotOneAddress_ShouldAskForOneRemoteAddress()
    {
        Assert.Contains("one remote address", Input(PublishKind.AddRemote, url: "two addresses").RemoteError([]));
    }

    // Git would keep the password or the token in .git/config in plain text, as the Remotes dialog says too.
    [Theory]
    [InlineData("https://alice:secret@github.com/alice/trag.git")]
    [InlineData("https://ghp_0123456789abcdef@github.com/alice/trag.git")]
    public void WhenTheAddedAddressCarriesAPasswordOrAToken_ShouldRefuseItAsTheRemotesDialogDoes(string url)
    {
        var input = Input(PublishKind.AddRemote, url: url);
        Assert.Equal((RemoteText.UrlHasPassword, false), (input.RemoteError([]), input.IsValid(Snapshots.Create())));
    }

    // While origin is taken the new remote is named after the repository and follows the name the user types, until the
    // user names the remote otherwise; with origin free it stays origin.
    [Fact]
    public void WhenTheRepositoryNameChanges_ShouldLetTheRemoteFollowItOnlyWhileOriginIsTakenAndTheRemoteIsUntouched()
    {
        var taken = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.Equal(("trag-next", "mine", "trag"), (PublishInput.RemoteNameFollowing(taken, "trag", "trag", "trag-next"),
            PublishInput.RemoteNameFollowing(taken, "mine", "trag", "trag-next"), PublishInput.RemoteNameFollowing(Snapshots.Create(), "trag", "trag", "trag-next")));
    }

    // Only an added address is judged.
    [Fact]
    public void WhenANewRepositoryIsCreated_ShouldNotJudgeTheAddressField()
    {
        Assert.Empty(Input(PublishKind.CreateRepository, url: "two addresses").RemoteError([]));
    }

    [Fact]
    public void WhenAnExistingRemoteAndItsBranchAreChosen_ShouldBeValid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.True(Input(PublishKind.ExistingRemote).IsValid(snapshot));
    }

    [Fact]
    public void WhenNoTargetBranchIsChosen_ShouldBeInvalid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.False((Input(PublishKind.ExistingRemote) with { TargetBranch = null }).IsValid(snapshot));
    }

    [Fact]
    public void WhenNoExistingRemoteIsChosen_ShouldBeInvalid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.False((Input(PublishKind.ExistingRemote) with { ExistingRemote = null }).IsValid(snapshot));
    }

    [Fact]
    public void WhenARemoteToAddHasNoAddressYet_ShouldBeInvalid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.False(Input(PublishKind.AddRemote, "upstream").IsValid(snapshot));
    }

    [Fact]
    public void WhenARemoteToAddHasAnAddress_ShouldBeValid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.True(Input(PublishKind.AddRemote, "upstream", "https://github.com/alice/trag").IsValid(snapshot));
    }

    // The snapshot has origin already.
    [Fact]
    public void WhenANewRepositorysRemoteNameIsTaken_ShouldBeInvalid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.False(Input(PublishKind.CreateRepository).IsValid(snapshot));
    }

    [Fact]
    public void WhenANewRepositorysRemoteNameIsFree_ShouldBeValid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.True(Input(PublishKind.CreateRepository, "upstream").IsValid(snapshot));
    }

    [Fact]
    public void WhenAPushIsAskedForWithoutACommit_ShouldBeInvalid()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.False(Input(PublishKind.CreateRepository, "upstream").IsValid(snapshot with { Head = null }));
    }

    [Fact]
    public void WhenNoPushIsAskedFor_ShouldBeValidWithoutACommit()
    {
        var snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        Assert.True((Input(PublishKind.CreateRepository, "upstream") with { Push = false }).IsValid(snapshot with { Head = null }));
    }

    // Origin is chosen though another remote comes first.
    [Fact]
    public void WhenTheRepositoryHasRemotes_ShouldDefaultToPushingToOrigin()
    {
        var withRemote = PublishInput.Defaults(Snapshots.Create(remotes: [Snapshots.Remote("backup"), Snapshots.Remote()]), "My Repo");
        Assert.Equal(PublishKind.ExistingRemote, withRemote.Kind); Assert.Equal("origin", withRemote.ExistingRemote); Assert.True(withRemote.Push);
    }

    // A repository created beside the remotes there are is added under its own name while origin is taken, so the form
    // does not start with a name already in use.
    [Fact]
    public void WhenOriginIsTaken_ShouldNameTheNewRemoteAfterTheRepositoryAndOtherwiseOrigin()
    {
        Assert.Equal("My-Repo", PublishInput.Defaults(Snapshots.Create(remotes: [Snapshots.Remote("backup"), Snapshots.Remote()]), "My Repo").RemoteName);
        Assert.Equal("origin", PublishInput.Defaults(Snapshots.Create(remotes: [Snapshots.Remote("backup")]), "My Repo").RemoteName);
        Assert.Equal("My-Repo", PublishInput.Defaults(Snapshots.Create(remotes: [Snapshots.Remote("Origin")]), "My Repo").RemoteName);
    }

    [Fact]
    public void WhenTheRepositoryHasNoRemoteAndNoCommit_ShouldDefaultToCreatingOriginWithoutAPush()
    {
        var bare = PublishInput.Defaults(Snapshots.Create(unborn: true), "My Repo");
        Assert.Equal(PublishKind.CreateRepository, bare.Kind); Assert.False(bare.Push); Assert.Equal("origin", bare.RemoteName);
    }

    [Fact]
    public void WhenANewRepositoryIsConfirmed_ShouldCarryTheTrimmedNamesTheAccountAndTheOwner()
    {
        var create = (Input(PublishKind.CreateRepository, " upstream ") with { RepositoryName = " trag ", IsPrivate = false }).ToOperation("main", "github:1", "acme");
        Assert.Equal(new PublishOperation(PublishKind.CreateRepository, "upstream", "main", true, AccountId: "github:1", OwnerId: "acme", RepositoryName: "trag", IsPrivate: false), create);
    }

    [Fact]
    public void WhenARemoteIsAdded_ShouldCarryTheTrimmedAddressAndNoAccountOrOwner()
    {
        var add = Input(PublishKind.AddRemote, "upstream", " https://github.com/alice/trag ").ToOperation("main", null, "ignored");
        Assert.Equal(new PublishOperation(PublishKind.AddRemote, "upstream", "main", true, RemoteUrl: "https://github.com/alice/trag"), add);
    }

    [Fact]
    public void WhenAnExistingRemoteIsConfirmed_ShouldCarryTheRemoteBranch()
    {
        Assert.Equal(new PublishOperation(PublishKind.ExistingRemote, "origin", "main", true, RemoteBranch: "main"), Input(PublishKind.ExistingRemote).ToOperation("main", "x", "y"));
    }
}
