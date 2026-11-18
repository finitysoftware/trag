using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class BranchRefParserTests
{
    [Fact]
    public void WhenARowHasEmptyFields_ShouldReadAllFiveNulSeparatedFieldsAndKeepTheEmptyOnes()
    {
        var (name, id, upstream, symref, isHead) = BranchRefParser.Parse("refs/heads/main\0" + new string('a', 40) + "\0\0\0 \r");
        Assert.Equal("refs/heads/main", name);
        Assert.Equal(new string('a', 40), id);
        Assert.Equal(string.Empty, upstream);
        Assert.Equal(string.Empty, symref);
        Assert.False(isHead);
    }

    [Fact]
    public void WhenTheBranchIsCheckedOut_ShouldSayItIsHead() =>
        Assert.True(BranchRefParser.Parse("refs/heads/main\0" + new string('a', 40) + "\0\0\0*").IsHead);

    // The row Git 2.55 prints for the checked-out main that tracks origin/main.
    [Fact]
    public void WhenTheBranchHasAnUpstream_ShouldKeepItApartFromTheSymref()
    {
        var (_, _, upstream, symref, _) = BranchRefParser.Parse("refs/heads/main\0" + "3e92c64aada07ff036185d3d1404dffc62e5d1c2" + "\0refs/remotes/origin/main\0\0*");
        Assert.Equal(("refs/remotes/origin/main", ""), (upstream, symref));
    }

    [Fact]
    public void WhenTheRefIsSymbolic_ShouldKeepItsTargetForTheReaderToSkip() =>
        Assert.Equal("refs/remotes/origin/main", BranchRefParser.Parse("refs/remotes/origin/HEAD\0" + new string('b', 40) + "\0\0refs/remotes/origin/main\0 ").Symref);

    [Fact]
    public void WhenTheRowIsShort_ShouldRejectIt() =>
        Assert.Equal("Invalid branch record.", Assert.Throws<FormatException>(() => BranchRefParser.Parse("refs/heads/main\0abc")).Message);
}
