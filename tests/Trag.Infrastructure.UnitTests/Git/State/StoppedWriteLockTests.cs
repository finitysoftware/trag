using Trag.Infrastructure.Git.State;

namespace Trag.Infrastructure.UnitTests.Git.State;

// Git writes the index, a ref or packed-refs through a lock beside it, and a Git that Cancel kills never removes it. The
// lock a write's own Git left goes; a lock that was there before the write, or one of a file the write does not name,
// belongs to another program and always stays.
public sealed class StoppedWriteLockTests : IDisposable
{
    private readonly string gitDirectory = Path.Combine(Path.GetTempPath(), "trag-lock-" + Guid.NewGuid().ToString("N"));

    public StoppedWriteLockTests() => Directory.CreateDirectory(gitDirectory);
    public void Dispose() => Directory.Delete(gitDirectory, true);

    private static CancellationToken Canceled() => new(canceled: true);

    private StoppedWriteLock Writing(string file) => new(Path.Combine(gitDirectory, file));

    private string Lock(string file)
    {
        var path = Path.Combine(gitDirectory, file + ".lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
        return path;
    }

    [Fact]
    public void WhenTheWritesOwnGitWasStoppedAndLeftTheLock_ShouldRemoveIt()
    {
        var left = Writing("index");
        var path = Lock("index");

        left.Release(Canceled());

        Assert.False(File.Exists(path));
    }

    // A fetch mapping's pattern names every ref it brings in, also one below a folder the fetch creates.
    [Theory]
    [InlineData("HEAD", "HEAD")]
    [InlineData("refs/remotes/origin/*", "refs/remotes/origin/feature/x")]
    public void WhenTheStoppedGitLeftTheLockOfARefItWasUpdating_ShouldRemoveIt(string written, string locked)
    {
        var left = Writing(written);
        var path = Lock(locked);

        left.Release(Canceled());

        Assert.False(File.Exists(path));
    }

    // Git rewrites packed-refs into packed-refs.new while it holds packed-refs.lock.
    [Fact]
    public void WhenTheStoppedGitLeftTheNewPackedRefs_ShouldRemoveIt()
    {
        var left = Writing("packed-refs");
        var path = Path.Combine(gitDirectory, "packed-refs.new");
        File.WriteAllBytes(path, []);

        left.Release(Canceled());

        Assert.False(File.Exists(path));
    }

    // A reftable repository writes every ref through one lock, the stack's table list.
    [Fact]
    public void WhenTheStoppedGitLeftTheLockOfAReftableStack_ShouldRemoveIt()
    {
        var left = new StoppedWriteLock(StoppedWriteLock.RefStorage(gitDirectory, "refs/remotes/origin/*"));
        var path = Lock("reftable/tables.list");

        left.Release(Canceled());

        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("index", "index")]
    [InlineData("refs/remotes/origin/*", "refs/remotes/origin/trunk")]
    public void WhenTheLockWasThereBeforeTheWrite_ShouldLeaveItEvenWhenTheWriteWasStopped(string written, string locked)
    {
        var path = Lock(locked);
        var left = Writing(written);

        left.Release(Canceled());

        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData("refs/remotes/origin/*", "refs/remotes/upstream/trunk")]
    [InlineData("refs/remotes/origin/feature-*", "refs/remotes/origin/trunk")]
    [InlineData("refs/heads/trunk", "HEAD")]
    public void WhenALockAppearsForARefTheRunDidNotUpdate_ShouldLeaveIt(string written, string locked)
    {
        var left = Writing(written);
        var path = Lock(locked);

        left.Release(Canceled());

        Assert.True(File.Exists(path));
    }

    // A write that was not stopped and still finds a lock afterwards met one some other program took meanwhile.
    [Fact]
    public void WhenTheWriteWasNotStopped_ShouldLeaveALockItFindsAfterwards()
    {
        var left = Writing("index");
        var path = Lock("index");

        left.Release(CancellationToken.None);

        Assert.True(File.Exists(path));
    }
}
