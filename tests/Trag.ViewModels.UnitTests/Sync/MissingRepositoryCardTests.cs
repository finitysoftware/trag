using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Sync;

// The sync panel's card for a push that found the remote's repository missing under the user's own account: it says
// what is missing and how Trag would create it, and creates it and pushes when asked, while no other write runs.
public sealed class MissingRepositoryCardTests
{
    private const string Address = "https://github.com/nadia/sample-app";

    [Fact]
    public async Task WhenAPushFindsTheRepositoryMissing_ShouldSayWhatIsMissingAndCreateItAndPushOnRequest()
    {
        var workspace = new FakePublishWorkspace
        {
            Snapshot = Snapshots.Create(branch: "trunk", trackingRemote: "sample-app", upstreamBranchRef: "refs/heads/trunk", remotes: [new("sample-app", Address, Address)]),
            Result = GitWriteResult.Rejected("The host has no repository at this address.") with { MissingRepository = true }
        };
        await using var model = ShellHarness.Compose(workspace, catalog: new FakeRepositoryCatalog { Accounts = { FakeAccountService.GitHub(user: "nadia") } });
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.False(model.Sync.HasMissingRepository); Assert.False(model.Sync.CreateMissingRepositoryCommand.CanExecute(null));

        await model.Sync.PushCommand.ExecuteAsync(null); await model.Sync.MissingRepositoryCompletion;

        Assert.True(model.Sync.HasMissingRepository); Assert.True(model.Sync.CreateMissingRepositoryCommand.CanExecute(null));
        Assert.Equal(("sample-app was not found on GitHub", "Trag creates nadia/sample-app as a private repository with your GitHub account, then pushes trunk."),
            (model.Sync.MissingRepository!.Text, model.Sync.MissingRepository.Note));
        workspace.Gate.IsClosed = true;
        var pushing = model.Sync.PushCommand.ExecuteAsync(null); await workspace.Gate.Started;
        Assert.False(model.Sync.CreateMissingRepositoryCommand.CanExecute(null));
        workspace.Gate.Open(); await pushing; await model.Sync.MissingRepositoryCompletion;
        workspace.Result = new(GitWriteOutcome.Succeeded, "Published trunk.");
        await model.Sync.CreateMissingRepositoryCommand.ExecuteAsync(null); await model.Sync.MissingRepositoryCompletion;
        Assert.Equal("sample-app", Assert.IsType<PublishRequest>(workspace.LastRequest).Publish.RepositoryName);
        Assert.False(model.Sync.HasMissingRepository);
    }
}
