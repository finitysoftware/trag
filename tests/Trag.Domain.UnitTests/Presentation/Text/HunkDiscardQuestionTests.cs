using Trag.Domain.Diff;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// The question before one hunk of the working tree is discarded: which file, what the working file loses and gets back,
// and that there is no undo.
public sealed class HunkDiscardQuestionTests
{
    private static readonly PatchLine[] Lines =
    [
        new(' ', " a", 1, 1), new('-', "-b", 2), new('+', "+B", null, 2), new('+', "+C", null, 3), new(' ', " d", 3, 4),
        new('+', "+e", null, 5), new(' ', " f", 4, 6), new('-', "-g", 5)
    ];

    [Fact]
    public void WhenAHunkIsAboutToBeDiscarded_ShouldNameItsFileAndWhatTheWorkingFileLosesAndGetsBack()
    {
        var question = new HunkDiscardQuestion(Snapshots.Modified("src/a b.txt"), Lines, new DiffBlock(1, 3).Lines);

        Assert.Equal("Discard this hunk?", question.Title);
        Assert.Equal("src/a b.txt", question.Subtitle);
        Assert.Equal("The working file loses 2 added lines and gets 1 removed line back; the rest of it and what is staged stay as they are. There is no undo.",
            question.Description);
    }

    [Fact]
    public void WhenSomeLinesOfAHunkAreAboutToBeDiscarded_ShouldAskAboutTheSelectedLines()
    {
        var question = new HunkDiscardQuestion(Snapshots.Modified("a.txt"), Lines, [2]);

        Assert.Equal("Discard the selected lines?", question.Title);
        Assert.Equal("The working file loses 1 added line; the rest of it and what is staged stay as they are. There is no undo.", question.Description);
    }

    [Theory]
    [InlineData(5, "The working file loses 1 added line; the rest of it and what is staged stay as they are. There is no undo.")]
    [InlineData(7, "The working file gets 1 removed line back; the rest of it and what is staged stay as they are. There is no undo.")]
    public void WhenAHunkOnlyAddsOrOnlyRemoves_ShouldSayOnlyThat(int line, string description) =>
        Assert.Equal(description, new HunkDiscardQuestion(Snapshots.Modified("a.txt"), Lines, new DiffBlock(line, line).Lines).Description);
}
