using Trag.Domain.Sync;

namespace Trag.Domain.UnitTests.Sync;

// A refspec is read in both directions: from a local branch to its remote-tracking ref and back.
public sealed class RemoteRefspecMappingTests
{
    private static readonly RemoteRefspecMapping Pattern = new("refs/heads/*", "refs/remotes/origin/*");
    private static readonly RemoteRefspecMapping Exact = new("refs/heads/main", "refs/remotes/origin/trunk");

    [Fact]
    public void WhenABranchIsMappedThroughAPattern_ShouldGiveItsRemoteTrackingRef() =>
        Assert.Equal("refs/remotes/origin/feature/topic", Pattern.DestinationFor("refs/heads/feature/topic"));

    [Fact]
    public void WhenARemoteTrackingRefIsMappedBackThroughAPattern_ShouldGiveItsBranch() =>
        Assert.Equal("refs/heads/feature/topic", Pattern.SourceFor("refs/remotes/origin/feature/topic"));

    [Fact]
    public void WhenTheNameUnderAPatternIsEmpty_ShouldStillMap() => Assert.Equal("refs/remotes/origin/", Pattern.DestinationFor("refs/heads/"));

    [Fact]
    public void WhenARefOutsideTheBranchesIsMappedThroughAPattern_ShouldNotMap() => Assert.Null(Pattern.DestinationFor("refs/tags/v1"));

    [Fact]
    public void WhenAnotherRemotesRefIsMappedBackThroughAPattern_ShouldNotMap() => Assert.Null(Pattern.SourceFor("refs/remotes/upstream/main"));

    [Fact]
    public void WhenTheBranchOfARefspecWithoutAStarIsMapped_ShouldGiveItsRemoteTrackingRef() =>
        Assert.Equal("refs/remotes/origin/trunk", Exact.DestinationFor("refs/heads/main"));

    [Fact]
    public void WhenTheRemoteTrackingRefOfARefspecWithoutAStarIsMappedBack_ShouldGiveItsBranch() =>
        Assert.Equal("refs/heads/main", Exact.SourceFor("refs/remotes/origin/trunk"));

    [Fact]
    public void WhenAnotherBranchIsMappedThroughARefspecWithoutAStar_ShouldNotMap() => Assert.Null(Exact.DestinationFor("refs/heads/other"));

    [Fact]
    public void WhenAnotherRemoteTrackingRefIsMappedBackThroughARefspecWithoutAStar_ShouldNotMap() =>
        Assert.Null(Exact.SourceFor("refs/remotes/origin/main"));

    // The star stands for a whole nested name, slashes and all, under a pattern that is itself nested.
    [Fact]
    public void WhenNamesAreNested_ShouldMapTheWholeNameUnderTheStar()
    {
        var feature = new RemoteRefspecMapping("refs/heads/feature/*", "refs/remotes/origin/feature/*");
        Assert.Equal("refs/heads/team/x/y", Pattern.SourceFor("refs/remotes/origin/team/x/y"));
        Assert.Equal("refs/remotes/origin/feature/a/b", feature.DestinationFor("refs/heads/feature/a/b"));
        Assert.Null(feature.DestinationFor("refs/heads/main"));
    }

    [Fact]
    public void WhenTheRefIsShorterThanThePattern_ShouldNotMap() => Assert.Null(Pattern.DestinationFor("refs/head"));
}
