using Trag.Domain.Accounts;

namespace Trag.Domain.UnitTests.Accounts;

public sealed class RemoteAccountStateTests
{
    private static readonly IReadOnlyList<AccountChoice> Choices =
        [new(AccountChoice.External, "Git"), new("alice", "alice · github.com"), new("bob", "bob · github.com")];

    [Fact]
    public void WhenAccountsCompete_ShouldShowTheChoiceWithTheSavedOne()
    {
        var saved = Saved();
        Assert.True(saved.HasChoices); Assert.Equal("alice", saved.Saved?.Id);
    }

    [Fact]
    public void WhenTheSavedAccountIsSelected_ShouldBeConfirmedAndNothingUnsaved()
    {
        var saved = Saved();
        Assert.True(saved.IsConfirmed("alice")); Assert.False(saved.IsUnsaved("alice"));
    }

    [Fact]
    public void WhenAnotherAccountIsSelected_ShouldBeUnsavedAndNotConfirmed()
    {
        var saved = Saved();
        Assert.True(saved.IsUnsaved("bob")); Assert.False(saved.IsConfirmed("bob"));
    }

    [Fact]
    public void WhenNothingIsSelected_ShouldBeNeitherConfirmedNorUnsaved()
    {
        var saved = Saved();
        Assert.False(saved.IsConfirmed(null)); Assert.False(saved.IsUnsaved(null));
    }

    [Fact]
    public void WhenOnlyOneAccountCouldServe_ShouldShowNoChoice()
    {
        var single = OneAccount();
        Assert.False(single.HasChoices);
    }

    [Fact]
    public void WhenTheSavedAccountIsNoChoice_ShouldHaveNoSavedChoice()
    {
        var saved = Saved();
        Assert.Null((saved with { SelectedId = "removed" }).Saved);
    }

    // A repository copied from another computer remembers Git's own credentials; the one account that could serve the
    // remote must stay reachable, else the only way back is editing the Git configuration by hand.
    [Fact]
    public void WhenGitsOwnCredentialsAreSavedAndOneAccountCouldServe_ShouldStillOfferTheChoice()
    {
        var external = new RemoteAccountState(Choices.Take(2).ToArray(), AccountChoice.External, "");
        Assert.True(external.HasChoices);
    }

    [Fact]
    public void WhenGitsOwnCredentialsAreSavedAndNoAccountCouldServe_ShouldOfferNoChoice()
    {
        Assert.False(new RemoteAccountState(Choices.Take(1).ToArray(), AccountChoice.External, "").HasChoices);
    }

    // Git's own credentials and two accounts, alice saved.
    private static RemoteAccountState Saved() => new(Choices, "alice", "");

    // Git's own credentials and alice, saved.
    private static RemoteAccountState OneAccount() => new(Choices.Take(2).ToArray(), "alice", "");
}
