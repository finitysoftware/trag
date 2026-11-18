using Trag.Application.Sync;
using Trag.Domain.Sync;

namespace Trag.Application.UnitTests.Sync;

// When Trag fetches on its own: at once without a fetch, then after the chosen interval since the last one (the user's
// own included), on activation after 15 seconds, later and later after failures, never again after a refused sign-in
// until a fetch succeeds, and never when off.
public sealed class AutoFetchScheduleTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenNoFetchRanYet_ShouldBeDueAtOnce()
    {
        var schedule = new AutoFetchSchedule();
        Assert.True(schedule.IsDue(Start));
    }

    [Fact]
    public void WhenAFetchSucceeded_ShouldBeDueOnlyOnceTheIntervalPassed()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        Assert.False(schedule.IsDue(Start.AddSeconds(59)));
        Assert.True(schedule.IsDue(Start.AddMinutes(1)));
    }

    // Whoever ran the later fetch, the user included.
    [Fact]
    public void WhenALaterFetchSucceeded_ShouldCountTheIntervalFromIt()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        schedule.Succeeded(Start.AddSeconds(50));
        Assert.False(schedule.IsDue(Start.AddMinutes(1)));
    }

    [Fact]
    public void WhenAnOlderFetchIsReportedAfterANewerOne_ShouldBeDueOnlyAMinuteAfterTheNewer()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start.AddSeconds(50));
        schedule.Succeeded(Start);
        // A minute after the older one has passed already; only the newer one may count.
        Assert.False(schedule.IsDue(Start.AddSeconds(109)));
        Assert.True(schedule.IsDue(Start.AddSeconds(110)));
    }

    [Fact]
    public void WhenTheIntervalIsFifteenMinutes_ShouldNotBeDueBefore()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        schedule.Interval = AutoFetchInterval.FifteenMinutes;
        Assert.False(schedule.IsDue(Start.AddMinutes(14)));
    }

    [Fact]
    public void WhenTheFetchIsOff_ShouldNeverBeDueNorOnActivation()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        schedule.Interval = AutoFetchInterval.Off;
        Assert.False(schedule.IsDue(Start.AddHours(1))); Assert.False(schedule.IsDueOnActivation(Start.AddHours(1)));
    }

    [Fact]
    public void WhenNoFetchRanYet_ShouldFetchOnActivation()
    {
        var schedule = new AutoFetchSchedule();
        Assert.True(schedule.IsDueOnActivation(Start));
    }

    [Fact]
    public void WhenTheWindowComesBackAfterAFetch_ShouldFetchOnlyAfterFifteenSeconds()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        Assert.False(schedule.IsDueOnActivation(Start.AddSeconds(14)));
        Assert.True(schedule.IsDueOnActivation(Start.AddSeconds(15)));
    }

    [Fact]
    public void WhenFetchesFail_ShouldWaitLongerEachTimeAndNotFetchOnActivation()
    {
        var schedule = new AutoFetchSchedule();
        var at = Start;
        foreach (var wait in new[] { 1, 2, 5, 10, 10 })
        {
            schedule.Failed(at, refusedCredentials: false);
            Assert.False(schedule.IsDue(at.AddMinutes(wait).AddSeconds(-1))); Assert.False(schedule.IsDueOnActivation(at.AddMinutes(1)));
            Assert.True(schedule.IsDue(at.AddMinutes(wait)));
            at = at.AddMinutes(wait);
        }
    }

    // The failures before it waited up to ten minutes; the success brings back the chosen interval.
    [Fact]
    public void WhenAFetchSucceedsAfterFailures_ShouldBeDueAfterTheIntervalAgain()
    {
        var schedule = new AutoFetchSchedule();
        var at = Start;
        foreach (var wait in new[] { 1, 2, 5, 10, 10 })
        {
            schedule.Failed(at, refusedCredentials: false);
            at = at.AddMinutes(wait);
        }

        schedule.Succeeded(at);

        Assert.True(schedule.IsDue(at.AddMinutes(1)));
    }

    // A push the remote accepted ends the failures too: the next fetch is due by the interval since the last success.
    [Fact]
    public void WhenTheScheduleResumesAfterFailures_ShouldBeDueByTheIntervalAgain()
    {
        var schedule = new AutoFetchSchedule();
        schedule.Succeeded(Start);
        schedule.Failed(Start.AddMinutes(1), refusedCredentials: false);
        schedule.Failed(Start.AddMinutes(2), refusedCredentials: false);

        schedule.Resume();

        Assert.True(schedule.IsDue(Start.AddMinutes(2).AddSeconds(30)));
    }

    [Fact]
    public void WhenTheRemoteRefusesTheSignIn_ShouldStopAndFetchNeitherLaterNorOnActivation()
    {
        var schedule = new AutoFetchSchedule();
        var at = Start;

        schedule.Failed(at, refusedCredentials: true);

        Assert.True(schedule.IsStopped);
        Assert.False(schedule.IsDue(at.AddHours(1))); Assert.False(schedule.IsDueOnActivation(at.AddHours(1)));
    }

    // Failures and a success came before the refused sign-in: the success cleared the failures, so the resumed schedule
    // waits the interval, not a back-off.
    [Fact]
    public void WhenAFetchSucceedsAfterARefusedSignIn_ShouldResumeFromIt()
    {
        var schedule = new AutoFetchSchedule();
        var at = Start;
        foreach (var wait in new[] { 1, 2, 5, 10, 10 })
        {
            schedule.Failed(at, refusedCredentials: false);
            at = at.AddMinutes(wait);
        }
        schedule.Succeeded(at);
        schedule.Failed(at, refusedCredentials: true);

        schedule.Succeeded(at.AddMinutes(3));

        Assert.False(schedule.IsStopped); Assert.True(schedule.IsDue(at.AddMinutes(4)));
    }
}
