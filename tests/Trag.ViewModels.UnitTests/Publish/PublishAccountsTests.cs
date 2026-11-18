using Trag.Application.Accounts;
using Trag.Application.Live;
using Trag.Application.Publish;
using Trag.Domain.Errors;
using Trag.TestSupport;
using Trag.ViewModels.Publish;

namespace Trag.ViewModels.UnitTests.Publish;

public sealed class PublishAccountsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheOwnersOfAnEarlierAccountArriveLate_ShouldNotReplaceTheNewerSelectionOrItsMessage(bool failOldRead)
    {
        var alice = FakeAccountService.GitHub("alice", "alice");
        var bob = FakeAccountService.GitHub("bob", "bob");
        var oldRead = new AsyncGate { IsClosed = true };
        var newRead = new AsyncGate { IsClosed = true };
        var catalog = new FakeRepositoryCatalog { Accounts = { alice, bob } };
        catalog.ReadOwners = async (id, ct) =>
        {
            await (id == alice.Id ? oldRead : newRead).WaitAsync(ct);
            if (id == alice.Id && failOldRead) throw new AccountException("Old error");
            return [new(id + "-team", id + " team")];
        };
        var model = new PublishAccountsViewModel(new PublishAccountsModel(new AccountsModel(catalog), new Repository()));
        var load = model.LoadAsync();
        await oldRead.Started;
        Assert.False(model.All.IsBusy);
        Assert.True(model.Owners.IsBusy);
        Assert.False(model.CanCreateRepository);

        model.SelectedAccount = bob;
        await newRead.Started;
        var owners = model.Owners.Completion;
        Assert.True(oldRead.Token.IsCancellationRequested);
        newRead.Open();
        await owners;
        Assert.True(model.CanCreateRepository);
        Assert.Equal(new[] { "bob", "bob-team" }, model.Owners.Value.Items.Select(owner => owner.Id));

        oldRead.Open();
        await load;
        Assert.Same(bob, model.SelectedAccount);
        Assert.Equal(new[] { "bob", "bob-team" }, model.Owners.Value.Items.Select(owner => owner.Id));
        Assert.Equal("", model.Message);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task WhenTheAccountReadIsCancelledAndReloaded_ShouldShowTheCancellationAndIgnoreTheLateResult()
    {
        var oldRead = new AsyncGate { IsClosed = true };
        var catalog = new FakeRepositoryCatalog();
        catalog.ReadAccounts = async ct =>
        {
            await oldRead.WaitAsync(ct);
            return [FakeAccountService.GitHub("old")];
        };
        var repository = new Repository();
        var model = new PublishAccountsViewModel(new PublishAccountsModel(new AccountsModel(catalog), repository));
        var oldLoad = model.LoadAsync();
        await oldRead.Started;
        Assert.True(model.All.IsBusy);
        Assert.True(repository.Cancels.CanCancel);
        repository.Cancels.CancelAll();
        Assert.True(oldRead.Token.IsCancellationRequested);
        Assert.False(model.IsBusy);
        Assert.False(repository.Cancels.CanCancel);

        catalog.ReadAccounts = null;
        catalog.Accounts.Add(FakeAccountService.GitLab("new"));
        await model.LoadAsync();
        oldRead.Open();
        await oldLoad;
        Assert.Equal("new", Assert.Single(model.All.Value).Id);
        Assert.True(model.IsGitLab);
        Assert.Equal("new", model.SelectedAccount!.Id);
    }

    [Fact]
    public async Task WhenAnAddressIsTypedDuringTheAccountRead_ShouldMatchItToTheArrivingAccounts()
    {
        var catalog = new FakeRepositoryCatalog { Accounts = { FakeAccountService.GitHub() } };
        catalog.AccountsGate.IsClosed = true;
        var model = new PublishAccountsViewModel(new PublishAccountsModel(new AccountsModel(catalog), new Repository()));
        var load = model.LoadAsync();
        await catalog.AccountsGate.Started;
        model.RefreshUrlAccounts("https://github.com/alice/trag");
        Assert.Empty(model.UrlAccounts.Items);
        catalog.AccountsGate.Open();
        await load;
        Assert.Equal(catalog.Accounts[0].Id, model.UrlAccounts.AccountId);
    }

    [Fact]
    public async Task WhenTheOwnersCannotBeListed_ShouldLeaveThePersonalAccountUsableWithoutTheInternalDetail()
    {
        var model = await LoadWithFailingOwnersAsync();
        Assert.True(model.CanCreateRepository);
        Assert.True(Assert.Single(model.Owners.Value.Items).IsUser);
        Assert.Contains("own account", model.Message);
        Assert.DoesNotContain("internal detail", model.Message);
    }

    // The owner failure leaves a message beside the selected account, so clearing has something to remove.
    [Fact]
    public async Task WhenTheAccountsAreCleared_ShouldRemoveEverySelectionAndTheMessage()
    {
        var model = await LoadWithFailingOwnersAsync();
        Assert.Contains("own account", model.Message);
        model.Clear();
        Assert.Empty(model.All.Value);
        Assert.Empty(model.Visible);
        Assert.Null(model.SelectedAccount);
        Assert.Null(model.Owners.Value.Selected);
        Assert.Null(model.UrlAccounts.Selected);
        Assert.Equal("", model.Message);
        Assert.False(model.CanCreateRepository);
    }

    [Fact]
    public async Task WhenTheAccountsCannotBeRead_ShouldLeaveAnAddressWithoutStoredCredentialsAsTheWayToPublish()
    {
        var model = new PublishAccountsViewModel(new PublishAccountsModel(new AccountsModel(new FakeRepositoryCatalog
        {
            AccountsError = new InvalidOperationException("internal detail")
        }), new Repository()));
        await model.LoadAsync();
        model.RefreshUrlAccounts("https://github.com/alice/trag");
        Assert.False(model.IsBusy);
        Assert.False(model.CanCreateRepository);
        Assert.Empty(model.UrlAccounts.Items);
        Assert.Contains("Accounts could not be read", model.Message);
    }

    private static async Task<PublishAccountsViewModel> LoadWithFailingOwnersAsync()
    {
        var catalog = new FakeRepositoryCatalog
        {
            Accounts = { FakeAccountService.GitHub() },
            OwnersError = new InvalidOperationException("internal detail")
        };
        var model = new PublishAccountsViewModel(new PublishAccountsModel(new AccountsModel(catalog), new Repository()));
        await model.LoadAsync();
        return model;
    }
}
