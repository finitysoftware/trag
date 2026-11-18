using Trag.Application.Diff;
using Trag.Application.Live;
using Trag.Application.Ports.Threading;
using Trag.Domain.Branches;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

// The application's models as one: what the session starts is shown only once the application attached, the current
// repository first, and every repository the session opens is recorded in the catalog; how the application starts,
// whether it may close, and how it stops.
public sealed class WorkbenchTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "trag-workbench", "alpha");

    private static RepositoryServices Services(FakeRepositoryWorkspace workspace) =>
        new(workspace, new StatusBarState(), new ImmediateViewDispatcher());

    [Fact]
    public async Task WhenTheApplicationStarts_ShouldReadTheLayoutAndThenRestoreTheLastRepository()
    {
        var (workbench, preferences) = Remembering();

        var starting = workbench.StartAsync(CancellationToken.None);
        Assert.False(workbench.Session.Current.HasRepository);
        preferences.LoadGate.Open();
        Assert.True(await starting);
        Assert.Equal(2, workbench.Layout.ThemeIndex);
        Assert.True(workbench.Session.Current.HasRepository);
    }

    // The layout and the list unreadable at once: the layout's notice stays when the last repository opens, beside the list's.
    [Fact]
    public async Task WhenTheLayoutAndTheListAreCorruptAndTheLastRepositoryIsRestored_ShouldSayBothNoticesOfTheStart()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var catalog = new FakeRepositoryCatalog { LoadError = new System.Text.Json.JsonException("list") };
        var preferences = new FakeLayoutStore { LoadFailure = new System.Text.Json.JsonException("layout") };
        var workbench = new Workbench(Services(workspace), new FakeLastRepositoryStore { Path = Folder }, catalog, preferences);
        workbench.Attach(_ => { });

        Assert.True(await workbench.StartAsync(CancellationToken.None));

        Assert.True(workbench.Session.Current.HasRepository);
        Assert.Equal(LayoutText.NotLoaded + " " + CatalogText.OpenedNotRecorded, workbench.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenTheLayoutIsCorruptAndALastRepositoryIsRestored_ShouldStillShowLayoutNotLoaded()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var preferences = new FakeLayoutStore { LoadFailure = new System.Text.Json.JsonException("layout") };
        var workbench = new Workbench(Services(workspace), new FakeLastRepositoryStore { Path = Folder }, new FakeRepositoryCatalog(), preferences);
        workbench.Attach(_ => { });

        Assert.True(await workbench.StartAsync(CancellationToken.None));

        Assert.True(workbench.Session.Current.HasRepository);
        Assert.Equal(LayoutText.NotLoaded, workbench.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenOnlyTheListIsCorruptAndTheLastRepositoryIsRestored_ShouldSayOnlyWhatTheOpenSaid()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var catalog = new FakeRepositoryCatalog { LoadError = new System.Text.Json.JsonException("list") };
        var workbench = new Workbench(Services(workspace), new FakeLastRepositoryStore { Path = Folder }, catalog, new FakeLayoutStore());
        workbench.Attach(_ => { });

        Assert.True(await workbench.StartAsync(CancellationToken.None));

        Assert.Equal(CatalogText.OpenedNotRecorded, workbench.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenTheLayoutIsCorruptAndNoRepositoryIsRestored_ShouldSayItOnce()
    {
        var preferences = new FakeLayoutStore { LoadFailure = new System.Text.Json.JsonException("layout") };
        var workbench = new Workbench(Services(new FakeRepositoryWorkspace()), new FakeLastRepositoryStore(), new FakeRepositoryCatalog(), preferences);
        workbench.Attach(_ => { });

        Assert.True(await workbench.StartAsync(CancellationToken.None));

        Assert.Equal(LayoutText.NotLoaded, workbench.StatusBar.Notice);
    }

    [Fact]
    public async Task WhenTheApplicationStartsAgain_ShouldStartNothing()
    {
        var (workbench, preferences) = Remembering();
        var starting = workbench.StartAsync(CancellationToken.None);
        preferences.LoadGate.Open();
        Assert.True(await starting);

        Assert.False(await workbench.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenNoGitWriteRuns_ShouldLetTheApplicationClose()
    {
        var (workbench, _, _, _) = await OpenedAsync();
        Assert.False(workbench.RefusesToClose());
    }

    [Fact]
    public async Task WhenAGitWriteRuns_ShouldStayOpenAndSayWhy()
    {
        var (workbench, workspace, _, _) = await OpenedAsync();
        workspace.Gate.IsClosed = true;
        var writing = workbench.Session.Current.Branches.WriteAsync(BranchAction.Create, new BranchOperation("topic", Switch: false));
        Assert.True(workbench.RefusesToClose());
        Assert.Equal(ShellText.OperationRunning, workbench.StatusBar.Notice);
        workspace.Gate.Open();
        await writing;
    }

    [Fact]
    public async Task WhenTheGitWriteIsDone_ShouldLetTheApplicationCloseAgain()
    {
        var (workbench, workspace, _, _) = await OpenedAsync();
        workspace.Gate.IsClosed = true;
        var writing = workbench.Session.Current.Branches.WriteAsync(BranchAction.Create, new BranchOperation("topic", Switch: false));
        Assert.True(workbench.RefusesToClose());
        workspace.Gate.Open();
        await writing;
        Assert.False(workbench.RefusesToClose());
    }

    [Fact]
    public async Task WhenTheApplicationStops_ShouldEndTheCatalogsReadsAndRetireTheRepository()
    {
        var (workbench, _, catalog, _) = await OpenedAsync();
        catalog.AccountsGate.IsClosed = true;
        var reading = workbench.Catalog.ReadAsync();
        Assert.True(workbench.Catalog.Read.IsBusy);

        workbench.Stop();
        Assert.False(workbench.Catalog.Read.IsBusy);
        Assert.True(workbench.Session.Current.IsRetired);
        catalog.AccountsGate.Open();
        await reading;
    }

    // A layout save in flight is what a close waits for, and disposing waits for it too.
    [Fact]
    public async Task WhenTheApplicationGoesDuringALayoutSave_ShouldWaitForTheSave()
    {
        var (workbench, _, _, preferences) = await OpenedAsync();
        workbench.Layout.ThemeIndex = 1;
        Assert.False(workbench.Saving.IsCompleted);

        workbench.Stop();
        await workbench.DisposeAsync();
        Assert.True(workbench.Saving.IsCompleted);
        Assert.Equal(1, preferences.Saved!.Theme);
    }

    [Fact]
    public void WhenTheApplicationAttaches_ShouldShowTheCurrentEmptyRepositoryAtOnce()
    {
        var (workbench, _) = Unattached();
        var shown = new List<Repository>();
        Assert.False(workbench.Session.Current.HasRepository);

        workbench.Attach(shown.Add);
        Assert.Same(workbench.Session.Current, Assert.Single(shown));
    }

    [Fact]
    public async Task WhenTheSessionOpensARepository_ShouldShowIt()
    {
        var (workbench, _) = Unattached();
        var shown = new List<Repository>();
        workbench.Attach(shown.Add);

        await workbench.Session.OpenRepositoryAsync(Folder);
        Assert.Equal(2, shown.Count);
        Assert.Same(workbench.Session.Current, shown[^1]);
        Assert.True(shown[^1].HasRepository);
    }

    [Fact]
    public async Task WhenTheSessionOpensARepository_ShouldRecordItInTheCatalog()
    {
        var (workbench, catalog) = Unattached();
        workbench.Attach(_ => { });

        await workbench.Session.OpenRepositoryAsync(Folder);
        Assert.Equal(Folder, Assert.Single(workbench.Catalog.Local.Items).Path);
        Assert.Equal([Folder], catalog.Saved!.Select(entry => entry.Path));
    }

    // The shell's Cancel is global: it stops what the application runs, as a clone in the background, and what the shown
    // repository reads, as a diff, and each says it was cancelled where it is shown.
    [Fact]
    public async Task WhenEscapeIsPressedDuringACloneAndADiffRead_ShouldCancelBothAndSayItForEach()
    {
        var (workbench, catalog) = Unattached();
        await using var _ = workbench;
        catalog.CloneGate.IsClosed = true;
        var cloning = workbench.Catalog.CloneAsync(new("alice/sandbox", "https://github.com/alice/sandbox.git", "https://github.com/alice/sandbox", "main", false, null),
            FakeAccountService.GitHub(), Path.Combine(Path.GetTempPath(), "trag-workbench", "sandbox"), true, _ => { });
        await catalog.CloneGate.Started;
        var diff = new DiffReader(new BackgroundDiffBuilder(), workbench.Session.Current.Cancels, DiffMessages.Working);
        diff.Start(new("a.cs", "working tree"), async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; });

        workbench.Cancel();
        // The clone's Git stops on its token and the cloner answers as a stopped Git does.
        Assert.True(catalog.CloneGate.Token.IsCancellationRequested);
        catalog.CloneGate.Cancel();
        await Task.WhenAll(cloning, diff.Completion).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(CloneText.Canceled, workbench.StatusBar.Status);
        Assert.Equal(DiffMessages.Working.Cancelled, diff.Document.Message);
    }

    // An attached workbench whose host remembers the folder and whose saved layout, theme 2, loads once its gate opens.
    private static (Workbench Workbench, FakeLayoutStore Preferences) Remembering()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var preferences = new FakeLayoutStore { Preferences = new(Theme: 2) };
        preferences.LoadGate.IsClosed = true;
        var workbench = new Workbench(Services(workspace), new FakeLastRepositoryStore { Path = Folder }, new FakeRepositoryCatalog(), preferences);
        workbench.Attach(_ => { });
        return (workbench, preferences);
    }

    // An attached workbench with a catalog and a layout store, the folder open and nothing running.
    private static async Task<(Workbench Workbench, FakeRepositoryWorkspace Workspace, FakeRepositoryCatalog Catalog, FakeLayoutStore Preferences)> OpenedAsync()
    {
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var catalog = new FakeRepositoryCatalog();
        var preferences = new FakeLayoutStore();
        var workbench = new Workbench(Services(workspace), new FakeLastRepositoryStore(), catalog, preferences);
        workbench.Attach(_ => { });
        await workbench.Session.OpenRepositoryAsync(Folder);
        return (workbench, workspace, catalog, preferences);
    }

    // A workbench with a catalog over a workspace that opens the folder, not attached yet.
    private static (Workbench Workbench, FakeRepositoryCatalog Catalog) Unattached()
    {
        var catalog = new FakeRepositoryCatalog();
        var workspace = new FakeRepositoryWorkspace { Snapshot = Snapshots.Create(root: Folder) };
        var workbench = new Workbench(new(workspace, new StatusBarState(), new ImmediateViewDispatcher()), new FakeLastRepositoryStore(), catalog, null);
        return (workbench, catalog);
    }
}
