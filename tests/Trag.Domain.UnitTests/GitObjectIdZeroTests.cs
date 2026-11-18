namespace Trag.Domain.UnitTests;

// An all-zero id is Git's way of saying there is no object, and of naming a line no commit has yet.
public sealed class GitObjectIdZeroTests
{
    [Fact]
    public void WhenAnIdIsAllZeros_ShouldBeNoObject()
    {
        Assert.True(GitObjectId.IsZero(new string('0', 40)));
        Assert.True(GitObjectId.IsZero(""));
        Assert.False(GitObjectId.IsZero(new string('0', 39) + "1"));
    }
}
