using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Application.Publish;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Publish;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Publish;

// The publish dialog's reads without a screen: the accounts in display order or why they are missing, and the owners of
// the chosen account — the account itself while its organizations load, the account's own error when they cannot.
public sealed class PublishAccountsModelTests
{
    [Fact]
    public async Task WhenTheAccountsAreRead_ShouldListThemInDisplayOrder()
    {
        var catalog = new FakeRepositoryCatalog();
        catalog.Accounts.AddRange([FakeAccountService.GitLab(), FakeAccountService.GitHub()]);
        var model = new PublishAccountsModel(new AccountsModel(catalog), new Repository());
        await model.ReadAccountsAsync();
        Assert.Equal([AccountProvider.GitHub, AccountProvider.GitLab], model.All.Value.Select(account => account.Provider));
    }

    [Fact]
    public async Task WhenTheAccountsCannotBeRead_ShouldSayTheyWereNotRead()
    {
        var catalog = new FakeRepositoryCatalog();
        var model = new PublishAccountsModel(new AccountsModel(catalog), new Repository());
        catalog.AccountsError = new IOException("offline");
        await model.ReadAccountsAsync();
        Assert.Equal(PublishText.AccountsNotRead, model.All.Message);
    }

    [Fact]
    public void WhenTheOwnersAreLoading_ShouldShowTheAccountItself()
    {
        var (catalog, model, account) = Owners();
        var held = new TaskCompletionSource<IReadOnlyList<PublishOwner>>();
        catalog.ReadOwners = (_, _) => held.Task;

        _ = model.ReadOwnersAsync(account);

        Assert.Equal(["alice"], model.Owners.Value.Items.Select(owner => owner.Id));
    }

    [Fact]
    public async Task WhenTheOwnersLoaded_ShouldShowTheAccountAndItsOrganizations()
    {
        var (catalog, model, account) = Owners();
        var held = new TaskCompletionSource<IReadOnlyList<PublishOwner>>();
        catalog.ReadOwners = (_, _) => held.Task;
        var reading = model.ReadOwnersAsync(account);

        held.SetResult([new("team", "The team")]);
        await reading;

        Assert.Equal(["alice", "team"], model.Owners.Value.Items.Select(owner => owner.Id));
    }

    [Fact]
    public async Task WhenTheOwnersCannotBeRead_ShouldShowTheAccountsOwnError()
    {
        var (catalog, model, account) = Owners();
        catalog.ReadOwners = (_, _) => Task.FromException<IReadOnlyList<PublishOwner>>(new AccountException("Token expired."));
        await model.ReadOwnersAsync(account);
        Assert.Equal("Token expired.", model.Owners.Message);
    }

    // The account chosen before had its owners shown, so the list has something to clear.
    [Fact]
    public async Task WhenNoAccountIsChosen_ShouldShowNoOwners()
    {
        var (_, model, account) = Owners();
        await model.ReadOwnersAsync(account);
        await model.ReadOwnersAsync(null);
        Assert.Empty(model.Owners.Value.Items);
    }

    [Fact]
    public async Task WhenBothReadsFail_ShouldSayWhyTheAccountsWereNotRead()
    {
        var (catalog, model, account) = Owners();
        catalog.AccountsError = new IOException("offline");
        catalog.ReadOwners = (_, _) => Task.FromException<IReadOnlyList<PublishOwner>>(new AccountException("Token expired."));
        await model.ReadAccountsAsync();
        await model.ReadOwnersAsync(account);
        Assert.Equal(PublishText.AccountsNotRead, model.Message);
    }

    [Fact]
    public async Task WhenOnlyTheOwnersCannotBeRead_ShouldSayTheOwnersReadsReason()
    {
        var (catalog, model, account) = Owners();
        catalog.ReadOwners = (_, _) => Task.FromException<IReadOnlyList<PublishOwner>>(new AccountException("Token expired."));
        await model.ReadAccountsAsync();
        await model.ReadOwnersAsync(account);
        Assert.Equal("Token expired.", model.Message);
    }

    [Fact]
    public async Task WhenTheOwnersOfTheChosenAccountAreRead_ShouldAllowCreatingFromIt()
    {
        var (_, model, account) = Owners();
        await model.ReadOwnersAsync(account);
        Assert.True(model.CanCreate(account));
    }

    [Fact]
    public async Task WhenNoAccountIsChosen_ShouldNotAllowCreating()
    {
        var (_, model, account) = Owners();
        await model.ReadOwnersAsync(account);
        Assert.False(model.CanCreate(null));
    }

    [Fact]
    public void WhenTheOwnersAreStillRead_ShouldBeBusyAndNotAllowCreating()
    {
        var (catalog, model, account) = Owners();
        catalog.ReadOwners = (_, _) => new TaskCompletionSource<IReadOnlyList<PublishOwner>>().Task;
        _ = model.ReadOwnersAsync(account);
        Assert.True(model.IsBusy);
        Assert.False(model.CanCreate(account));
    }

    // The owners read was cleared, so no owner is chosen although an account is.
    [Fact]
    public async Task WhenNoOwnerIsChosen_ShouldNotAllowCreating()
    {
        var (_, model, account) = Owners();
        await model.ReadOwnersAsync(account);
        await model.ReadOwnersAsync(null);
        Assert.False(model.CanCreate(account));
    }

    // The dialog's model over a fake catalog, and alice's account to read the owners of.
    private static (FakeRepositoryCatalog Catalog, PublishAccountsModel Model, AccountInfo Account) Owners()
    {
        var catalog = new FakeRepositoryCatalog();
        var model = new PublishAccountsModel(new AccountsModel(catalog), new Repository());
        return (catalog, model, FakeAccountService.GitHub("alice", "alice"));
    }
}
