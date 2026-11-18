using Trag.Application.Live;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Live;

public sealed class CancelRegistryTests
{
    [Fact]
    public void WhenNoRegisteredWorkIsBusy_ShouldOfferNoCancel()
    {
        var (cancels, _) = Registered(() => false);
        Assert.False(cancels.CanCancel);
    }

    [Fact]
    public void WhenOneRegisteredWorkIsBusy_ShouldOfferCancel()
    {
        var (cancels, _) = Registered(() => true);
        Assert.True(cancels.CanCancel);
    }

    // The registry asks the busy checks every time; it keeps no answer of its own.
    [Fact]
    public void WhenTheBusyWorkIsDone_ShouldOfferNoCancelAgain()
    {
        var busy = true;
        var (cancels, _) = Registered(() => busy);
        Assert.True(cancels.CanCancel);
        busy = false;
        Assert.False(cancels.CanCancel);
    }

    [Fact]
    public void WhenEverythingIsCanceled_ShouldReachEveryRegistrationInOrderBusyOrNot()
    {
        var (cancels, cancelled) = Registered(() => false);
        cancels.CancelAll();
        Assert.Equal(["feature", "other"], cancelled);
    }

    // A registration disposed before its registry goes takes its work out of Cancel's reach: neither offered nor cancelled.
    [Fact]
    public void WhenARegistrationIsDisposed_ShouldNeitherOfferNorReachThatWorkAgain()
    {
        var cancels = new CancelRegistry();
        var cancelled = new List<string>();
        var feature = cancels.Register(() => true, () => cancelled.Add("feature"));
        cancels.Register(() => false, () => cancelled.Add("other"));
        Assert.Equal((true, 2), (cancels.CanCancel, cancels.Count));

        feature.Dispose();
        cancels.CancelAll();

        Assert.Equal((false, 1), (cancels.CanCancel, cancels.Count));
        Assert.Equal(["other"], cancelled);
    }

    [Fact]
    public void WhenAChangeIsNotified_ShouldAnnounceItOnce()
    {
        var cancels = new CancelRegistry();
        var announced = 0;
        cancels.Changed += () => announced++;
        cancels.NotifyChanged();
        Assert.Equal(1, announced);
    }

    [Fact]
    public async Task WhenOnlyTheApplicationHasBusyWork_ShouldOfferCancelOnTheApplicationAlone()
    {
        await using var workbench = new Workbench(RepositoryServices.None(), new FakeLastRepositoryStore(), new FakeRepositoryCatalog(), null);
        var (repository, _) = RegisterOnBoth(workbench);
        Assert.True(workbench.Cancels.CanCancel); Assert.False(repository.Cancels.CanCancel);
    }

    // The application and every repository keep registries of their own: what registers on one is not reached by the
    // other, so a repository's reads go with it.
    [Fact]
    public async Task WhenTheRepositoryCancelsEverything_ShouldReachOnlyItsOwnRegistrations()
    {
        await using var workbench = new Workbench(RepositoryServices.None(), new FakeLastRepositoryStore(), new FakeRepositoryCatalog(), null);
        var (repository, cancelled) = RegisterOnBoth(workbench);
        repository.Cancels.CancelAll();
        Assert.Equal(["repository"], cancelled);
    }

    // Two registrations: "feature", busy as the given check says, and "other", never busy.
    private static (CancelRegistry Cancels, List<string> Cancelled) Registered(Func<bool> featureBusy)
    {
        var cancels = new CancelRegistry();
        var cancelled = new List<string>();
        cancels.Register(featureBusy, () => cancelled.Add("feature"));
        cancels.Register(() => false, () => cancelled.Add("other"));
        return (cancels, cancelled);
    }

    // Registers busy work on the application and work that is not busy on its current repository.
    private static (Repository Repository, List<string> Cancelled) RegisterOnBoth(Workbench workbench)
    {
        var repository = workbench.Session.Current;
        var cancelled = new List<string>();
        workbench.Cancels.Register(() => true, () => cancelled.Add("application"));
        repository.Cancels.Register(() => false, () => cancelled.Add("repository"));
        return (repository, cancelled);
    }
}
