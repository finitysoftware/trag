using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Publish;

public sealed class PublishOwnersTests
{
    // Organizations are deduplicated before sorting, so the first of two with the same id keeps its name; one without an id
    // and the account itself listed as an organization are left out.
    [Fact]
    public void WhenTheAccountsOrganizationsAreAdded_ShouldPutTheAccountFirstAndEachOrganizationOnceSorted()
    {
        var personal = PublishOwners.ForAccount(FakeAccountService.GitHub(user: "alice"));
        var owners = PublishOwners.With(personal, [
            new("b", "beta"), new("a", "Alpha"), new("b", "duplicate"), new("", "invalid"), new("alice", "alice", true)]);

        Assert.Equal(["alice", "a", "b"], owners.Select(owner => owner.Id));
        Assert.True(owners[0].IsUser);
        Assert.Equal("beta", owners[2].Display);
    }

    [Fact]
    public void WhenThereIsNoAccount_ShouldOfferNoOwner()
    {
        Assert.Empty(PublishOwners.ForAccount(null));
    }

    [Fact]
    public void WhenTheAccountIsMissing_ShouldOfferNoneOfItsOrganizations()
    {
        Assert.Empty(PublishOwners.With([], [new("a", "Alpha")]));
    }

    [Fact]
    public void WhenTheOwnerIsTheAccount_ShouldNameNoOrganization()
    {
        Assert.Null(new PublishOwner("alice", "alice", IsUser: true).OrganizationId);
    }

    [Fact]
    public void WhenTheOwnerIsAnOrganization_ShouldNameItself()
    {
        Assert.Equal("acme", new PublishOwner("acme", "Acme").OrganizationId);
    }
}
