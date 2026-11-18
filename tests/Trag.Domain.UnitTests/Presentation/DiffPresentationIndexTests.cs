using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

public sealed class DiffPresentationIndexTests
{
    private static readonly PatchLine Marker = new('h', "Unchanged lines omitted");
    private static readonly DiffPresentation Document = new(
        [new(' ', " a", 1, 1), Marker, new('-', "-b", 2), new('+', "+b", null, 2), Marker, new(' ', " c", 9, 9)], [], [], [], [], "");

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(3, 4)]
    [InlineData(2, 1)]
    [InlineData(99, 4)]
    public void WhenAnEqualLineRepeats_ShouldFindTheOneNearestToWhereItWas(int near, int expected) => Assert.Equal(expected, Document.IndexOf(Marker, near));

    [Fact]
    public void WhenALineWithTheSameNumbersAndTextIsThere_ShouldFindIt() =>
        Assert.Equal(3, Document.IndexOf(new('+', "+b", null, 2), 0));

    [Fact]
    public void WhenTheLineIsGone_ShouldNotFindIt() =>
        Assert.Equal(-1, Document.IndexOf(new('+', "+b", null, 3), 3));

    [Fact]
    public void WhenNothingIsShown_ShouldFindNoLine() =>
        Assert.Equal(-1, DiffPresentation.Empty.IndexOf(Marker, 0));
}
