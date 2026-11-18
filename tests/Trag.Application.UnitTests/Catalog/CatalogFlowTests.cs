using Trag.Application.Catalog;
using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Catalog;

// The repository picker's flows as outcomes: nothing here throws to the view model.
public sealed class CatalogFlowTests
{
    private static string At(string name) => Path.Combine(Path.GetTempPath(), "trag-catalog-flows", name);

    [Fact]
    public async Task WhenTheProbeOfAnAddedFolderFails_ShouldAnswerFailed()
    {
        var catalog = Failing(new IOException("disk"));
        Assert.Equal(CatalogAdditionOutcome.Failed, (await catalog.AddPathAsync(At("a"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhenTheInitializationFails_ShouldAnswerFailed()
    {
        var catalog = Failing(new IOException("disk"));
        Assert.Equal(CatalogAdditionOutcome.Failed, (await catalog.InitializeAsync(At("a"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhenAnOpenedRepositoryCannotBeSaved_ShouldAnswerFailed()
    {
        var catalog = Failing(new IOException("disk"));
        Assert.Equal(CatalogAdditionOutcome.Failed, (await catalog.RecordOpenedAsync(Snapshots.Create(root: At("a")), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhenTheListCannotBeSaved_ShouldAnswerThatItWasNot()
    {
        var catalog = Failing(new IOException("disk"));
        Assert.False(await catalog.TrySaveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheProbeOfAnAddedFolderIsCanceled_ShouldAnswerCanceled()
    {
        var catalog = Failing(new OperationCanceledException());
        Assert.Equal(CatalogAdditionOutcome.Canceled, (await catalog.AddPathAsync(At("a"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task WhenACloneSucceeds_ShouldAddItToTheList()
    {
        var (_, workflow, request) = Clone();
        var cloned = await workflow.RunAsync(request, CancellationToken.None);
        Assert.True(cloned.Succeeded); Assert.Equal(CatalogAdditionOutcome.Added, cloned.Addition?.Outcome);
    }

    [Fact]
    public async Task WhenGitRefusesTheClone_ShouldAnswerWithItsMessageAndDetails()
    {
        var (store, workflow, request) = Clone();
        store.CloneOutcome = new(false, "Git refused.", "details");
        var refused = await workflow.RunAsync(request, CancellationToken.None);
        Assert.Equal(new CloneResult(false, "Git refused.", "details"), refused);
    }

    [Fact]
    public async Task WhenTheCloneIsCanceled_ShouldSaySo()
    {
        var (store, workflow, request) = Clone();
        store.CloneGate.IsClosed = true;
        var pending = workflow.RunAsync(request, CancellationToken.None);
        await store.CloneGate.Started; store.CloneGate.Cancel();
        Assert.Equal(CloneText.Canceled, (await pending).Message);
    }

    [Fact]
    public async Task WhenTheCloneFailsUnexpectedly_ShouldSendTheUserToTheTargetFolder()
    {
        var (store, workflow, request) = Clone();
        store.CloneGate.IsClosed = true;
        var pending = workflow.RunAsync(request, CancellationToken.None);
        await store.CloneGate.Started; store.CloneGate.Fail(new IOException("boom"));
        Assert.Equal(CatalogText.CloneNotDisplayed, (await pending).Message);
    }

    [Fact]
    public async Task WhenTheUserPicksAFolder_ShouldAnswerIt()
    {
        Assert.Equal(new FolderPick("C:/x", false), await FolderPick.AskAsync(() => Task.FromResult<string?>("C:/x")));
    }

    [Fact]
    public async Task WhenThePickerIsClosed_ShouldAnswerNoFolderAndNoFailure()
    {
        Assert.Equal(new FolderPick(null, false), await FolderPick.AskAsync(() => Task.FromCanceled<string?>(new CancellationToken(true))));
    }

    [Fact]
    public async Task WhenThePickerFails_ShouldAnswerNoFolderAndAFailure()
    {
        Assert.Equal(new FolderPick(null, true), await FolderPick.AskAsync(() => Task.FromException<string?>(new IOException("no dialog"))));
    }

    // The local catalog over a store whose every call fails with that exception.
    private static LocalRepositoryCatalog Failing(Exception fail)
    {
        var store = new ThrowingCatalog();
        var catalog = new LocalRepositoryCatalog(store, store, store);
        store.Fail = fail;
        return catalog;
    }

    // A clone of the sandbox into its own folder, with the first account, over a fake catalog.
    private static (FakeRepositoryCatalog Store, CloneWorkflow Workflow, CloneRequest Request) Clone()
    {
        var store = new FakeRepositoryCatalog();
        var local = new LocalRepositoryCatalog(store, store, store);
        var workflow = new CloneWorkflow(store, local);
        return (store, workflow, new CloneRequest("https://github.com/contoso/sandbox.git", At("sandbox"), "account-1"));
    }

    // A catalog whose every call fails the same way.
    private sealed class ThrowingCatalog : Trag.Application.Ports.Catalog.IRepositoryCatalog
    {
        public Exception Fail { get; set; } = new IOException("disk");
        public string HomeFolder => "";
        public Task<IReadOnlyList<RepositoryEntry>> LoadAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RepositoryEntry>>([]);
        public Task<IReadOnlyList<RepositoryEntry>> UpdateAsync(Func<IReadOnlyList<RepositoryEntry>, IReadOnlyList<RepositoryEntry>> change, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<RepositoryEntry>>(Fail);
        public Task<RepositoryProbe> ProbeAsync(string path, CancellationToken ct) => Task.FromException<RepositoryProbe>(Fail);
        public Task InitializeAsync(string path, CancellationToken ct) => Task.FromException(Fail);
        public Task<IReadOnlyList<AccountInfo>> AccountsAsync(CancellationToken ct) => Task.FromException<IReadOnlyList<AccountInfo>>(Fail);
        public Task<RemoteRepositoryList> ListRemoteAsync(string accountId, CancellationToken ct) => Task.FromException<RemoteRepositoryList>(Fail);
        public Task<IReadOnlyList<Trag.Domain.Publish.PublishOwner>> OwnersAsync(string accountId, CancellationToken ct) => Task.FromException<IReadOnlyList<Trag.Domain.Publish.PublishOwner>>(Fail);
        public Task<CloneResult> CloneAsync(CloneRequest request, CancellationToken ct) => Task.FromException<CloneResult>(Fail);
    }
}
