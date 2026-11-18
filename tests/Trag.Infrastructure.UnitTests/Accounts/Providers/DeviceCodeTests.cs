using Trag.Domain.Errors;
using Trag.Infrastructure.Accounts.Providers;

namespace Trag.Infrastructure.UnitTests.Accounts.Providers;

// The code GitHub hands out for one sign-in: what is accepted as one, how long it is good for, and how the polling pace
// follows GitHub's requests.
public sealed class DeviceCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static OAuthResponse Response(string? device = "device", string? user = "ABCD-1234", string? page = "https://github.com/login/device", int expires = 900, int interval = 5) =>
        new() { DeviceCode = device, UserCode = user, VerificationUri = page, ExpiresIn = expires, Interval = interval };

    [Fact]
    public void WhenTheResponseIsUsable_ShouldBecomeACodeWithItsDeadlineAndAtLeastTheDocumentedInterval()
    {
        var code = DeviceCode.Parse(Response(interval: 1), Now);
        Assert.Equal("device", code.Value);
        Assert.Equal("ABCD-1234", code.UserCode);
        Assert.Equal("https://github.com/login/device", code.VerificationUri.AbsoluteUri);
        Assert.Equal(Now.AddSeconds(900), code.Deadline);
        Assert.Equal(5, code.Interval);
    }

    [Fact]
    public void WhenTheResponseCarriesAnError_ShouldExplainItRatherThanParseIt() =>
        Assert.Equal("GitHub does not accept Trag's sign-in right now. Try again later, or install a newer Trag.",
            Assert.Throws<AccountException>(() => DeviceCode.Parse(new() { Error = "device_flow_disabled" }, Now)).Message);

    [Theory]
    [InlineData("", "ABCD-1234", "https://github.com/login/device", 900, 5)]
    [InlineData("device", " ", "https://github.com/login/device", 900, 5)]
    [InlineData("device", "ABCD-1234-ABCD-1234-ABCD-1234-ABCD", "https://github.com/login/device", 900, 5)]
    [InlineData("device", "ABCD 1234", "https://github.com/login/device", 900, 5)]
    [InlineData("device", "ABCD-1234", "https://evil.invalid/login/device", 900, 5)]
    [InlineData("device", "ABCD-1234", "https://github.com/login/device", 0, 5)]
    [InlineData("device", "ABCD-1234", "https://github.com/login/device", 3601, 5)]
    [InlineData("device", "ABCD-1234", "https://github.com/login/device", 900, -1)]
    [InlineData("device", "ABCD-1234", "https://github.com/login/device", 900, 301)]
    public void WhenTragCannotShowOrActOnTheResponse_ShouldRefuseIt(string device, string user, string page, int expires, int interval) =>
        Assert.Equal("GitHub returned an invalid sign-in response. Try again.",
            Assert.Throws<AccountException>(() => DeviceCode.Parse(Response(device, user, page, expires, interval), Now)).Message);

    [Fact]
    public void WhenMoreThanAnIntervalIsLeft_ShouldWaitTheInterval()
    {
        var code = DeviceCode.Parse(Response(expires: 12), Now);
        Assert.Equal(TimeSpan.FromSeconds(5), code.DelayUntilNextPoll(Now));
    }

    [Fact]
    public void WhenLessThanAnIntervalIsLeft_ShouldWaitOnlyUntilTheCodeExpires()
    {
        var code = DeviceCode.Parse(Response(expires: 12), Now);
        Assert.Equal(TimeSpan.FromSeconds(2), code.DelayUntilNextPoll(Now.AddSeconds(10)));
    }

    [Fact]
    public void WhenTheCodeHasExpired_ShouldNotWait()
    {
        var code = DeviceCode.Parse(Response(expires: 12), Now);
        Assert.Equal(TimeSpan.Zero, code.DelayUntilNextPoll(Now.AddSeconds(13)));
    }

    [Fact]
    public void WhenTheDeadlineIsStillAhead_ShouldNotBeExpired()
    {
        var code = DeviceCode.Parse(Response(expires: 12), Now);
        Assert.False(code.Expired(Now.AddSeconds(11)));
    }

    [Fact]
    public void WhenTheDeadlineIsReached_ShouldBeExpired()
    {
        var code = DeviceCode.Parse(Response(expires: 12), Now);
        Assert.True(code.Expired(Now.AddSeconds(12)));
    }

    [Fact]
    public void WhenGitHubAsksToSlowDownWithoutAnInterval_ShouldAddFiveSeconds()
    {
        var code = DeviceCode.Parse(Response(), Now);
        code.SlowDown(0);
        Assert.Equal(10, code.Interval);
    }

    // The interval was already slowed down once, to ten seconds.
    [Fact]
    public void WhenGitHubNamesALongerInterval_ShouldTakeIt()
    {
        var code = DeviceCode.Parse(Response(), Now);
        code.SlowDown(0);
        Assert.Equal(10, code.Interval);
        code.SlowDown(30);
        Assert.Equal(30, code.Interval);
    }

    [Fact]
    public void WhenGitHubNamesAnIntervalOverAnHour_ShouldWaitAnHour()
    {
        var code = DeviceCode.Parse(Response(), Now);
        code.SlowDown(0);
        Assert.Equal(10, code.Interval);
        code.SlowDown(30);
        Assert.Equal(30, code.Interval);
        code.SlowDown(100_000);
        Assert.Equal(3600, code.Interval);
    }
}
