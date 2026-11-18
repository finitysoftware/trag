using Trag.Domain.Accounts;

namespace Trag.Domain.UnitTests.Accounts;

// Which account a remote is used with. Trag never guesses between identities: anything but one obvious answer leaves
// Git's own credentials in charge, and the sentence always says which of the four reasons applied.
public sealed class RemoteAccountSelectionTests
{
    private const string Fetch = "https://gitlab.example/group/repo.git";
    private const string Ssh = "git@gitlab.example:group/repo.git";
    private static readonly AccountInfo Alice = Account("alice", "1");
    private static readonly AccountInfo Bob = Account("bob", "2");

    private static AccountInfo Account(string name, string userId) =>
        new("account-" + userId, AccountProvider.GitLab, userId, name, new("https://gitlab.example/api/v4/"),
            new("https://gitlab.example/"), true, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void WhenNoAccountMatches_ShouldLeaveGitsOwnCredentialsInCharge()
    {
        var state = RemoteAccountSelection.Choose([], Fetch, Fetch, null);
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.Equal("Using your existing Git credentials. SSH uses its configured agent.", state.Message);
        Assert.Equal(AccountChoice.External, Assert.Single(state.Choices).Id);
    }

    [Fact]
    public void WhenExactlyOneAccountMatches_ShouldUseItAndOfferGitsOwnCredentialsBesideIt()
    {
        var state = RemoteAccountSelection.Choose([Alice], Fetch, Fetch, null);
        Assert.Equal(Alice.Id, state.SelectedId);
        Assert.Equal("Using " + Alice.Display + ", the only account matching this remote. Pick another choice below to change it.", state.Message);
        Assert.Equal([AccountChoice.External, Alice.Id], state.Choices.Select(c => c.Id));
    }

    [Fact]
    public void WhenTwoAccountsMatch_ShouldOfferBothButChooseNeither()
    {
        var state = RemoteAccountSelection.Choose([Alice, Bob], Fetch, Fetch, null);
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.Equal(3, state.Choices.Count);
        Assert.Equal("Using your existing Git credentials. SSH uses its configured agent.", state.Message);
    }

    [Fact]
    public void WhenAnAccountMatchesOnlyOneOfTheTwoDestinations_ShouldNotOfferIt()
    {
        var state = RemoteAccountSelection.Choose([Alice], Fetch, "https://elsewhere.example/group/repo.git", null);
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.Equal(AccountChoice.External, Assert.Single(state.Choices).Id);
    }

    // Alice stays offered so the user can switch without editing Git's configuration.
    [Fact]
    public void WhenGitsOwnCredentialsAreTheSavedChoice_ShouldWinOverTheOnlyMatchingAccount()
    {
        var state = RemoteAccountSelection.Choose([Alice], Fetch, Fetch, AccountChoice.External);
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.True(state.HasChoices);
    }

    [Fact]
    public void WhenOneOfTwoMatchingAccountsIsSaved_ShouldUseItAndSaySo()
    {
        var saved = RemoteAccountSelection.Choose([Alice, Bob], Fetch, Fetch, Bob.Id);
        Assert.Equal(Bob.Id, saved.SelectedId);
        Assert.Equal("Using the saved account for this repository.", saved.Message);
    }

    [Fact]
    public void WhenTheSavedAccountIsGone_ShouldFallBackToGitRatherThanToAnotherIdentity()
    {
        var state = RemoteAccountSelection.Choose([Alice], Fetch, Fetch, "account-removed");
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.Equal("Using your existing Git credentials. SSH uses its configured agent.", state.Message);
    }

    [Fact]
    public void WhenNoAccountIsSavedForAnSshRemote_ShouldOfferEveryAccountButUseNone()
    {
        var unsaved = RemoteAccountSelection.Choose([Alice, Bob], Ssh, Ssh, null);
        Assert.Equal(AccountChoice.External, unsaved.SelectedId);
        Assert.Equal(3, unsaved.Choices.Count);
    }

    // Either destination over SSH is enough: that side's key decides whatever account is saved.
    [Fact]
    public void WhenOnlyThePushDestinationIsSsh_ShouldStillSayTheKeyDecides()
    {
        var saved = RemoteAccountSelection.Choose([Alice, Bob], Fetch, Ssh, Alice.Id);
        Assert.Equal("SSH uses its configured key independently of the hosting account.", saved.Message);
    }

    [Fact]
    public void WhenAnAccountIsSavedForAnSshRemote_ShouldUseItAndSayTheKeyStillDecides()
    {
        var saved = RemoteAccountSelection.Choose([Alice, Bob], Ssh, Ssh, Alice.Id);
        Assert.Equal(Alice.Id, saved.SelectedId);
        Assert.Equal("SSH uses its configured key independently of the hosting account.", saved.Message);
    }

    [Theory]
    [InlineData("ssh://git@gitlab.example/group/repo.git")]
    [InlineData("git@gitlab.example:group/repo.git")]
    public void WhenAnSshRemoteIsWrittenEitherWay_ShouldRecognizeIt(string address)
    {
        var state = RemoteAccountSelection.Choose([Alice], address, address, Alice.Id);
        Assert.Equal("SSH uses its configured key independently of the hosting account.", state.Message);
    }

    [Fact]
    public void WhenTheRemoteIsALocalPath_ShouldOfferNothing()
    {
        var state = RemoteAccountSelection.Choose([Alice], "../other-repo", "../other-repo", null);
        Assert.Equal(AccountChoice.External, state.SelectedId);
        Assert.Equal(AccountChoice.External, Assert.Single(state.Choices).Id);
    }
}
