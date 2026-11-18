using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

public sealed class RemoteNamesTests
{
    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("-origin")]
    [InlineData("a..b")]
    [InlineData("origin/")]
    [InlineData("origin.lock")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData(".hidden")]
    [InlineData("team/.hidden")]
    [InlineData("a@{b")]
    [InlineData("a//b")]
    [InlineData("origin.")]
    public void WhenARemoteNameBreaksARule_ShouldBeRejectedBeforeGitIsAsked(string name)
    {
        Assert.NotEmpty(RemoteNames.Error(name, []));
        Assert.False(RemoteNames.IsValid(name));
    }

    [Fact]
    public void WhenTheRemoteNameIsTaken_ShouldSayItAlreadyExists() => Assert.Contains("already exists", RemoteNames.Error("origin", ["origin"]));

    // Windows keeps a remote's remote-tracking branches in a folder whose name ignores case, so two such remotes would
    // write over each other's refs.
    [Fact]
    public void WhenTheRemoteNameDiffersFromATakenOneOnlyInCase_ShouldBeTakenAndNameTheTakenOne() =>
        Assert.Equal("A remote named Origin already exists. Choose another name.", RemoteNames.Error("origin", ["Origin"]));

    // A remote's remote-tracking branches live under refs/remotes/<name>/, so a name that is a taken one followed by a slash
    // and more, or the other way around, shares that folder; Git 2.55 refuses such a pair, Git up to 2.50 adds it.
    [Theory]
    [InlineData("origin/b", "origin")]
    [InlineData("origin", "origin/b")]
    [InlineData("Origin/b", "origin")]
    public void WhenTheRemoteNameOverlapsATakenOne_ShouldBeTakenAndNameTheTakenOne(string name, string taken) =>
        Assert.Equal("Remote " + taken + " would share its remote-tracking branches with this name. Choose another name.", RemoteNames.Error(name, [taken]));

    [Fact]
    public void WhenTheRemoteNameOnlyStartsLikeATakenOne_ShouldBeFree() => Assert.Empty(RemoteNames.Error("origin-b", ["origin"]));

    [Fact]
    public void WhenTheRemoteNameIsAtTheLengthLimit_ShouldBreakNoRule() => Assert.Empty(RemoteNames.Error(new('a', GitLimits.NameLength), []));

    [Fact]
    public void WhenTheRemoteNameIsOverTheLengthLimit_ShouldBeRejected() => Assert.NotEmpty(RemoteNames.Error(new('a', GitLimits.NameLength + 1), []));

    [Fact]
    public void WhenTheRemoteNameFollowsEveryRule_ShouldBeValid() => Assert.True(RemoteNames.IsValid("origin"));
}
