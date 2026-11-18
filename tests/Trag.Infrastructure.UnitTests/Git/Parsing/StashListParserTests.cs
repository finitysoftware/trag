using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class StashListParserTests
{
    [Fact]
    public void WhenTwoStashesAreListed_ShouldReadEachRowInOrder()
    {
        var first = new string('a', 40); var second = new string('b', 40);
        var output = first + "\0On main: half-done form\0" + "1789414265\n" + second + "\0WIP on trunk: 1a2b3c4 Subject\0" + "1789293600\r\n";
        var entries = StashListParser.Parse(output);
        Assert.Equal(2, entries.Count);
        Assert.Equal(first, entries[0].Id); Assert.Equal(0, entries[0].Index); Assert.Equal("main", entries[0].Branch); Assert.Equal("half-done form", entries[0].Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 21, 31, 5, TimeSpan.FromHours(2)), entries[0].When);
        Assert.Equal(second, entries[1].Id); Assert.Equal(1, entries[1].Index); Assert.Equal("trunk", entries[1].Branch); Assert.Equal("stash@{1}", entries[1].Reference);
    }

    [Fact]
    public void WhenThereIsNoStash_ShouldListNothing() => Assert.Empty(StashListParser.Parse(""));

    [Fact]
    public void WhenAnIdIsNotHex_ShouldRejectTheRecord() => Assert.Throws<FormatException>(() => StashListParser.Parse("not-hex\0msg\0date\n"));

    [Fact]
    public void WhenARecordHasTooFewFields_ShouldRejectIt()
    {
        var first = new string('a', 40);
        Assert.Throws<FormatException>(() => StashListParser.Parse(first + "\0only-two-fields\n"));
    }
}
