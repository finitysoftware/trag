using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Shell;

// What closing the settings window does to the shell that owns it: the sync state and the account list are read again,
// once, at once (the window ends the editing on the UI thread), and never for a shell that is busy, hidden or gone.
public sealed class SettingsMessageTests
{
    [Fact]
    public async Task WhenTheSettingsClose_ShouldRereadOnlyTheOwningShellsAccountsAndSyncEvenWhileItsSyncPanelIsHidden()
    {
        var first = Harness();
        var second = Harness();
        await using var a = first.Shell();
        await using var b = second.Shell();
        await OpenPickerAsync(a);
        await OpenPickerAsync(b);
        var aSync = first.Sync.Reads.Count;
        var bSync = second.Sync.Reads.Count;
        var aCatalog = first.Catalog.AccountsReads;
        var bCatalog = second.Catalog.AccountsReads;
        first.Catalog.Accounts.Add(FakeAccountService.GitHub());

        a.Workbench.Accounts.EndEditing();
        await RefreshCompletion(a);

        Assert.NotSame(a.Workbench, b.Workbench);
        Assert.Equal(aSync + 1, first.Sync.Reads.Count);
        Assert.Equal(aCatalog + 1, first.Catalog.AccountsReads);
        Assert.Single(a.Catalog.AccountGroups);
        Assert.Equal(bSync, second.Sync.Reads.Count);
        Assert.Equal(bCatalog, second.Catalog.AccountsReads);
        Assert.Empty(b.Catalog.AccountGroups);
    }

    [Fact]
    public async Task WhenTheSettingsCloseAfterAnotherRepositoryOpened_ShouldRereadTheSyncTargetOfTheNewOne()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("first"); await model.Sync.SyncCompletion;
        await model.Session.OpenRepositoryAsync("second"); await model.Sync.SyncCompletion;
        var reads = h.Sync.Reads.Count;

        model.Workbench.Accounts.EndEditing();
        await model.Sync.SyncCompletion;

        Assert.Equal(reads + 1, h.Sync.Reads.Count); Assert.Equal("second", model.Repository.Snapshot!.Root);
    }

    [Fact]
    public async Task WhenTheSettingsCloseAfterTheShellWasDisposed_ShouldReadNothing()
    {
        var h = Harness();
        var model = h.Shell();
        await OpenPickerAsync(model);
        var syncReads = h.Sync.Reads.Count;
        var catalogReads = h.Catalog.AccountsReads;

        await model.DisposeAsync();
        model.Workbench.Accounts.EndEditing();
        await RefreshCompletion(model);

        Assert.Equal(syncReads, h.Sync.Reads.Count);
        Assert.Equal(catalogReads, h.Catalog.AccountsReads);
    }

    [Fact]
    public async Task WhenTheShellIsDisposedDuringTheSettingsReads_ShouldCancelThemAndIgnoreTheirLateResults()
    {
        var h = Harness();
        var model = h.Shell();
        await OpenPickerAsync(model);
        h.Sync.ReadGate.IsClosed = true;
        h.Catalog.AccountsGate.IsClosed = true;
        h.Catalog.Accounts.Add(FakeAccountService.GitHub());
        model.Workbench.Accounts.EndEditing();
        var pending = RefreshCompletion(model);
        Assert.False(pending.IsCompleted);

        await model.DisposeAsync();
        var syncText = model.Sync.Panel.Message;
        Assert.True(h.Sync.ReadGate.Token.IsCancellationRequested);
        Assert.True(h.Catalog.AccountsGate.Token.IsCancellationRequested);
        h.Sync.ReadGate.Open();
        h.Catalog.AccountsGate.Open();
        await pending;

        Assert.Empty(model.Catalog.AccountGroups);
        Assert.Equal(syncText, model.Sync.Panel.Message);
        Assert.False(model.Catalog.State.IsBusy);
    }

    [Fact]
    public async Task WhenTheSettingsClose_ShouldStartBothReadsAtOnceAndKeepTheirCompletionsOpenUntilTheyEnd()
    {
        var h = Harness();
        await using var model = h.Shell();
        await OpenPickerAsync(model);
        var syncReads = h.Sync.Reads.Count;
        var catalogReads = h.Catalog.AccountsReads;
        h.Sync.ReadGate.IsClosed = true;
        h.Catalog.AccountsGate.IsClosed = true;

        model.Workbench.Accounts.EndEditing();
        await h.Sync.ReadGate.Arrival(syncReads + 1);
        await h.Catalog.AccountsGate.Arrival(catalogReads + 1);
        Assert.False(model.Sync.SyncCompletion.IsCompleted);
        Assert.False(model.Catalog.CatalogCompletion.IsCompleted);
        h.Sync.ReadGate.Open();
        h.Catalog.AccountsGate.Open();
        await RefreshCompletion(model);
    }

    [Fact]
    public async Task WhenTheSettingsCloseWithTheCatalogHiddenAndTheSyncBusy_ShouldStartNoRead()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        h.Sync.ReadGate.IsClosed = true;
        model.Repository.Remotes.Reread();
        var syncReads = h.Sync.Reads.Count;
        var catalogReads = h.Catalog.AccountsReads;

        model.Workbench.Accounts.EndEditing(); // received at once, so nothing started if the counts did not move

        Assert.Equal(syncReads, h.Sync.Reads.Count);
        Assert.Equal(catalogReads, h.Catalog.AccountsReads);
        h.Sync.ReadGate.Open();
        await model.Sync.SyncCompletion;
    }

    [Fact]
    public async Task WhenTheSettingsCloseWhileAWriteRunsAndTheCatalogLoads_ShouldStartNoRead()
    {
        var h = Harness();
        await using var model = h.Shell();
        await OpenPickerAsync(model);
        model.Repository.IsWriting = true;
        h.Catalog.AccountsGate.IsClosed = true;
        var loading = model.Catalog.Load();
        var syncReads = h.Sync.Reads.Count;
        var catalogReads = h.Catalog.AccountsReads;

        model.Workbench.Accounts.EndEditing(); // received at once, so nothing started if the counts did not move

        Assert.Equal(syncReads, h.Sync.Reads.Count);
        Assert.Equal(catalogReads, h.Catalog.AccountsReads);
        h.Catalog.AccountsGate.Open();
        await loading;
        model.Repository.IsWriting = false;
        await model.Sync.SyncCompletion;
    }

    // The repository's read comes first.
    [Fact]
    public async Task WhenTheSettingsCloseWhileTheRepositoryIsRead_ShouldStartNoSyncRead()
    {
        var h = Harness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Repository.IsReading = true;
        var syncReads = h.Sync.Reads.Count;

        model.Workbench.Accounts.EndEditing();

        Assert.Equal(syncReads, h.Sync.Reads.Count);
        model.Repository.IsReading = false;
    }

    // A repository with a tracked remote, so the sync panel has something to read again.
    private static ShellHarness Harness(QueuedDispatcher? dispatcher = null)
    {
        var h = new ShellHarness();
        if (dispatcher is not null) h.Dispatcher = dispatcher;
        h.Reader.Snapshot = h.Synced();
        return h;
    }

    private static async Task OpenPickerAsync(ShellViewModel model)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        model.Navigator.ShowRepositories();
        await model.Catalog.CatalogCompletion;
    }

    private static Task RefreshCompletion(ShellViewModel model) =>
        Task.WhenAll(model.Sync.SyncCompletion, model.Catalog.CatalogCompletion);
}
