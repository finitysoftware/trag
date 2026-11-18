using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class RemoteRefParserTests
{
    [Fact]
    public void WhenARowHasAnEmptySymref_ShouldReadItsThreeNulSeparatedFieldsAsABranch()
    {
        var (name, objectName, isSymref) = RemoteRefParser.Parse("refs/remotes/origin/main\0" + new string('a', 40) + "\0\r");
        Assert.Equal("refs/remotes/origin/main", name);
        Assert.Equal(new string('a', 40), objectName);
        Assert.False(isSymref);
    }

    [Fact]
    public void WhenARowHasASymrefTarget_ShouldReadItAsAnAlias() =>
        Assert.True(RemoteRefParser.Parse("refs/remotes/origin/HEAD\0" + new string('b', 40) + "\0refs/remotes/origin/main").IsSymref);

    [Fact]
    public void WhenTheRowIsShort_ShouldRejectIt() =>
        Assert.Equal("Invalid remote ref.", Assert.Throws<FormatException>(() => RemoteRefParser.Parse("refs/remotes/origin/main\0abc")).Message);
}
