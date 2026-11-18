using Trag.Infrastructure.Platform.Watching;

namespace Trag.Infrastructure.UnitTests.Platform.Watching;

// What a filesystem event means for the status. A single tracked file can be re-read on its own; anything that can
// change what other files mean needs the whole status, and Git's own churn means nothing at all.
public sealed class WatchedPathClassifierTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-watch-" + Guid.NewGuid().ToString("N"));

    public WatchedPathClassifierTests() => Directory.CreateDirectory(root);

    private WatchDecision Working(string relative, WatcherChangeTypes kind = WatcherChangeTypes.Changed) =>
        WatchedPathClassifier.WorkingPath(root, Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)), kind);

    private WatchDecision Git(string relative) =>
        WatchedPathClassifier.GitPath(root, Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void WhenAFileChanges_ShouldReReadItOnItsOwnWithForwardSlashes()
    {
        var decision = Working("src/deep/file.cs");
        Assert.Equal(WatchDecision.Kind.Signal, decision.Action);
        Assert.Equal("src/deep/file.cs", decision.Path);
    }

    // The superproject's status knows a submodule only as one entry, so a file changing inside it is that entry's change,
    // however deep the file and however many submodules it has of its own.
    [Theory]
    [InlineData("sub/file.c")]
    [InlineData("sub/deep/file.c")]
    [InlineData("sub/inner/file.c")]
    public void WhenAFileInsideASubmoduleChanges_ShouldReReadTheSubmodulesEntry(string relative)
    {
        foreach (var repository in new[] { "sub", "sub/inner" })
        {
            Directory.CreateDirectory(Path.Combine(root, repository));
            File.WriteAllText(Path.Combine(root, repository, ".git"), "gitdir: elsewhere\n");
        }

        var decision = Working(relative);

        Assert.Equal((WatchDecision.Kind.Signal, "sub"), (decision.Action, decision.Path));
    }

    // The ignore rules change what every other file means.
    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".gitattributes")]
    [InlineData("nested/.gitignore")]
    public void WhenAnIgnoreOrAttributesFileChanges_ShouldReadTheWholeStatus(string relative) =>
        Assert.Equal(WatchDecision.Kind.Reconcile, Working(relative).Action);

    // An editor's safe write renames a new copy over the file and deletes the old one. A removed name may have been a
    // folder; the read of that name covers what lay under it.
    [Theory]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Renamed)]
    public void WhenAFileIsRemovedOrRenamed_ShouldReReadItsPathOnItsOwn(WatcherChangeTypes kind)
    {
        var decision = Working("src/file.cs", kind);
        Assert.Equal((WatchDecision.Kind.Signal, "src/file.cs"), (decision.Action, decision.Path));
    }

    // A nested repository's own .git turns its folder into one row or back into files.
    [Theory]
    [InlineData(WatcherChangeTypes.Deleted)]
    [InlineData(WatcherChangeTypes.Renamed)]
    public void WhenANestedRepositorysGitEntryIsRemovedOrRenamed_ShouldReadTheWholeStatus(WatcherChangeTypes kind) =>
        Assert.Equal(WatchDecision.Kind.Reconcile, Working("nested/.git", kind).Action);

    // A build makes folders in its ignored output all the time; the read of a folder's name covers what it holds.
    [Fact]
    public void WhenADirectoryAppears_ShouldReReadItsPath()
    {
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        var decision = Working("folder", WatcherChangeTypes.Created);
        Assert.Equal((WatchDecision.Kind.Signal, "folder"), (decision.Action, decision.Path));
    }

    // Windows changes a folder's time whenever an entry in it is added, removed or renamed, and the entry reports itself.
    [Fact]
    public void WhenADirectorysOwnTimeChanges_ShouldIgnoreIt()
    {
        Directory.CreateDirectory(Path.Combine(root, "folder"));
        Assert.Equal(WatchDecision.Kind.Ignore, Working("folder").Action);
    }

    [Fact]
    public void WhenTheGitDirectoryItselfChangesInTheWorkingTree_ShouldReadTheWholeStatus() =>
        Assert.Equal(WatchDecision.Kind.Reconcile, Working(".git").Action);

    [Fact]
    public void WhenAFileInsideTheGitDirectoryChangesInTheWorkingTree_ShouldIgnoreItInAnyCase()
    {
        Assert.Equal(WatchDecision.Kind.Ignore, Working(".git/index").Action);
        Assert.Equal(WatchDecision.Kind.Ignore, Working(".GIT/index").Action);
    }

    // Nothing the user sees changes.
    [Theory]
    [InlineData("trag-write-0123.index")]
    [InlineData("objects/ab/cdef")]
    [InlineData("logs/HEAD")]
    [InlineData("index.lock")]
    [InlineData("refs/heads/main.lock")]
    public void WhenGitChurnsItsOwnFiles_ShouldIgnoreThem(string relative) =>
        Assert.Equal(WatchDecision.Kind.Ignore, Git(relative).Action);

    [Theory]
    [InlineData("index")]
    [InlineData("HEAD")]
    [InlineData("MERGE_HEAD")]
    [InlineData("refs/heads/main")]
    [InlineData("rebase-merge/done")]
    [InlineData("refs/heads/sharedindex.topic")]
    [InlineData("refs/heads/fsmonitor--daemon")]
    public void WhenAnythingElseInTheGitDirectoryChanges_ShouldReadTheWholeStatus(string relative)
    {
        var decision = Git(relative);
        Assert.Equal(WatchDecision.Kind.Reconcile, decision.Action);
        Assert.Null(decision.Path);
    }

    // Git freshens a split index's shared file on every read of the index, Trag's own status included, and writes a new
    // one only beside a new index. A linked worktree and a submodule keep theirs in their own git directory inside this one.
    [Theory]
    [InlineData("sharedindex.0123456789abcdef0123456789abcdef01234567")]
    [InlineData("sharedindex_a1B2c3")]
    [InlineData("worktrees/linked/sharedindex.0123456789abcdef0123456789abcdef01234567")]
    [InlineData("modules/sub/sharedindex.0123456789abcdef0123456789abcdef01234567")]
    public void WhenGitTouchesASharedIndex_ShouldIgnoreIt(string relative) =>
        Assert.Equal(WatchDecision.Kind.Ignore, Git(relative).Action);

    // A submodule added from a repository already at its path keeps its git directory inside the working tree, where only
    // the working tree's watcher sees it; its index still changes the submodule's entry.
    [Fact]
    public void WhenGitTouchesASharedIndexInsideASubmodulesOwnGitDirectory_ShouldIgnoreIt()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub", ".git"));

        Assert.Equal(WatchDecision.Kind.Ignore, Working("sub/.git/sharedindex.0123456789abcdef0123456789abcdef01234567").Action);
        Assert.Equal((WatchDecision.Kind.Signal, "sub"), (Working("sub/.git/index").Action, Working("sub/.git/index").Path));
    }

    // Git's file-system monitor creates and deletes a cookie in its own folder for every query, Trag's own reads included.
    // A linked worktree's monitor and a submodule's keep theirs in their git directory inside this one.
    [Theory]
    [InlineData("fsmonitor--daemon")]
    [InlineData("fsmonitor--daemon/cookies")]
    [InlineData("fsmonitor--daemon/cookies/71928-1")]
    [InlineData("worktrees/linked/fsmonitor--daemon/cookies/98040-1")]
    [InlineData("modules/sub/fsmonitor--daemon/cookies/131004-1")]
    public void WhenGitsFileSystemMonitorWritesItsCookies_ShouldIgnoreThem(string relative) =>
        Assert.Equal(WatchDecision.Kind.Ignore, Git(relative).Action);

    // A submodule's git directory inside the working tree keeps its monitor's cookies where only this watcher sees them.
    [Fact]
    public void WhenGitsFileSystemMonitorWritesItsCookiesInsideASubmodulesOwnGitDirectory_ShouldIgnoreThem()
    {
        Directory.CreateDirectory(Path.Combine(root, "sub", ".git", "fsmonitor--daemon", "cookies"));

        Assert.Equal(WatchDecision.Kind.Ignore, Working("sub/.git/fsmonitor--daemon/cookies/55672-1", WatcherChangeTypes.Created).Action);
        Assert.Equal(WatchDecision.Kind.Ignore, Working("sub/.git/fsmonitor--daemon/cookies/55672-1", WatcherChangeTypes.Deleted).Action);
        Assert.Equal(WatchDecision.Kind.Ignore, Working("sub/.git/fsmonitor--daemon/cookies").Action);
    }

    // In a reftable repository HEAD is the stub "ref: refs/heads/.invalid" and refs/heads a file that never changes, so a
    // ref a terminal command creates, moves or deletes shows only as a new table and the tables.list swapped in.
    [Theory]
    [InlineData("reftable/0x000000000003-0x000000000003-983a1fc3.ref")]
    [InlineData("reftable/tables.list")]
    public void WhenAReftableTableIsWritten_ShouldReconcile(string relative) =>
        Assert.Equal(WatchDecision.Kind.Reconcile, Git(relative).Action);

    [Fact]
    public void WhenTablesListLockAppears_ShouldIgnoreIt() =>
        Assert.Equal(WatchDecision.Kind.Ignore, Git("reftable/tables.list.lock").Action);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
