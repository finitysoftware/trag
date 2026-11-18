using Trag.Domain.History;
using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.History;

public sealed class HistoryQueryTests
{
    [Fact]
    public void WhenTheTextFitsTheLimit_ShouldBeValid()
    {
        Assert.True(HistoryQuery.IsValidText(""));
        Assert.True(HistoryQuery.IsValidText(new string('a', GitLimits.QueryLength)));
    }

    [Fact]
    public void WhenTheTextIsOverTheLimit_ShouldBeInvalid()
    {
        Assert.False(HistoryQuery.IsValidText(new string('a', GitLimits.QueryLength + 1)));
    }

    [Fact]
    public void WhenTheTextCarriesANul_ShouldBeInvalid()
    {
        Assert.False(HistoryQuery.IsValidText("a\0b"));
    }
}
