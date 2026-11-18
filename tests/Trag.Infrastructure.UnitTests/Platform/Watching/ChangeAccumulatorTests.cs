using Trag.Infrastructure.Platform.Watching;

namespace Trag.Infrastructure.UnitTests.Platform.Watching;

public sealed class ChangeAccumulatorTests
{
    [Fact]
    public void WhenADuplicatePathArrivesAtCapacity_ShouldKeepEveryPathAndWaitAQuietPeriodFromIt()
    {
        var batch = new ChangeAccumulator(2);
        batch.Add("a", TimeSpan.Zero);
        batch.Add("b", TimeSpan.FromMilliseconds(100));
        batch.Add("a", TimeSpan.FromMilliseconds(200));

        Assert.Equal(2, batch.Count);
        Assert.Null(batch.Take(TimeSpan.FromMilliseconds(499)));
        var change = batch.Take(TimeSpan.FromMilliseconds(500))!;
        Assert.False(change.Reconcile);
        Assert.Equal(["a", "b"], change.Paths.Order().ToArray());
    }

    // A reconcile asked for exactly at the first change's deadline goes out with that batch, once, and leaves nothing behind.
    [Fact]
    public void WhenAReconcileArrivesAtExactlyTheDeadline_ShouldDeliverItOnce()
    {
        var batch = new ChangeAccumulator();
        batch.Add("a", TimeSpan.Zero);
        batch.Add(null, TimeSpan.FromSeconds(1));

        var change = batch.Take(TimeSpan.FromSeconds(1));

        Assert.True(change!.Reconcile);
        Assert.Null(batch.Take(TimeSpan.FromSeconds(1)));
        Assert.Null(batch.Take(TimeSpan.FromSeconds(5)));
    }

