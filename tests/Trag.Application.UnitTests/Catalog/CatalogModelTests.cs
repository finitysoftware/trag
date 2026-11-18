using Trag.Application.Accounts;
using Trag.Application.Catalog;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Catalog;

// The catalog without a screen, over a fake catalog: the order of its read and what an account read again keeps, the
// local items across rebuilds and removal, recording an opened repository, why a repository did not open, what a clone
// tells while it runs, and the picker's message about each.
public sealed class CatalogModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "trag-catalog-model");

    private static string Folder(string name) => Path.Combine(Root, name);

    private static (CatalogModel Model, FakeRepositoryCatalog Catalog, StatusBarState Bar) Create(FakeRepositoryWorkspace? workspace = null,
        CancelRegistry? cancels = null)
    {
        var catalog = new FakeRepositoryCatalog();
        var bar = new StatusBarState();
        var services = new RepositoryServices(workspace ?? new FakeRepositoryWorkspace(), bar, new ImmediateViewDispatcher());
        cancels ??= new CancelRegistry();
        var session = new RepositorySession(services, new FakeLastRepositoryStore(), _ => { }, cancels);
        return (new CatalogModel(catalog, new AccountsModel(catalog), services, session, cancels), catalog, bar);
    }

    // A repository opened from outside the list joins it with its branch and when it was opened; the entries already
    // there keep when they were opened.
    [Fact]
    public async Task WhenARepositoryOutsideTheListOpens_ShouldRecordItAndLeaveTheOthersAlone()
    {
        var (model, catalog, _) = Create();
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));
        await model.RecordOpenedAsync(Snapshots.Create(root: Folder("beta"), branch: "main"), CancellationToken.None);
        Assert.Equal(2, catalog.Saved!.Count);
        var beta = catalog.Saved.Single(entry => entry.Name == "beta");
        Assert.Equal("main", beta.Branch); Assert.NotNull(beta.LastOpenedAt); Assert.Null(catalog.Saved.Single(entry => entry.Name == "alpha").LastOpenedAt);
    }

    // A full list keeps what it holds: the repository opened from outside it is not added, and the status bar says so.
    [Fact]
    public async Task WhenTheListIsFullAndARepositoryIsOpened_ShouldSayItWasNotAdded()
    {
        var (model, catalog, bar) = Create();
        catalog.Entries.AddRange(Enumerable.Range(0, RepositoryEntry.MaximumListed).Select(i => new RepositoryEntry(Folder("listed-" + i), "listed-" + i, DateTimeOffset.UtcNow)));

        await model.RecordOpenedAsync(Snapshots.Create(root: Folder("beta"), branch: "main"), CancellationToken.None);

        Assert.Equal(0, catalog.Saves);
        Assert.Equal("Repository opened, but the repository list holds 500 repositories, the most it keeps, so it was not added. Remove one to add it.", bar.Notice);
    }

    [Fact]
    public async Task WhenTheCatalogIsRead_ShouldShowTheListThenTheAccountsThenCheckTheLinks()
    {
        var (model, _) = AlphaAndTwoAccounts();
        var events = new List<string>();
        model.Local.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Local.Items)) events.Add("rebuilt " + model.Local.Items.Count); };
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Accounts)) events.Add("accounts " + model.Accounts.Count); };
        model.Local.Checked += () => events.Add("checked");
        model.LinksChecked += () => events.Add("links");

        await model.ReadAsync();

        Assert.Equal(["rebuilt 1", "accounts 2", "checked", "links"], events);
    }

    // Only once the links are checked do the hosted repositories match their clones again.
    [Fact]
    public async Task WhenTheLinksWereChecked_ShouldMatchTheHostedRepositoriesToTheirClones()
    {
        var (model, _) = AlphaAndTwoAccounts();

        await model.ReadAsync();

        var alpha = Assert.Single(model.Local.Items);
        Assert.True(alpha.IsValid);
        Assert.Same(alpha, model.Local.Clones().Find("git@github.com:alice/alpha.git"));
    }

    [Fact]
    public async Task WhenTheCatalogIsReadAgainWithoutAnAccount_ShouldKeepTheOtherAccountWithItsHostedListAndTheItems()
    {
        var (model, catalog) = AlphaAndTwoAccounts();
        await model.ReadAsync();
        var alpha = model.Local.Items[0];
        var kept = model.Accounts[1];
        catalog.Accounts.RemoveAt(0);

        await model.ReadAsync();

        Assert.Same(kept, Assert.Single(model.Accounts));
        Assert.Same(alpha, Assert.Single(model.Local.Items));
    }

    [Fact]
    public async Task WhenTheAccountsCannotBeRead_ShouldKeepThemSaySoAndCheckTheLinksAllTheSame()
    {
        var (model, catalog, _) = Create();
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));
        catalog.Accounts.Add(FakeAccountService.GitHub());
        await model.ReadAsync();
        var account = Assert.Single(model.Accounts);

        catalog.AccountsError = new IOException("offline");
        var links = 0;
        model.LinksChecked += () => links++;
        await model.ReadAsync();
        Assert.Equal(CatalogText.AccountsNotRead, model.Message);
        Assert.Same(account, Assert.Single(model.Accounts));
        Assert.Equal(1, links);
        Assert.Equal("", model.Read.Message);
    }

    // A list that cannot be read says so where the picker speaks, over its welcome.
    [Fact]
    public async Task WhenTheListCannotBeRead_ShouldSaySoInThePickersMessage()
    {
        var (model, catalog, _) = Create();
        Assert.Equal(CatalogText.Welcome, model.Message);
        catalog.LoadError = new IOException("locked");

        await model.ReadAsync();

        Assert.Equal(CatalogText.ListNotRead, model.Message);
    }

    // The accounts do not wait for the list: a list that cannot be read still shows them with their hosted repositories,
    // and the message names each part that could not be read.
    [Fact]
    public async Task WhenTheListCannotBeRead_ShouldStillShowTheAccounts()
    {
        var (model, catalog, _) = Create();
        catalog.LoadError = new IOException("locked");
        catalog.Accounts.Add(FakeAccountService.GitHub());

        await model.ReadAsync();

        Assert.Single(model.Accounts);
        Assert.Equal(CatalogText.ListNotRead, model.Message);
        catalog.AccountsError = new IOException("offline");
        await model.ReadAsync();
        Assert.Equal(CatalogText.ListAndAccountsNotRead, model.Message);
    }

    // While the list cannot be read, an addition reads it again instead of starting a new list: the file it could not read
    // is never written over by a list holding only the new folder.
    [Fact]
    public async Task WhenTheListFileIsCorrupt_ShouldSayListNotReadAndRefuseToAddUntilItIsRead()
    {
        var (model, catalog, _) = Create();
        catalog.LoadError = new System.Text.Json.JsonException("corrupt");
        await model.ReadAsync();

        var added = await model.AddAsync(Folder("beta"));

        Assert.Equal(CatalogAdditionOutcome.Failed, added.Outcome);
        Assert.Equal(0, catalog.Saves);
    }

    // The account's read also leaves the shell's Cancel, which the application keeps for as long as it runs.
    [Fact]
    public async Task WhenAnAccountGoes_ShouldStopItsHostedReadLeaveTheShellsCancelAndLeaveTheOthersRunning()
    {
        var (model, catalog, tokens, held, reading, cancels) = await HostedReadsAsync();
        var (one, two) = (model.Accounts[0], model.Accounts[1]);
        Assert.True(one.List.IsBusy && two.List.IsBusy);
        var registered = cancels.Count;

        catalog.Accounts.RemoveAt(0);
        await model.ReadAsync();

        Assert.Same(two, Assert.Single(model.Accounts));
        Assert.Equal((true, false, false), (tokens["one"].IsCancellationRequested, one.List.IsBusy, tokens["two"].IsCancellationRequested));
        Assert.Equal((registered - 1, true), (cancels.Count, cancels.CanCancel));
        held.SetResult(new([], ReachedLimit: false));
        await reading;
    }

    [Fact]
    public async Task WhenTheReadsAreCanceled_ShouldStopTheHostedReadsStillRunning()
    {
        var (model, _, tokens, held, reading, _) = await HostedReadsAsync();
        var (one, two) = (model.Accounts[0], model.Accounts[1]);
        Assert.True(one.List.IsBusy && two.List.IsBusy);

        model.CancelReads();

        Assert.Equal((true, false), (tokens["one"].IsCancellationRequested, one.List.IsBusy));
        Assert.Equal((true, false), (tokens["two"].IsCancellationRequested, two.List.IsBusy));
        held.SetResult(new([], ReachedLimit: false));
        await reading;
    }

    [Fact]
    public async Task WhenTheListIsLoaded_ShouldShowItsItemsUnchecked()
    {
        var (model, catalog, _) = Create();
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));

        await model.Local.EnsureLoadedAsync(CancellationToken.None);
        model.Local.Rebuild();

        var alpha = Assert.Single(model.Local.Items);
        Assert.Equal(RepositoryLinkStatus.Unknown, alpha.Status);
    }

    [Fact]
    public async Task WhenAFolderIsAdded_ShouldBeAValidLinkWithoutACheck()
    {
        var (model, _, alpha) = await LoadedAsync();

        var added = await model.Local.AddAsync(Folder("beta"));
        Assert.Equal(CatalogAdditionOutcome.Added, added.Outcome);

        var beta = model.Local.Accept(added);
        Assert.Contains(beta, model.Local.Items);
        Assert.Equal((RepositoryLinkStatus.Valid, RepositoryLinkStatus.Unknown), (beta!.Status, alpha.Status));
    }

    [Fact]
    public async Task WhenTheListDidNotTakeAFolder_ShouldHaveNoItemForIt()
    {
        var (model, _, _) = await LoadedAsync();
        Assert.Null(model.Local.Accept(new(CatalogAdditionOutcome.NotRepository, null)));
    }

    // A clone goes beside the most recently opened repository, under the owner and the name its address gives; without
    // one, into a Repos folder in the home folder.
    [Fact]
    public async Task WhenACloneTargetIsSuggested_ShouldPlaceItBesideTheMostRecentRepository()
    {
        var (empty, catalog, _) = Create();
        Assert.Equal(Path.Combine(catalog.HomeFolder, "Repos", "alice", "sandbox"), empty.Clone.SuggestTarget(Sandbox().CloneUrl, "me", "sandbox"));

        var (model, _, _) = await LoadedAsync();
        Assert.Equal(Path.Combine(Root, "alice", "sandbox"), model.Clone.SuggestTarget(Sandbox().CloneUrl, "me", "sandbox"));
    }

    [Fact]
    public async Task WhenTheListIsRebuilt_ShouldKeepEachItemTheSameItem()
    {
        var (model, _, alpha) = await LoadedAsync();
        await AddBetaAsync(model);
        Assert.Contains(alpha, model.Local.Items);
    }

    [Fact]
    public async Task WhenAnItemIsRemoved_ShouldSaveTheListWithoutIt()
    {
        var (model, catalog, alpha) = await LoadedAsync();
        var beta = await AddBetaAsync(model);

        Assert.True(await model.Local.RemoveAsync(beta));

        Assert.Same(alpha, Assert.Single(model.Local.Items));
        Assert.Equal([Folder("alpha")], catalog.Saved!.Select(entry => entry.Path));
    }

    [Fact]
    public async Task WhenARepositoryOpens_ShouldShowItFirstInTheList()
    {
        var (model, _, _) = await LoadedAsync();
        await model.RecordOpenedAsync(Snapshots.Create(root: Folder("gamma")), CancellationToken.None);
        Assert.Equal(Folder("gamma"), model.Local.Items[0].Path);
    }

    [Fact]
    public async Task WhenTheRepositoryOpens_ShouldAnswerThatItDid()
    {
        var (model, _, _, alpha) = await CheckedAsync(new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("alpha")) });
        Assert.True(await model.OpenAsync(alpha));
    }

    [Fact]
    public async Task WhenTheOpenFails_ShouldSayTheErrorInTheMessage()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("alpha")) };
        workspace.Read = () => throw new IOException("The folder is gone.");
        var (model, _, bar, alpha) = await CheckedAsync(workspace);
        Assert.False(await model.OpenAsync(alpha));
        Assert.Equal(bar.ErrorMessage, model.Message);
        Assert.NotEqual(bar.Status, model.Message);
    }

    // There is no error to say.
    [Fact]
    public async Task WhenTheOpenIsCanceled_ShouldSayTheStatusLine()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("alpha")) };
        workspace.Read = () => throw new OperationCanceledException();
        var (model, _, bar, alpha) = await CheckedAsync(workspace);
        Assert.False(await model.OpenAsync(alpha));
        Assert.Equal(bar.Status, model.Message);
        Assert.Equal("", bar.ErrorMessage);
        Assert.NotEqual("", bar.Status);
    }

    // A link the check found broken leads nowhere: nothing opens, and the message says what the check found.
    [Fact]
    public async Task WhenTheLinkIsBroken_ShouldOpenNothingAndSayWhatTheCheckFound()
    {
        var (model, catalog, _) = Create(workspace: new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("gone")) });
        catalog.Entries.Add(new(Folder("gone"), "gone", DateTimeOffset.UtcNow));
        catalog.Probes[Folder("gone")] = new(RepositoryLinkStatus.Missing);
        await model.ReadAsync();

        Assert.False(await model.OpenAsync(Assert.Single(model.Local.Items)));

        Assert.Equal(CatalogText.Link(RepositoryLinkStatus.Missing, false), model.Message);
    }

    // Nothing opens while a clone runs; once it is done, a listed repository opens again.
    [Fact]
    public async Task WhenACloneRuns_ShouldOpenNothing()
    {
        var (model, catalog, _, alpha) = await CheckedAsync(new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("alpha")) });
        catalog.CloneGate.IsClosed = true;
        var cloning = model.CloneAsync(Sandbox(), FakeAccountService.GitHub(), Folder("sandbox"), true, _ => { });
        await catalog.CloneGate.Started;

        Assert.False(model.CanOpen);
        Assert.False(await model.OpenAsync(alpha));

        catalog.CloneGate.Open(); await cloning;
        Assert.True(model.CanOpen);
        Assert.True(await model.OpenAsync(alpha));
    }

    // Nothing opens while the open repository is being written.
    [Fact]
    public async Task WhenTheOpenRepositoryIsBeingWritten_ShouldOpenNothing()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder("alpha"), files: [Snapshots.Staged("a.txt")]) };
        var catalog = new FakeRepositoryCatalog { Entries = { new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow) } };
        var services = new RepositoryServices(workspace, new StatusBarState(), new ImmediateViewDispatcher());
        var session = new RepositorySession(services, new FakeLastRepositoryStore(), _ => { }, new CancelRegistry());
        var model = new CatalogModel(catalog, new AccountsModel(catalog), services, session, new CancelRegistry());
        await model.ReadAsync();
        var alpha = Assert.Single(model.Local.Items);
        Assert.True(await model.OpenAsync(alpha));
        workspace.Gate.IsClosed = true;

        var writing = session.Current.Status.CommitAsync("Message");
        Assert.False(model.CanOpen);
        Assert.False(await model.OpenAsync(alpha));

        workspace.Gate.Open(); await writing;
        Assert.True(model.CanOpen);
    }

    // What became of a folder the picker chose, initialized or took off the list is the picker's message.
    [Fact]
    public async Task WhenAFolderIsOfferedInitializedOrRemoved_ShouldSayWhatBecameOfIt()
    {
        var (model, catalog, alpha) = await LoadedAsync();
        catalog.Probes[Folder("plain")] = new(RepositoryLinkStatus.NotRepository);

        Assert.Equal(CatalogAdditionOutcome.Added, (await model.AddAsync(Folder("beta"))).Outcome);
        Assert.Equal(CatalogText.Added("beta", alreadyListed: false), model.Message);
        await model.AddAsync(Folder("beta"));
        Assert.Equal(CatalogText.Added("beta", alreadyListed: true), model.Message);
        Assert.Equal(CatalogAdditionOutcome.NotRepository, (await model.AddAsync(Folder("plain"))).Outcome);
        Assert.Equal(CatalogText.NotRepository, model.Message);

        catalog.InitializeError = new OperationCanceledException();
        await model.InitializeAsync(Folder("plain"));
        Assert.Equal(CatalogText.InitializeFailed, model.Message);
        catalog.InitializeError = new IOException("denied");
        Assert.Equal(CatalogAdditionOutcome.Failed, (await model.InitializeAsync(Folder("plain"))).Outcome);
        Assert.Equal(CatalogText.InitializeFailed, model.Message);
        catalog.InitializeError = null;
        await model.InitializeAsync(Folder("plain"));
        Assert.Equal(CatalogText.Initialized("plain"), model.Message);

        await model.RemoveAsync(alpha);
        Assert.Equal(CatalogText.Removed("alpha"), model.Message);
        var beta = model.Local.Items.Single(item => item.Name == "beta");
        catalog.SaveError = new IOException("locked");
        await model.RemoveAsync(beta);
        Assert.Equal(CatalogText.RemovedNotSaved, model.Message);
    }

    [Fact]
    public async Task WhenACloneStarts_ShouldSayWhereFromAndCloneWithTheAccount()
    {
        var (_, catalog, bar, _, cloning) = await CloningAsync();
        Assert.Equal(CatalogText.CloningFrom("alice/sandbox"), bar.Status);
        var request = Assert.Single(catalog.Clones);
        Assert.Equal(("https://github.com/alice/sandbox.git", "account-1"), (request.CloneUrl, request.AccountId));
        catalog.CloneGate.Open(); await cloning;
    }

    [Fact]
    public async Task WhenARunningCloneReportsProgress_ShouldPassItToTheCallerAndTheStatusBar()
    {
        var (_, catalog, bar, progress, cloning) = await CloningAsync();
        var request = catalog.Clones[0];

        request.Progress!.Report("Receiving objects: 50%");

        Assert.Equal(["Receiving objects: 50%"], progress);
        Assert.Equal("Receiving objects: 50%", bar.Status);
        catalog.CloneGate.Open(); await cloning;
    }

    [Fact]
    public async Task WhenACloneFinishes_ShouldComeBackWithItsAdditionAndSaySo()
    {
        var (model, catalog, bar, _, cloning) = await CloningAsync();

        catalog.CloneGate.Open();
        var outcome = await cloning;

        Assert.Equal((true, CatalogAdditionOutcome.Added), (outcome!.Succeeded, outcome.Addition!.Outcome));
        Assert.Equal(CatalogText.Cloned, bar.Status);
        Assert.Equal(CatalogText.ClonedAs("sandbox"), model.Message);
    }

    // The progress the clone reported while it ran is all the caller got.
    [Fact]
    public async Task WhenProgressComesAfterTheCloneFinished_ShouldPassItNowhere()
    {
        var (_, catalog, bar, progress, cloning) = await CloningAsync();
        var request = catalog.Clones[0];
        request.Progress!.Report("Receiving objects: 50%");
        catalog.CloneGate.Open();
        await cloning;

        request.Progress.Report("late");

        Assert.Equal((1, CatalogText.Cloned), (progress.Count, bar.Status));
    }

    [Fact]
    public async Task WhenGitRefusesTheClone_ShouldLeaveItsMessageInTheStatusBar()
    {
        var (model, catalog, bar) = Create();
        var progress = new List<string>();
        catalog.CloneOutcome = new(false, "Git refused the clone.", "fatal: denied");
        var outcome = await model.CloneAsync(Sandbox(), FakeAccountService.GitHub(), Folder("other"), true, progress.Add);
        Assert.Equal((false, "Git refused the clone.", "Git refused the clone."), (outcome!.Succeeded, outcome.Message, bar.Status));
    }

    // Alpha listed and linked to its clone on GitHub, and two accounts; nothing is read yet.
    private static (CatalogModel Model, FakeRepositoryCatalog Catalog) AlphaAndTwoAccounts()
    {
        var (model, catalog, _) = Create();
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));
        catalog.Probes[Folder("alpha")] = new(RepositoryLinkStatus.Valid, "trunk", "https://github.com/alice/alpha.git", Folder("alpha"));
        catalog.Accounts.AddRange([FakeAccountService.GitHub("one", "alice"), FakeAccountService.GitHub("two", "bob")]);
        return (model, catalog);
    }

    // Two accounts read, each reading its hosted list, which the catalog holds until the test lets it go; the tokens are
    // the reads', and the registry is the one the shell's Cancel reaches.
    private static async Task<(CatalogModel Model, FakeRepositoryCatalog Catalog, Dictionary<string, CancellationToken> Tokens,
        TaskCompletionSource<RemoteRepositoryList> Held, Task Reading, CancelRegistry Cancels)> HostedReadsAsync()
    {
        var cancels = new CancelRegistry();
        var (model, catalog, _) = Create(cancels: cancels);
        catalog.Accounts.AddRange([FakeAccountService.GitHub("one", "alice"), FakeAccountService.GitHub("two", "bob")]);
        var tokens = new Dictionary<string, CancellationToken>();
        var held = new TaskCompletionSource<RemoteRepositoryList>();
        catalog.ReadRemote = (id, ct) =>
        {
            tokens[id] = ct;
            return held.Task;
        };
        await model.ReadAsync();
        var reading = Task.WhenAll(model.Accounts[0].LoadAsync(), model.Accounts[1].LoadAsync());
        return (model, catalog, tokens, held, reading, cancels);
    }

    // Alpha loaded into the list, not checked.
    private static async Task<(CatalogModel Model, FakeRepositoryCatalog Catalog, LocalRepository Alpha)> LoadedAsync()
    {
        var (model, catalog, _) = Create();
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));
        await model.Local.EnsureLoadedAsync(CancellationToken.None);
        model.Local.Rebuild();
        return (model, catalog, model.Local.Items[0]);
    }

    // Alpha listed and checked, over the workspace that opens it.
    private static async Task<(CatalogModel Model, FakeRepositoryCatalog Catalog, StatusBarState Bar, LocalRepository Alpha)> CheckedAsync(FakeRepositoryWorkspace workspace)
    {
        var (model, catalog, bar) = Create(workspace: workspace);
        catalog.Entries.Add(new(Folder("alpha"), "alpha", DateTimeOffset.UtcNow));
        await model.ReadAsync();
        return (model, catalog, bar, Assert.Single(model.Local.Items));
    }

    // Beta added and the list rebuilt around it.
    private static async Task<LocalRepository> AddBetaAsync(CatalogModel model)
    {
        return model.Local.Accept(await model.Local.AddAsync(Folder("beta")))!;
    }

    private static RemoteRepository Sandbox() =>
        new("alice/sandbox", "https://github.com/alice/sandbox.git", "https://github.com/alice/sandbox", "main", false, null);

    // A clone of the sandbox under way, held by the catalog until the test lets it go; its progress goes to the list.
    private static async Task<(CatalogModel Model, FakeRepositoryCatalog Catalog, StatusBarState Bar, List<string> Progress, Task<CloneResult?> Cloning)> CloningAsync()
    {
        var (model, catalog, bar) = Create();
        catalog.CloneGate.IsClosed = true;
        var progress = new List<string>();
        var cloning = model.CloneAsync(Sandbox(), FakeAccountService.GitHub(), Folder("sandbox"), true, progress.Add);
        await catalog.CloneGate.Started;
        return (model, catalog, bar, progress, cloning);
    }
}
