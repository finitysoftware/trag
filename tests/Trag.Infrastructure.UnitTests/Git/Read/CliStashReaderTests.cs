using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// The stash list as real Git writes it.
public sealed class CliStashReaderTests
{
    // Git takes a time zone offset beyond fourteen hours from GIT_AUTHOR_DATE and writes it into the stash commit; the
    // entry is still told by the moment it was made, not by year one.
    [Fact]
    public async Task WhenAStashsDateHasAnOffsetBeyondFourteenHours_ShouldReadTheMomentItWasMade()
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        var pushed = await fixture.Runner.RunAsync(fixture.Root, ["stash", "push", "-m", "far east"], fixture.Token,
            new Dictionary<string, string?> { ["GIT_AUTHOR_DATE"] = "1700000000 +1500" });
        Assert.Equal(0, pushed.ExitCode);

        var entry = Assert.Single(await new CliStashReader(fixture.Runner).ReadStashesAsync(fixture.Root, fixture.Token));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), entry.When);
    }

    // Git takes a date in seconds past the year 9999, past even what a long holds, and %at prints it as given; Git's own
    // %aI shows such a date as 1970, and one such stash must not cost the whole list.
    [Theory]
    [InlineData("@300000000000 +0000")]
    [InlineData("@9223372036854775808 +0000")]
    public async Task WhenAStashsDateIsBeyondWhatADateCanHold_ShouldListItAtTheEpochAsGitShowsIt(string date)
    {
        using var fixture = await GitFixture.CreateAsync();
        await fixture.WriteAsync("a.txt", "one\n");
        await fixture.CommitAsync("first");
        await fixture.WriteAsync("a.txt", "changed\n");
        var pushed = await fixture.Runner.RunAsync(fixture.Root, ["stash", "push", "-m", "far future"], fixture.Token,
            new Dictionary<string, string?> { ["GIT_AUTHOR_DATE"] = date });
        Assert.Equal(0, pushed.ExitCode);

        var entry = Assert.Single(await new CliStashReader(fixture.Runner).ReadStashesAsync(fixture.Root, fixture.Token));

        Assert.Equal(DateTimeOffset.UnixEpoch, entry.When);
    }
}
