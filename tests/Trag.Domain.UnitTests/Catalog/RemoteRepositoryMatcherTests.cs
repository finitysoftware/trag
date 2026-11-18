using Trag.Domain.Catalog;

namespace Trag.Domain.UnitTests.Catalog;

public sealed class RemoteRepositoryMatcherTests
{
    [Fact]
    public void WhenALocalCloneHasTheSameOriginInAnotherUrlForm_ShouldFindIt()
    {
        var matcher = Matcher();
        Assert.Equal("a", matcher.Find("https://github.com/owner/repo")!.Path);
        Assert.Equal("c", matcher.Find("ssh://git@gitlab.example/team/tool/")!.Path);
    }

    [Fact]
    public void WhenNoLocalCloneHasTheOrigin_ShouldFindNothing()
    {
        var matcher = Matcher();
        Assert.Null(matcher.Find("https://github.com/owner/other"));
    }

    // The clone b has no origin, which an empty address must not match.
    [Fact]
    public void WhenTheAddressIsEmpty_ShouldFindNothing()
    {
        var matcher = Matcher();
        Assert.Null(matcher.Find(""));
    }

    // Clone a with an scp-style origin in other letter case, b without an origin, c with an https origin.
    private static RemoteRepositoryMatcher<RepositoryEntry> Matcher()
    {
        var now = DateTimeOffset.UtcNow;
        return new RemoteRepositoryMatcher<RepositoryEntry>(
            [new("a", "a", now, null, "git@github.com:Owner/Repo.git"), new("b", "b", now), new("c", "c", now, null, "https://gitlab.example/team/tool")],
            entry => entry.RemoteUrl);
    }
}
