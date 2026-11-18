using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

public sealed class RefNamesTests
{
    [Theory]
    [InlineData("feature/topic", 255, true)]
    [InlineData("", 255, false)]
    [InlineData("-dashed", 255, false)]
    [InlineData("with space", 255, false)]
    [InlineData("with\ttab", 255, false)]
    [InlineData("with\u0001control", 255, false)]
    [InlineData("refs/remotes/origin/main", 1024, true)]
    public void WhenANameIsChecked_ShouldBeSafeOnlyWhenShortEnoughVisibleAndNotAnOption(string value, int maxLength, bool expected) =>
        Assert.Equal(expected, RefNames.HasSafeCharacters(value, maxLength));

    // The length limit belongs to the caller: the same name passes a larger one.
    [Fact]
    public void WhenANameIsLongerThanTheCallersLimit_ShouldBeUnsafe()
    {
        var name = new string('a', 256);
        Assert.False(RefNames.HasSafeCharacters(name, 255));
    }

    [Fact]
    public void WhenANameIsWithinTheCallersLimit_ShouldBeSafe()
    {
        var name = new string('a', 256);
        Assert.True(RefNames.HasSafeCharacters(name, 1024));
    }

    [Fact]
    public void WhenTheRefIsARemoteTrackingBranch_ShouldNameItWithoutTheRemotesPrefix() =>
        Assert.Equal("origin/main", RefNames.RemoteTrackingName("refs/remotes/origin/main"));

    [Fact]
    public void WhenTheRefIsNotARemoteTrackingBranch_ShouldKeepTheWholeRef() =>
        Assert.Equal("refs/heads/main", RefNames.RemoteTrackingName("refs/heads/main"));

    [Theory]
    [InlineData("refs/heads/main", "main")]
    [InlineData("refs/heads/heads/main", "heads/main")]
    [InlineData("HEAD", null)]
    [InlineData("refs/tags/main", null)]
    public void WhenAFullRefNameIsRead_ShouldNameTheLocalBranchItIsOrNone(string fullName, string? expected) =>
        Assert.Equal(expected, RefNames.LocalBranch(fullName));
}
