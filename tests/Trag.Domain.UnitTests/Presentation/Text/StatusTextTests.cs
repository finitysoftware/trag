using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class StatusTextTests
{
    [Theory]
    [InlineData('M', "Modified")]
    [InlineData('A', "Added")]
    [InlineData('D', "Deleted")]
    [InlineData('R', "Renamed")]
    [InlineData('C', "Copied")]
    [InlineData('T', "Type changed")]
    [InlineData('?', "Untracked")]
    [InlineData('.', "Unchanged")]
    [InlineData('!', "Ignored")]
    [InlineData('X', "Changed")]
    public void WhenAStatusLetterIsShown_ShouldUseItsWord(char code, string word) => Assert.Equal(word, StatusText.Word(code));

    // A hunk of the working tree's diff goes into the index; one of the index's diff comes back out of it.
    [Theory]
    [InlineData(false, "Stage hunk")]
    [InlineData(true, "Unstage hunk")]
    public void WhenAHunkIsShownOnASide_ShouldNameWhatTakingItDoes(bool fromIndex, string action) => Assert.Equal(action, StatusText.HunkAction(fromIndex));

    [Theory]
    [InlineData(false, "Stage lines")]
    [InlineData(true, "Unstage lines")]
    public void WhenLinesAreChosenOnASide_ShouldNameWhatTakingThemDoes(bool fromIndex, string action) => Assert.Equal(action, StatusText.LinesAction(fromIndex));

    [Fact]
    public void WhenAHunkOfTheWorkingTreeIsShown_ShouldNameItsDiscard()
    {
        Assert.Equal("Discard hunk", StatusText.DiscardHunk);
        Assert.Equal("Discard lines", StatusText.DiscardLines);
    }
}
