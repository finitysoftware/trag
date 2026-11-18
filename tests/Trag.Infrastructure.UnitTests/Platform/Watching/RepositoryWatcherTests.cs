using System.Threading.Channels;
using Trag.Application.Live;
using Trag.Application.Ports.Watching;
using Trag.Domain.Status;
using Trag.Infrastructure.Platform.Watching;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Platform.Watching;

// Real FileSystemWatcher over a temporary folder: which events become a path signal, which force a reconcile, which are ignored.
public sealed class RepositoryWatcherTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-watch-" + Guid.NewGuid().ToString("N"), "repo");
    private string GitDirectory => Path.Combine(root, ".git");
    public RepositoryWatcherTests() => Directory.CreateDirectory(GitDirectory);
    public void Dispose() { var container = Path.GetDirectoryName(root)!; if (Directory.Exists(container)) Directory.Delete(container, true); }

    private RepositorySnapshot Snapshot => Snapshots.Create(root) with { GitDirectory = GitDirectory, CommonDirectory = GitDirectory };
    private static async Task<RepositoryChange> Next(IRepositoryChangeSource source)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await source.ReadAsync(timeout.Token);
    }
    private Task Write(string relative, string content = "x") => File.WriteAllTextAsync(Path.Combine(root, relative), content);

    // The user's own Git configuration (signing among it) lives outside the repository; a change to it is a reconcile, as
    // a change to .git/config is.
    [Fact]
    public async Task WhenTheUsersGitConfigurationChanges_ShouldForceAReconcile()
    {
        var config = Path.Combine(Path.GetDirectoryName(root)!, "home", ".gitconfig");
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        await File.WriteAllTextAsync(config, "[user]\n");
        using var source = new RepositoryWatcher([config]).Watch(Snapshot);
        Assert.True((await Next(source)).Reconcile);

        await File.WriteAllTextAsync(config, "[tag]\n\tgpgSign = true\n");

        Assert.True((await Next(source)).Reconcile);
    }

    // npm install, a build or an unzip writes thousands of files at once and the OS drops events it cannot buffer; the
    // watcher says so once and goes on watching. Polling every five seconds covers the gap, but once the watcher's own
    // events arrive again the source is a watcher again: polling for ever re-reads the whole status every five seconds
    // and the status bar keeps saying the watcher is unavailable.
    // A burst on this machine's disk is not reliably more than the watcher's buffer, so the test raises the watcher's Error
    // event the way the OS raises it for a dropped buffer; the watcher itself goes on running, as it does after an overflow.
    [Fact]
    public async Task WhenTheOperatingSystemDropsEventsOnce_ShouldGoBackToWatchingOnceItsEventsArriveAgain()
    {
        using var source = await WatchingAsync();
        var watchers = (List<FileSystemWatcher>)typeof(RepositoryWatcher).Assembly.GetType("Trag.Infrastructure.Platform.Watching.RepositoryChangeSource")!
            .GetField("watchers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(source)!;
        typeof(FileSystemWatcher).GetMethod("OnError", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(watchers[0], [new ErrorEventArgs(new InternalBufferOverflowException())]);
        Assert.True(source.IsPolling);

        await Task.Delay(TimeSpan.FromSeconds(1));
        await Write("a.txt", "edited after the burst");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (source.IsPolling && DateTime.UtcNow < deadline) await Task.Delay(100);

        Assert.False(source.IsPolling);
    }

    [Fact]
    public async Task WhenTheWatchStarts_ShouldFirstAskForAReconcileWithoutPolling()
    {
        await Write("a.txt");
        using var source = new RepositoryWatcher().Watch(Snapshot);
        Assert.True((await Next(source)).Reconcile); Assert.False(source.IsPolling);
    }

    // Windows raises a change for a folder entry when a file inside it changes size or time; the entry is the root's own
    // or a folder's, and the file reports itself.
    [Fact]
    public async Task WhenAFileIsEdited_ShouldReportItsPath()
    {
        using var source = await WatchingAsync();
        await Write("a.txt", "edited");
        var change = await Next(source);
        Assert.False(change.Reconcile);
        Assert.Equal(["a.txt"], change.Paths);
    }

    // The folder is what counts: the status read of a folder finds the files in it. A file written the moment the folder
    // appears may not report itself on Linux, where the watch on the new folder starts only after the folder is seen.
    [Fact]
    public async Task WhenANewDirectoryAppearsWithAFile_ShouldReportTheDirectory()
    {
        using var source = await AfterAnEditAsync();
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await Write(Path.Combine("sub", "b.txt"));
        var change = await Next(source);
        Assert.False(change.Reconcile);
        Assert.Contains("sub", change.Paths);
        Assert.All(change.Paths, path => Assert.True(path == "sub" || path.StartsWith("sub/", StringComparison.Ordinal), path));
    }

    // A build makes a folder with a file in its output, rewrites another file there and deletes the folder before; the
    // watcher does not know that Git ignores the output, but none of it is a full read.
    [Fact]
    public async Task WhenABuildChurnsItsOutputFolder_ShouldReportOnlyItsPaths()
    {
        using var source = await WatchingAsync();
        var obj = Directory.CreateDirectory(Path.Combine(root, "obj")).FullName;
        for (var step = 0; step < 20; step++)
        {
            var folder = Directory.CreateDirectory(Path.Combine(obj, "step" + step)).FullName;
            await File.WriteAllTextAsync(Path.Combine(folder, "out.dll"), "x");
            await File.WriteAllTextAsync(Path.Combine(obj, "cache"), step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (step > 0) Directory.Delete(Path.Combine(obj, "step" + (step - 1)), true);
        }

        var change = await Next(source);

        Assert.False(change.Reconcile);
        Assert.All(change.Paths, path => Assert.StartsWith("obj", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAFileInANewDirectoryIsEdited_ShouldReportItsPath()
    {
        using var source = await AfterANewDirectoryAsync();
        await Write(Path.Combine("sub", "b.txt"), "y");
        var change = await Next(source);
        Assert.False(change.Reconcile);
        Assert.Equal(["sub/b.txt"], change.Paths);
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".gitattributes")]
    public async Task WhenAnIgnoreOrAttributesFileIsWritten_ShouldForceAReconcile(string name)
    {
        using var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        await Write(name, "*.tmp\n");
        Assert.True((await Next(source)).Reconcile);
    }

    [Fact]
    public async Task WhenAFileIsDeleted_ShouldReportItsPath()
    {
        using var source = await WatchingTwoFilesAsync();
        File.Delete(Path.Combine(root, "a.txt"));
        var change = await Next(source);
        Assert.False(change.Reconcile);
        Assert.Equal(["a.txt"], change.Paths);
    }

    [Fact]
    public async Task WhenAFileIsRenamed_ShouldReportBothNames()
    {
        using var source = await WatchingTwoFilesAsync();
        File.Delete(Path.Combine(root, "a.txt"));
        await Next(source);
        File.Move(Path.Combine(root, "b.txt"), Path.Combine(root, "c.txt"));
        var change = await Next(source);
        Assert.False(change.Reconcile);
        Assert.Equal(["b.txt", "c.txt"], change.Paths.Order(StringComparer.Ordinal));
    }

    // JetBrains IDEs save by writing a new copy beside the file, renaming the file away, renaming the copy over it and
    // deleting the old one; in the root and in a folder alike the change is those three names, not a full read.
    [Theory]
    [InlineData("a.txt")]
    [InlineData("src/a.txt")]
    public async Task WhenAnEditorSavesAFileByRenamingANewCopyOverIt_ShouldReportOnlyTheNamesItTouched(string file)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        await Write(file, "before");
        using var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);

        var full = Path.Combine(root, file);
        await File.WriteAllTextAsync(full + "___jb_tmp___", "after");
        File.Move(full, full + "___jb_old___");
        File.Move(full + "___jb_tmp___", full);
        File.Delete(full + "___jb_old___");
        var change = await Next(source);

        Assert.False(change.Reconcile);
        Assert.Equal([file, file + "___jb_old___", file + "___jb_tmp___"], change.Paths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WhenAReferenceIsUpdatedInTheGitDirectory_ShouldForceAReconcile()
    {
        using var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        await Write(Path.Combine(".git", "HEAD"), "ref: refs/heads/main\n");
        Assert.True((await Next(source)).Reconcile);
    }

    [Fact]
    public async Task WhenTheIndexIsUpdatedInTheGitDirectory_ShouldForceAReconcile()
    {
        using var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        await Write(Path.Combine(".git", "HEAD"), "ref: refs/heads/main\n");
        await Next(source);
        await Write(Path.Combine(".git", "index"), "DIRC");
        Assert.True((await Next(source)).Reconcile);
    }

    // A linked worktree keeps its own Git directory and the common one outside its root: an index written in the first is
    // a reconcile, and so is a branch another worktree moves in the second.
    [Fact]
    public async Task WhenTheGitDirectoryIsOutsideTheRoot_ShouldWatchItAndTheCommonDirectory()
    {
        var common = Path.Combine(Path.GetDirectoryName(root)!, "main.git");
        var own = Directory.CreateDirectory(Path.Combine(common, "worktrees", "repo")).FullName;
        var heads = Directory.CreateDirectory(Path.Combine(common, "refs", "heads")).FullName;
        using var source = new RepositoryWatcher().Watch(Snapshots.Create(root) with { GitDirectory = own, CommonDirectory = common });
        await Next(source);

        await File.WriteAllTextAsync(Path.Combine(own, "index"), "DIRC");
        Assert.True((await Next(source)).Reconcile);
        await File.WriteAllTextAsync(Path.Combine(heads, "main"), new string('a', 40) + "\n");
        Assert.True((await Next(source)).Reconcile);
    }

    [Fact]
    public async Task WhenTheRepositoryRootIsRemoved_ShouldNoticeItThroughTheParentWatcher()
    {
        using var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        Directory.Delete(root, true);
        Assert.True((await Next(source)).Reconcile);
    }

    // The second reconcile comes from the recovery timer.
    [Fact]
    public async Task WhenTheRootIsMissing_ShouldPollAtOnceAndKeepPolling()
    {
        using var source = new RepositoryWatcher().Watch(Snapshots.Create(Path.Combine(root, "missing")) with { GitDirectory = GitDirectory });
        Assert.True(source.IsPolling);
        Assert.True((await Next(source)).Reconcile);
        Assert.True((await Next(source)).Reconcile);
    }

    [Fact]
    public async Task WhenTheSourceIsDisposed_ShouldEndAPendingRead()
    {
        var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        var pending = Next(source);
        source.Dispose();
        await Assert.ThrowsAsync<ChannelClosedException>(() => pending);
    }

    [Fact]
    public async Task WhenTheSourceIsDisposedAgain_ShouldIgnoreIt()
    {
        var source = await DisposedWhileReadingAsync();
        source.Dispose();
    }

    [Fact]
    public async Task WhenADisposedSourceIsAskedToReconcile_ShouldIgnoreIt()
    {
        var source = await DisposedWhileReadingAsync();
        source.Dispose();
        source.Reconcile();
    }

    // Watching with a.txt in place, the first reconcile already read.
    private async Task<IRepositoryChangeSource> WatchingAsync()
    {
        await Write("a.txt");
        var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        return source;
    }

    // ...then a.txt edited and its change read.
    private async Task<IRepositoryChangeSource> AfterAnEditAsync()
    {
        var source = await WatchingAsync();
        await Write("a.txt", "edited");
        await Next(source);
        return source;
    }

    // ...then a new folder with a file in it, and their change read.
    private async Task<IRepositoryChangeSource> AfterANewDirectoryAsync()
    {
        var source = await AfterAnEditAsync();
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        await Write(Path.Combine("sub", "b.txt"));
        await Next(source);
        return source;
    }

    // Watching with a.txt and b.txt in place, the first reconcile already read.
    private async Task<IRepositoryChangeSource> WatchingTwoFilesAsync()
    {
        await Write("a.txt"); await Write("b.txt");
        var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        return source;
    }

    // Disposed while a read was waiting, which the dispose ended.
    private async Task<IRepositoryChangeSource> DisposedWhileReadingAsync()
    {
        var source = new RepositoryWatcher().Watch(Snapshot);
        await Next(source);
        var pending = Next(source);
        source.Dispose();
        await Assert.ThrowsAsync<ChannelClosedException>(() => pending);
        return source;
    }
}
