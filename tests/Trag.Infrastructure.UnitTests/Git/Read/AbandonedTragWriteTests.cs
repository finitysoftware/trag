using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.State;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// A Trag ended in the middle of a write (Task Manager, a crash) leaves the index.lock it took, closed now, and its
// private index. The Git it started is not ended with it and may finish the write on that private index, while the
// index Git reads stays as it was.
public sealed class AbandonedTragWriteTests
{
    [Fact]
    public async Task WhenTragWasEndedMidWrite_ShouldReportTheRecoveryIndexBesideTheLockItLeft()
    {
        using var f = await GitFixture.CreateAsync("main");
        var gitDirectory = await CommitAndCopyIndexAsync(f);
        File.Create(Path.Combine(gitDirectory, "index.lock")).Dispose();

        Assert.Equal([LimitationText.InterruptedWrite], (await f.ReadAsync()).Limitations);
    }

    // Without a lock the private index is one Trag kept because it could not reconcile a write, not one to put in place.
    [Fact]
    public async Task WhenNoLockLiesBesideTheRecoveryIndex_ShouldAskForAReviewNotToPutItInPlace()
    {
        using var f = await GitFixture.CreateAsync("main");
        await CommitAndCopyIndexAsync(f);

        Assert.Equal([LimitationText.RecoveryIndex], (await f.ReadAsync()).Limitations);
    }

    // A write in progress holds index.lock open from start to end; its private index is work in progress.
    [Fact]
    public async Task WhenALiveTragWriteHoldsTheLock_ShouldNotReportItsPrivateIndex()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "base\n"); await f.CommitAsync("Base");
        await using var transaction = new IndexTransaction(Path.Combine(f.Root, ".git"));
        await transaction.CopyAsync(f.Token);

        Assert.Empty((await f.ReadAsync()).Limitations);
    }

    // The private index is a copy of the index, as Trag makes it before Git runs.
    private static async Task<string> CommitAndCopyIndexAsync(GitFixture f)
    {
        await f.WriteAsync("a.txt", "base\n"); await f.CommitAsync("Base");
        var gitDirectory = Path.Combine(f.Root, ".git");
        File.Copy(Path.Combine(gitDirectory, "index"), Path.Combine(gitDirectory, IndexTransaction.NamePrefix + Guid.NewGuid().ToString("N") + ".index"));
        return gitDirectory;
    }
}
