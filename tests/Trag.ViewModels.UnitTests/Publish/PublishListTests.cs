using Trag.Application.Publish;
using Trag.Domain.Accounts;
using Trag.TestSupport;
using Trag.ViewModels.Publish;

namespace Trag.ViewModels.UnitTests.Publish;

// The selections over the owner and account-choice lists; the lists themselves are tested in Trag.Application.UnitTests and
// Trag.Domain.UnitTests.
public sealed class PublishListTests
{
    [Fact]
    public void WhenTheOwnersAreListed_ShouldSelectThePersonalAccountAndOfferAChoiceOnlyWithOrganizations()
    {
        var personal = PublishOwnerList.ForAccount(FakeAccountService.GitHub(user: "alice"));
        var owners = personal.WithOrganizations([new("b", "beta"), new("a", "Alpha")]);

        Assert.Equal(["alice", "a", "b"], owners.Items.Select(owner => owner.Id));
        Assert.Same(owners.Items[0], owners.Selected);
        Assert.True(owners.HasChoice);
        Assert.False(personal.HasChoice);
    }

    [Fact]
    public void WhenOneAccountMatchesTheAddress_ShouldChooseItButKeepAnExplicitExternalChoice()
    {
        var accounts = new[] { FakeAccountService.GitHub(), FakeAccountService.GitLab() };
        var choices = PublishUrlAccountList.Create("https://github.com/alice/trag", accounts);

        Assert.Equal(new[] { AccountChoice.External, accounts[0].Id }, choices.Items.Select(item => item.Id));
        Assert.Equal(accounts[0].Id, choices.AccountId);
        choices.Selected = choices.Items[0];
        choices = PublishUrlAccountList.Create("https://github.com/alice/other", accounts, choices.Selected.Id);
        Assert.Equal(AccountChoice.External, choices.Selected!.Id);
        Assert.Null(choices.AccountId);
    }

    [Fact]
    public void WhenSeveralAccountsMatchTheAddress_ShouldWaitForAChoiceAndFallBackToTheRemainingAccountWhenItIsRemoved()
    {
        var alice = FakeAccountService.GitHub("alice", "alice");
        var bob = FakeAccountService.GitHub("bob", "bob");
        const string url = "https://github.com/team/trag";
        var choices = PublishUrlAccountList.Create(url, [alice, bob]);
        Assert.Equal(AccountChoice.External, choices.Selected!.Id);
        Assert.Null(choices.AccountId);

        choices = PublishUrlAccountList.Create(url, [alice, bob], bob.Id);
        Assert.Equal(bob.Id, choices.AccountId);
        choices = PublishUrlAccountList.Create(url, [alice], bob.Id);
        Assert.Equal(alice.Id, choices.AccountId);
        choices = PublishUrlAccountList.Create(url, [], alice.Id);
        Assert.False(choices.HasChoices);
        Assert.Null(choices.Selected);
        Assert.Null(choices.AccountId);
    }
}
