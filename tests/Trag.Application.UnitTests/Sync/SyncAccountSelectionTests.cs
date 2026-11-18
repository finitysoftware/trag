using Trag.Application.Sync;
using Trag.Domain.Accounts;

namespace Trag.Application.UnitTests.Sync;

// Save writes the account chosen for the remote; with no saved state or nothing chosen there is nothing to save.
public sealed class SyncAccountSelectionTests
{
    [Fact]
    public void WhenAnAccountIsChosenForTheRemote_ShouldOfferToSaveIt()
    {
        var accounts = new SyncAccountSelection(new RemoteAccountState([new("account-1", "alice")], null, "")) { Selected = new("account-1", "alice") };
        Assert.True(accounts.CanSave);
    }

    [Fact]
    public void WhenNothingIsChosen_ShouldOfferNoSave() =>
        Assert.False(new SyncAccountSelection(new RemoteAccountState([new("account-1", "alice")], null, "")).CanSave);
}
