using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Application.Sync;
using Trag.Application.Workspace;
using Trag.Domain.Accounts;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Sync;

// A push or publish that found the remote's repository missing on its host, when the remote's address names it under
// the user's own Trag account there: Trag offers to create it, private, and to publish the branch to it as the push
// would have. Every write answers the offer again, so a write that went through withdraws it, and so does a snapshot in
// which the remote's address no longer names the repository offered.
public sealed class MissingRepositoryModelTests
{
    private const string Address = "https://github.com/nadia/sample-app";
    private static readonly AccountInfo Account = FakeAccountService.GitHub(user: "nadia");
    private static readonly GitWriteResult Missing = GitWriteResult.Rejected("The host has no repository at this address.") with { MissingRepository = true };

    [Fact]
    public async Task WhenAPublishFindsTheRepositoryMissingUnderTheAccountsOwnName_ShouldOfferToCreateItPrivateAndPublishTheBranchToIt()
    {
        var (workspace, repository, writer, missing) = await OpenAsync(Snapshots.Create(branch: "trunk", remotes: [new("sample-app", Address, Address)]));
        await using var _ = workspace;
        writer.Result = Missing;

        await repository.Remotes.ConfirmPublish(new(PublishKind.ExistingRemote, "", "", "", "sample-app", "hybrid", Push: true), null, null)!.RunAsync();
        await missing.Completion;

        var publish = new PublishOperation(PublishKind.ExistingRemote, "sample-app", "trunk", true, AccountId: Account.Id,
            RepositoryName: "sample-app", IsPrivate: true, RemoteBranch: "hybrid");
        Assert.Equal(new MissingRepository(new(Account, "sample-app"), publish), missing.Offer);
        writer.Result = new(GitWriteOutcome.Succeeded, "Published trunk.");
        await missing.CreateAsync();
        await missing.Completion;
        Assert.Equal(publish, Assert.IsType<PublishRequest>(writer.LastRequest).Publish);
        Assert.Null(missing.Offer);
    }

    [Fact]
    public async Task WhenAPushFindsTheRepositoryMissing_ShouldOfferToPublishTheBranchToTheBranchItTracks()
    {
        var (workspace, repository, writer, missing) = await OpenAsync(Snapshots.Create(branch: "trunk", trackingRemote: "sample-app",
            upstreamBranchRef: "refs/heads/trunk", remotes: [new("sample-app", Address, Address)]));
        await using var _ = workspace;
        writer.Result = Missing;

        await repository.Remotes.PushAsync();
        await missing.Completion;

        Assert.Equal(new PublishOperation(PublishKind.ExistingRemote, "sample-app", "trunk", true, AccountId: Account.Id,
            RepositoryName: "sample-app", IsPrivate: true, RemoteBranch: "trunk"), missing.Offer?.Publish);
    }

    // The repository was just created; a push after it that is answered as missing cannot be helped by creating it again.
    [Fact]
    public async Task WhenThePushAfterCreatingTheRepositoryIsAnsweredMissing_ShouldOfferNothing()
    {
        var (workspace, repository, writer, missing) = await OpenAsync(Snapshots.Create(branch: "trunk", remotes: [new("sample-app", Address, Address)]));
        await using var _ = workspace;
        writer.Result = Missing;
        await repository.Remotes.ConfirmPublish(new(PublishKind.ExistingRemote, "", "", "", "sample-app", null, Push: true), null, null)!.RunAsync();
        await missing.Completion;

        await missing.CreateAsync();
        await missing.Completion;

        Assert.Equal("sample-app", Assert.IsType<PublishRequest>(writer.LastRequest).Publish.RepositoryName);
        Assert.Null(missing.Offer);
    }

    [Fact]
    public async Task WhenTheMissingRepositoryIsUnderAnotherOwner_ShouldOfferNothing()
    {
        const string organization = "https://github.com/acme/sample-app";
        var (workspace, repository, writer, missing) = await OpenAsync(Snapshots.Create(branch: "trunk", remotes: [new("sample-app", organization, organization)]));
        await using var _ = workspace;
        writer.Result = Missing;

        await repository.Remotes.ConfirmPublish(new(PublishKind.ExistingRemote, "", "", "", "sample-app", null, Push: true), null, null)!.RunAsync();
        await missing.Completion;

        Assert.Null(missing.Offer);
    }