    // Changes that keep coming after the overflow do not push the reconcile past the first change's deadline.
    [Fact]
    public void WhenTheBatchOverflows_ShouldDropItsPathsAndAskForAReconcileAtTheOriginalDeadline()
    {
        var batch = Overflowed();

        Assert.Equal(0, batch.Count);
        Assert.Null(batch.Take(TimeSpan.FromMilliseconds(999)));
        var recovery = batch.Take(TimeSpan.FromSeconds(1))!;
        Assert.True(recovery.Reconcile);
        Assert.Empty(recovery.Paths);
        Assert.Equal(Timeout.InfiniteTimeSpan, batch.Delay(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WhenAPathArrivesAfterAnOverflowWasTaken_ShouldStartAFreshBatch()
    {
        var batch = Overflowed();
        batch.Take(TimeSpan.FromSeconds(1));

        batch.Add("next", TimeSpan.FromSeconds(2));
        var next = batch.Take(TimeSpan.FromMilliseconds(2300))!;
        Assert.False(next.Reconcile);
        Assert.Equal(["next"], next.Paths);
    }

    [Fact]
    public void WhenChangesArriveInABurst_ShouldDeliverEachPathOnceAfterAQuietPeriod()
    {
        var batch = new ChangeAccumulator();
        batch.Add("a", TimeSpan.Zero);
        batch.Add("a", TimeSpan.FromMilliseconds(200));
        batch.Add("b", TimeSpan.FromMilliseconds(250));
        Assert.Null(batch.Take(TimeSpan.FromMilliseconds(549)));
        var change = batch.Take(TimeSpan.FromMilliseconds(550))!;
        Assert.False(change.Reconcile);
        Assert.Equal(["a", "b"], change.Paths.Order().ToArray());
        Assert.Equal(Timeout.InfiniteTimeSpan, batch.Delay(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void WhenChangesNeverStop_ShouldStillDeliverThemWithinOneSecond()
    {
        var batch = new ChangeAccumulator();
        for (var i = 0; i < 10; i++) batch.Add("a", TimeSpan.FromMilliseconds(i * 100));
        Assert.Null(batch.Take(TimeSpan.FromMilliseconds(999)));
        Assert.NotNull(batch.Take(TimeSpan.FromSeconds(1)));
    }

    // Memory stays bounded however many paths the watcher sends.
    [Fact]
    public void WhenFarMorePathsArriveThanTheBatchHolds_ShouldKeepNoneAndAskForAReconcile()
    {
        var batch = Flooded();
        Assert.Equal(0, batch.Count);
        Assert.True(batch.Take(TimeSpan.FromSeconds(1))!.Reconcile);
    }

    [Fact]
    public void WhenAnUnknownChangeArrivesAfterAnOverflowWasTaken_ShouldAskForAReconcileAgain()
    {
        var batch = Flooded();
        batch.Take(TimeSpan.FromSeconds(1));

        batch.Add(null, TimeSpan.FromSeconds(2));
        batch.Add("one", TimeSpan.FromSeconds(2));
        Assert.True(batch.Take(TimeSpan.FromSeconds(3))!.Reconcile);
    }

    [Fact]
    public void WhenOnlyAPathArrivesAfterAnUnknownChangeWasTaken_ShouldNotAskForAReconcile()
    {
        var batch = Flooded();
        batch.Take(TimeSpan.FromSeconds(1));
        batch.Add(null, TimeSpan.FromSeconds(2));
        batch.Add("one", TimeSpan.FromSeconds(2));
        batch.Take(TimeSpan.FromSeconds(3));

        batch.Add("one", TimeSpan.FromSeconds(4));
        Assert.False(batch.Take(TimeSpan.FromSeconds(5))!.Reconcile);
    }

    [Fact]
    public void WhenNothingHasArrived_ShouldHaveNothingToTake()
    {
        var batch = new ChangeAccumulator();
        Assert.Null(batch.Take(TimeSpan.Zero));
    }

    [Fact]
    public void WhenAnUnknownChangeComesFirst_ShouldDropTheFollowingPathsAndAskForAReconcile()
    {
        var batch = new ChangeAccumulator();
        batch.Add(null, TimeSpan.Zero);
        batch.Add("a", TimeSpan.FromMilliseconds(10));
        Assert.Equal(0, batch.Count);
        var change = batch.Take(TimeSpan.FromSeconds(1))!;
        Assert.True(change.Reconcile); Assert.Empty(change.Paths);
    }

    [Fact]
    public void WhenAReconcileIsTaken_ShouldLeaveAnEmptyBatchWithNothingPending()
    {
        var batch = ReconcileTaken();
        Assert.Equal(0, batch.Count); Assert.Null(batch.Take(TimeSpan.FromSeconds(2)));
        Assert.Equal(Timeout.InfiniteTimeSpan, batch.Delay(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void WhenAPathArrivesAfterAReconcileWasTaken_ShouldWaitAQuietPeriodForIt()
    {
        var batch = ReconcileTaken();
        batch.Add("b", TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromMilliseconds(300), batch.Delay(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, batch.Count);
    }

    // A batch of one path overflowed by its second change and still changing just before its one-second deadline.
    private static ChangeAccumulator Overflowed()
    {
        var batch = new ChangeAccumulator(1);
        batch.Add("a", TimeSpan.Zero);
        batch.Add("b", TimeSpan.FromMilliseconds(800));
        batch.Add("c", TimeSpan.FromMilliseconds(950));
        return batch;
    }

    // A batch of four paths at most, sent ten thousand at once.
    private static ChangeAccumulator Flooded()
    {
        var batch = new ChangeAccumulator(4);
        for (var i = 0; i < 10_000; i++) batch.Add(i.ToString(), TimeSpan.Zero);
        return batch;
    }

    // An unknown change and a path after it, taken as a reconcile.
    private static ChangeAccumulator ReconcileTaken()
    {
        var batch = new ChangeAccumulator();
        batch.Add(null, TimeSpan.Zero);
        batch.Add("a", TimeSpan.FromMilliseconds(10));
        batch.Take(TimeSpan.FromSeconds(1));
        return batch;
    }
}
