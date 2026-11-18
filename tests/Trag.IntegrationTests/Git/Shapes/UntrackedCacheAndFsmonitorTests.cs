using Trag.Application.Live;
using Trag.Application.Workspace;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Platform.Watching;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Shapes;

// A repository with core.untrackedCache=true and core.fsmonitor=true, as Git for Windows' installer option and scalar
// leave one: the index caches what is untracked, and Git's own file-system monitor is a daemon that the first command
// reading the index starts. Trag's status turns the monitor off; its other readers let it answer. The keys are set
// after the fixture's commit, since its commands are a terminal's and would start the daemon. The daemon was checked on
// Windows only, so the tests run there.
public sealed class UntrackedCacheAndFsmonitorTests
{
    // The watcher's read of the paths it saw change is the status alone: it lists the new files and starts no daemon.
    [Fact]
    public async Task WhenOnlyTheSignaledPathsAreRead_ShouldSeeTheNewFilesWithoutStartingTheMonitor()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("Base", "a.txt");
        var previous = await fixture.ReadAsync();
        await MonitorAsync(fixture);
        try
        {
            await fixture.WriteAsync("d/new.txt", "new\n");
            await fixture.WriteAsync("e/f/x.txt", "x\n");

            var read = await new CliRepositoryReader(fixture.Runner).ReadPathsAsync(previous, ["d/new.txt", "e/f/x.txt"], fixture.Token);

            Assert.Equal(["d/new.txt", "e/f/x.txt"], read.Files.Select(file => file.Path.Display).Order());
            Assert.Equal(1, (await fixture.RunAsync("fsmonitor--daemon", "status")).ExitCode);
        }
        finally { await StopMonitorAsync(fixture); }
    }

    // A status in a terminal writes the untracked cache and the monitor's token into the index. F5 still shows what Git
    // reads: a file in new nested folders, an untracked file gone and a tracked file edited, whether the daemon that
    // wrote the token still runs or was stopped.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenFilesChangeAfterTheTerminalCachedTheUntrackedFiles_ShouldRefreshToWhatGitReads(bool monitorRunning)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("Base", "a.txt");
        await fixture.WriteAsync("u/old.txt", "old\n");
        await MonitorAsync(fixture);
        try
        {
            await fixture.Runner.RunAsync(fixture.Root, ["status"], fixture.Token, new Dictionary<string, string?> { ["GIT_OPTIONAL_LOCKS"] = "1" });
            var index = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, ".git", "index"), fixture.Token);
            Assert.Equal((true, true), (index.AsSpan().IndexOf("UNTR"u8) >= 0, index.AsSpan().IndexOf("FSMN"u8) >= 0));
            await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(fixture.Runner));
            await workspace.OpenAsync(fixture.Root, fixture.Token);
            if (!monitorRunning) await StopMonitorAsync(fixture);
            await fixture.WriteAsync("n/m/new.txt", "new\n");
            File.Delete(Path.Combine(fixture.Root, "u", "old.txt"));
            await fixture.WriteAsync("a.txt", "changed\n");

            var refreshed = await workspace.RefreshAsync(fixture.Token);

            var git = (await fixture.GitAsync("-c", "core.fsmonitor=false", "status", "--porcelain=v2", "-uall")).Split('\n')
                .Select(line => line.TrimEnd('\r').Split(' ')[^1]).Order().ToArray();
            Assert.Equal(["a.txt", "n/m/new.txt"], git);
            Assert.Equal(git, refreshed!.Files.Select(file => file.Path.Display).Order());
        }
        finally { await StopMonitorAsync(fixture); }
    }

    // The daemon creates and deletes a cookie in .git/fsmonitor--daemon/cookies/ for every query, and the diff of the
    // shown file asks it. A cookie taken as a change would start a read of the whole status, after which the shell reads
    // the shown diff again and its query writes the next cookie.
    [Fact]
    public async Task WhenTragReadsTheShownFilesDiffWithTheMonitorConfigured_ShouldNotWakeItsOwnWatcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "a\n");
        await fixture.CommitAsync("Base", "a.txt");
        await MonitorAsync(fixture);
        await fixture.WriteAsync("a.txt", "changed\n");
        try
        {
            await using var workspace = new RepositoryWorkspace(new CliRepositoryReader(fixture.Runner), new RepositoryWatcher(), new CliFileDiffReader(fixture.Runner));
            var file = Assert.Single((await workspace.OpenAsync(fixture.Root, fixture.Token))!.Files);
            using var watching = CancellationTokenSource.CreateLinkedTokenSource(fixture.Token);
            await using var changes = workspace.WatchAsync(watching.Token).GetAsyncEnumerator();
            Assert.Equal(0, (await fixture.RunAsync("fsmonitor--daemon", "status")).ExitCode);
            // The open's first index read starts the daemon; that and the watch's first read are over once nothing comes.
            var next = await QuietAsync(changes, TimeSpan.FromSeconds(2), fixture.Token);

            Assert.NotNull(await workspace.ReadDiffAsync(file, false, false, fixture.Token));

            var woke = await Task.WhenAny(next, Task.Delay(TimeSpan.FromSeconds(3), fixture.Token)) == next;
            watching.Cancel();
            await next;
            Assert.False(woke);
        }
        finally { await StopMonitorAsync(fixture); }
    }

    // The wait for the next update, once none has come for the given time.
    private static async Task<Task<bool>> QuietAsync(IAsyncEnumerator<RepositoryUpdate> changes, TimeSpan quiet, CancellationToken ct)
    {
        var next = changes.MoveNextAsync().AsTask();
        while (await Task.WhenAny(next, Task.Delay(quiet, ct)) == next)
        {
            Assert.True(await next);
            next = changes.MoveNextAsync().AsTask();
        }
        return next;
    }

    private static async Task MonitorAsync(GitFixture fixture)
    {
        await fixture.GitAsync("config", "core.untrackedCache", "true");
        await fixture.GitAsync("config", "core.fsmonitor", "true");
    }

    // The daemon runs apart from the test's process tree and outlives the fixture unless it is told to stop.
    private static Task StopMonitorAsync(GitFixture fixture) => fixture.RunAsync("fsmonitor--daemon", "stop");
}