    // A fetch sends nothing, so it has nothing to publish once the repository exists.
    [Fact]
    public async Task WhenAFetchFindsTheRepositoryMissing_ShouldOfferNothing()
    {
        var (workspace, repository, writer, missing) = await OpenAsync(Snapshots.Create(branch: "trunk", trackingRemote: "sample-app",
            upstreamBranchRef: "refs/heads/trunk", remotes: [new("sample-app", Address, Address)]));
        await using var _ = workspace;
        writer.Result = Missing;

        await repository.Remotes.FetchAsync();
        await missing.Completion;

        Assert.Null(missing.Offer);
    }

    // The accounts answer only after another write has answered the offer again: the late answer belongs to a write that
    // is no longer the last one, so it offers nothing.
    [Fact]
    public async Task WhenAnotherWriteEndsWhileTheAccountsAreAsked_ShouldNotOfferForTheEarlierWrite()
    {
        var catalog = new FakeRepositoryCatalog { Accounts = { Account } };
        var (workspace, repository, writer, missing) = await OpenAsync(new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: "trunk",
            trackingRemote: "sample-app", upstreamBranchRef: "refs/heads/trunk", remotes: [new("sample-app", Address, Address)]) }, catalog);
        await using var _ = workspace;
        writer.Result = Missing;
        catalog.AccountsGate.IsClosed = true;
        await repository.Remotes.PushAsync();
        await catalog.AccountsGate.Started;

        writer.Result = new(GitWriteOutcome.Succeeded, "Fetched.");
        await repository.Remotes.FetchAsync();
        catalog.AccountsGate.Open();
        await missing.Completion;

        Assert.Null(missing.Offer);
    }

    // The user fixes the remote's address in a terminal and refreshes: the repository offered is no longer the one the
    // remote names, while a refresh that keeps the address keeps the offer.
    [Fact]
    public async Task WhenARefreshBringsAnotherAddressForTheRemote_ShouldWithdrawTheOffer()
    {
        const string typo = "https://github.com/nadia/sampel-app";
        var reader = new FakeRepositoryReader { Snapshot = Snapshots.Create(branch: "trunk", remotes: [new("sample-app", typo, typo)]) };
        var (workspace, repository, writer, missing) = await OpenAsync(reader);
        await using var _ = workspace;
        writer.Result = Missing;
        await repository.Remotes.ConfirmPublish(new(PublishKind.ExistingRemote, "", "", "", "sample-app", null, Push: true), null, null)!.RunAsync();
        await missing.Completion;

        reader.Snapshot = reader.Snapshot with { IndexFingerprint = "edited" };
        await RefreshAsync(workspace, repository);
        Assert.NotNull(missing.Offer);
        reader.Snapshot = reader.Snapshot with { Remotes = [new("sample-app", Address, Address)] };
        await RefreshAsync(workspace, repository);

        Assert.Null(missing.Offer);
    }

    private static Task<(RepositoryWorkspace Workspace, Repository Repository, FakeGitWriter Writer, MissingRepositoryModel Missing)> OpenAsync(RepositorySnapshot snapshot) =>
        OpenAsync(new FakeRepositoryReader { Snapshot = snapshot });

    private static async Task<(RepositoryWorkspace Workspace, Repository Repository, FakeGitWriter Writer, MissingRepositoryModel Missing)> OpenAsync(
        FakeRepositoryReader reader, FakeRepositoryCatalog? catalog = null)
    {
        var writer = new FakeGitWriter();
        var workspace = new RepositoryWorkspace(reader, writer: writer, synchronizer: new FakeGitSync());
        var repository = new Repository(new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher()));
        var missing = new MissingRepositoryModel(repository.Remotes, new AccountsModel(catalog ?? new FakeRepositoryCatalog { Accounts = { Account } }));
        repository.Apply((await workspace.OpenAsync("fixture", CancellationToken.None))!); await repository.Remotes.Completion;
        return (workspace, repository, writer, missing);
    }

    private static async Task RefreshAsync(RepositoryWorkspace workspace, Repository repository)
    {
        repository.Apply((await workspace.RefreshAsync(CancellationToken.None))!);
        repository.CompleteRefresh();
    }
}
