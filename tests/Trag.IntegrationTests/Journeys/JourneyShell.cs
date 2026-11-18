using Trag.Application.Ports.Threading;
using Trag.Desktop.Composition;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Platform.Storage;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.IntegrationTests.Journeys;

// The harness of the user journeys: the shell as the application composes it over real Git and a real
// watcher (ShellFactory.Create), with the repository list in memory and the window's dialogs answered by a RecordingHost.
// Every folder of a journey (new repositories, bare remotes, second clones) lives in the fixture's container. A journey
// drives the view models as a user would, awaits what each step runs (the command, then SyncCompletion for the sync
// read that follows a write), and asks Git itself for the state after every step.
internal sealed class JourneyShell : IAsyncDisposable
{
    private readonly MemoryRepositoryCatalogStore list = new();

    private JourneyShell(GitFixture fixture) => Fixture = fixture;

    public GitFixture Fixture { get; }
    public ShellViewModel Shell { get; private set; } = null!;
    public RecordingHost Host { get; } = new();
    // The Git program the shell and its repository list start, "git" from the PATH unless a test puts another in its place.
    public GitExecutable Executable { get; } = new();

    // A journey runs many Git processes (second clones, rebases, Git LFS, submodules), so it gets five minutes, not the
    // fixture's usual two.
    public static async Task<JourneyShell> StartAsync()
    {
        var journey = new JourneyShell(await GitFixture.CreateAsync(scenarioTimeout: TimeSpan.FromMinutes(5)));
        await journey.ShowAsync();
        return journey;
    }

    // The application closed and started again: a new shell over the same repository list.
    public async Task RestartAsync()
    {
        await Shell.DisposeAsync();
        await ShowAsync();
    }

    // Started as the window starts it: with nothing to restore, the shell shows the repository list and reads it.
    private async Task ShowAsync()
    {
        var catalog = new RepositoryCatalogFacade(new GitProcessRunner(executable: Executable), list);
        Shell = ShellFactory.Create(Executable, catalog, null, null, null, null, new ImmediateViewDispatcher(), _ => { });
        Shell.Host = Host;
        await Shell.InitializeAsync(Fixture.Token);
        await Shell.Catalog.CatalogCompletion;
    }

    // Git's own answer in a folder of the journey; a failing command throws with its stderr.
    public Task<string> Git(string root, params string[] arguments) => Fixture.GitAtAsync(root, arguments);

    public async ValueTask DisposeAsync()
    {
        await Shell.DisposeAsync();
        Fixture.Dispose();
    }
}
