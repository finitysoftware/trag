using Trag.Application.Accounts;
using Trag.Application.Catalog;
using Trag.Application.Live;
using Trag.Application.Ports.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Errors;
using Trag.Domain.Publish;
using Trag.TestSupport;
using Trag.ViewModels.Repositories;

namespace Trag.ViewModels.UnitTests.Repositories;

public sealed class AccountGroupTests
{
    [Fact]
    public async Task WhenTheGroupLoadsItsRepositories_ShouldSayWhatTheLoadIsDoingAndWhatCameBack()
    {
        var directory = new FakeRepositoryCatalog();
        var account = FakeAccountService.GitHub(user: "contoso");
        var group = new AccountGroup(new HostedRepositories(new AccountsModel(directory), account, new CancelRegistry()), TimeProvider.System);
        Assert.Equal("GitHub · contoso (0)", group.HeaderText); Assert.Equal("Load repositories", group.LoadText); Assert.Equal("", group.Message);

        await group.LoadCommand.ExecuteAsync(null);
        Assert.True(group.IsLoaded); Assert.Equal("Reload repositories", group.LoadText);
        Assert.Equal("No repositories were returned for this account.", group.Message);

        directory.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await group.LoadCommand.ExecuteAsync(null);
        Assert.Equal("GitHub · contoso (1)", group.HeaderText); Assert.Equal("", group.Message);
        Assert.Equal("contoso", group.Rows.Single().Account.UserName);
    }

    // The account's own exception speaks; the shell's Cancel reports the interruption; the catalog's Cancel says nothing.
    [Fact]
    public async Task WhenALoadFailsOrIsCanceled_ShouldSayWhyUnlessTheCatalogCanceledIt()
    {
        var directory = new HeldDirectory();
        var cancels = new CancelRegistry();
        var hosted = new HostedRepositories(new AccountsModel(directory), FakeAccountService.GitHub(), cancels);
        var group = new AccountGroup(hosted, TimeProvider.System);
        var loading = group.LoadCommand.ExecuteAsync(null);
        Assert.True(group.IsLoading); Assert.Equal("Loading repositories…", group.Message); Assert.False(group.LoadCommand.CanExecute(null));
        directory.Pending.SetException(new AccountException("Token expired.")); await loading;
        Assert.Equal("Token expired.", group.Message); Assert.False(group.IsLoaded); Assert.False(group.IsLoading);

        directory.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loading = group.LoadCommand.ExecuteAsync(null);
        Assert.True(cancels.CanCancel);
        cancels.CancelAll();
        Assert.True(directory.Token.IsCancellationRequested); Assert.False(group.IsLoading);
        directory.Pending.SetResult(new([], ReachedLimit: false)); await loading;
        Assert.Equal("Loading canceled.", group.Message); Assert.Empty(group.Rows);

        directory.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loading = group.LoadCommand.ExecuteAsync(null);
        hosted.Cancel();
        directory.Pending.SetException(new IOException("late")); await loading;
        Assert.Equal("", group.Message); Assert.False(group.IsLoading);
    }

    // An empty answer changes the load button too.
    [Fact]
    public async Task WhenALoadAnswers_ShouldAnnounceTheLoadButtonAndForNewRowsTheHeaderAndTheCountText()
    {
        var directory = new FakeRepositoryCatalog();
        var account = FakeAccountService.GitHub(user: "contoso");
        var group = new AccountGroup(new HostedRepositories(new AccountsModel(directory), account, new CancelRegistry()), TimeProvider.System);
        var changes = new List<string?>();
        group.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await group.LoadCommand.ExecuteAsync(null);
        Assert.Contains(nameof(AccountGroup.LoadText), changes);

        changes.Clear();
        directory.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await group.LoadCommand.ExecuteAsync(null);
        Assert.Superset(new HashSet<string?> { nameof(AccountGroup.Rows), nameof(AccountGroup.CountText), nameof(AccountGroup.HeaderText) },
            changes.ToHashSet());
    }

    [Fact]
    public async Task WhenTragReadAllThePagesItReads_ShouldSayUnderTheHeaderThatItListsOnlyTheMostRecentlyActive()
    {
        var directory = new HeldDirectory();
        var group = new AccountGroup(new HostedRepositories(new AccountsModel(directory), FakeAccountService.GitHub(user: "contoso"), new CancelRegistry()), TimeProvider.System);
        var loading = group.LoadCommand.ExecuteAsync(null);
        directory.Pending.SetResult(new([new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)], ReachedLimit: true));
        await loading;
        Assert.Equal("Trag lists at most 1,000 repositories, the most recently active ones.", group.Message);
    }

    private sealed class HeldDirectory : IRemoteRepositoryDirectory
    {
        public TaskCompletionSource<RemoteRepositoryList> Pending { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AccountInfo>>([]);
        public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct)
        {
            Token = ct;
            return Pending.Task;
        }
        public Task<IReadOnlyList<PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PublishOwner>>([]);
    }
}
