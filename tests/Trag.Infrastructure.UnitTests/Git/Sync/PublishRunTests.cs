using Trag.Domain.Accounts;
using Trag.Domain.Publish;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Sync;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Sync;

// What a publish has already done is a few facts; the sentence that tells the user where it stopped follows from them.
public sealed class PublishRunTests
{
    private static PublishRun Run(PublishKind kind = PublishKind.AddRemote, string? accountId = null, string? remoteBranch = null, string? repository = "repo") =>
        new(new PublishRequest(Snapshots.Create(branch: "topic"), new(kind, " origin ", "topic", true, RemoteUrl: " https://example.invalid/repo.git ",
            AccountId: accountId, RepositoryName: repository, RemoteBranch: remoteBranch)));

    // A typed remote added, and the sentence read once after that step.
    private static PublishRun WithRemoteAdded()
    {
        var run = Run();
        run.RemoteAdded = true;
        _ = run.Changed;
        return run;
    }

    // A repository created on the provider, and the sentence read once after that step.
    private static PublishRun WithRepositoryCreated()
    {
        var run = Run(PublishKind.CreateRepository, accountId: "github:1");
        run.Created = "you/repo";
        _ = run.Changed;
        return run;
    }

    // ...then added as the remote, and the sentence read again.
    private static PublishRun WithCreatedRepositoryAdded()
    {
        var run = WithRepositoryCreated();
        run.RemoteAdded = true;
        _ = run.Changed;
        return run;
    }

    [Fact]
    public void WhenNoRemoteBranchIsGiven_ShouldTrimTheNamesAndKeepTheBranchsOwnNameOnTheRemote()
    {
        var run = Run();
        Assert.Equal("origin", run.Remote);
        Assert.Equal("topic", run.Target);
        Assert.Equal("origin/topic", run.Upstream);
        Assert.Equal("https://example.invalid/repo.git", run.Address);
    }

    [Fact]
    public void WhenARemoteBranchIsGiven_ShouldTrackTheBranchUnderThatTrimmedName() =>
        Assert.Equal("origin/shared/topic", Run(remoteBranch: " shared/topic ").Upstream);

    [Fact]
    public void WhenNothingIsDoneYet_ShouldHaveNoChangeAndNoPrefix()
    {
        var run = Run();
        Assert.Equal("", run.Changed);
        Assert.Equal("", run.Prefix);
    }

    [Fact]
    public void WhenTheRemoteIsAdded_ShouldNameIt()
    {
        var run = Run();
        run.RemoteAdded = true;
        Assert.Equal("Remote origin was added.", run.Changed);
    }

    [Fact]
    public void WhenTheUpstreamIsRecordedAfterTheRemote_ShouldNameBothInOrderAndPrefixWithThem()
    {
        var run = WithRemoteAdded();
        run.UpstreamRecorded = true;
        Assert.Equal("Remote origin was added. topic now tracks origin/topic.", run.Changed);
        Assert.Equal("Remote origin was added. topic now tracks origin/topic. ", run.Prefix);
    }

    [Fact]
    public void WhenTheRepositoryIsCreatedButTheRemoteIsNotAdded_ShouldNameTheCreatedRepository()
    {
        var run = Run(PublishKind.CreateRepository, accountId: "github:1");
        run.Created = "you/repo";
        Assert.Equal("you/repo was created on the provider.", run.Changed);
    }

    [Fact]
    public void WhenTheCreatedRepositoryIsAddedAsTheRemote_ShouldSayItWasCreatedAndAdded()
    {
        var run = WithRepositoryCreated();
        run.RemoteAdded = true;
        Assert.Equal("you/repo was created and added as origin.", run.Changed);
    }

    [Fact]
    public void WhenTheUpstreamIsRecordedAfterTheCreatedRemote_ShouldNameTheTrackingToo()
    {
        var run = WithCreatedRepositoryAdded();
        run.UpstreamRecorded = true;
        Assert.Equal("you/repo was created and added as origin. topic now tracks origin/topic.", run.Changed);
    }

    [Fact]
    public void WhenARepositoryIsCreated_ShouldRememberTheAccountThatCreatedIt() =>
        Assert.Equal("github:1", Run(PublishKind.CreateRepository, accountId: "github:1").AccountId);

    [Fact]
    public void WhenARemoteIsAddedWithATragAccount_ShouldRememberThatAccount() =>
        Assert.Equal("gitlab:2", Run(PublishKind.AddRemote, accountId: "gitlab:2").AccountId);

    [Fact]
    public void WhenARemoteIsAddedWithGitsOwnCredentials_ShouldRememberNoAccount() =>
        Assert.Null(Run(PublishKind.AddRemote, accountId: AccountChoice.External).AccountId);

    // An existing remote keeps whatever account it already has.
    [Fact]
    public void WhenPublishingToAnExistingRemote_ShouldRememberNoAccount() =>
        Assert.Null(Run(PublishKind.ExistingRemote, accountId: "github:1", repository: null).AccountId);

    // The repository an existing remote names was missing and is created for it: the push that follows, and every one
    // after it, goes with the account that created it, which the credentials the remote had may not be.
    [Fact]
    public void WhenTheRepositoryOfAnExistingRemoteIsCreated_ShouldRememberTheAccountThatCreatedIt() =>
        Assert.Equal("github:1", Run(PublishKind.ExistingRemote, accountId: "github:1").AccountId);
}
