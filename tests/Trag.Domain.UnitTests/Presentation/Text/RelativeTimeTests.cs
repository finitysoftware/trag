using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class RelativeTimeTests
{
    // A time text reads no clock: given a time an hour after it, a fetch says only its clock time, any day.
    [Fact]
    public void WhenAFetchIsShown_ShouldCountFromTheTimeItIsGiven()
    {
        var fetched = new DateTimeOffset(2001, 2, 3, 10, 30, 0, TimeSpan.Zero);
        Assert.Equal(fetched.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), RelativeTime.Fetched(fetched, fetched.AddMinutes(1)));
        Assert.Equal("2 hours ago", RelativeTime.Describe(fetched, fetched.AddHours(2)));
    }

    [Fact]
    public void WhenLessThanAMinuteHasPassed_ShouldSayJustNow()
    {
        var now = Now();
        Assert.Equal("just now", RelativeTime.Describe(now.AddSeconds(-30), now));
    }

    // A commit dated ahead of this clock, as a skewed clock writes it, is no negative time ago.
    [Fact]
    public void WhenTheTimeIsInTheFuture_ShouldSayJustNow()
    {
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", RelativeTime.Describe(now.AddHours(3), now));
    }

    [Fact]
    public void WhenMinutesHavePassed_ShouldCountThemInTheSingularOrThePlural()
    {
        var now = Now();
        Assert.Equal("1 minute ago", RelativeTime.Describe(now.AddMinutes(-1), now));
        Assert.Equal("5 minutes ago", RelativeTime.Describe(now.AddMinutes(-5), now));
    }

    [Fact]
    public void WhenHoursHavePassed_ShouldCountThem()
    {
        var now = Now();
        Assert.Equal("3 hours ago", RelativeTime.Describe(now.AddHours(-3), now));
    }

    [Fact]
    public void WhenMoreThanADayButLessThanTwoHavePassed_ShouldSayYesterday()
    {
        var now = Now();
        Assert.Equal("yesterday", RelativeTime.Describe(now.AddHours(-30), now));
    }

    [Fact]
    public void WhenDaysHavePassed_ShouldCountThem()
    {
        var now = Now();
        Assert.Equal("5 days ago", RelativeTime.Describe(now.AddDays(-5), now));
    }

    [Fact]
    public void WhenWeeksHavePassed_ShouldCountThem()
    {
        var now = Now();
        Assert.Equal("3 weeks ago", RelativeTime.Describe(now.AddDays(-21), now));
    }

    [Fact]
    public void WhenMonthsHavePassed_ShouldCountThem()
    {
        var now = Now();
        Assert.Equal("3 months ago", RelativeTime.Describe(now.AddDays(-100), now));
    }

    [Fact]
    public void WhenYearsHavePassed_ShouldCountThem()
    {
        var now = Now();
        Assert.Equal("2 years ago", RelativeTime.Describe(now.AddDays(-800), now));
    }

    [Fact]
    public void WhenSomethingWasOpenedToday_ShouldSayToday()
    {
        var now = Now();
        Assert.StartsWith("Today ", RelativeTime.Opened(now, now));
    }

    [Fact]
    public void WhenSomethingWasOpenedYesterday_ShouldSayYesterday()
    {
        var now = Now();
        Assert.StartsWith("Yesterday ", RelativeTime.Opened(now.AddDays(-1), now));
    }

    [Fact]
    public void WhenSomethingWasOpenedBeforeYesterday_ShouldGiveTheLocalDateAndTime()
    {
        var now = Now();
        var local = now.ToLocalTime();
        Assert.Equal(local.AddDays(-3).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), RelativeTime.Opened(now.AddDays(-3), now));
    }

    private static DateTimeOffset Now() => new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
}
