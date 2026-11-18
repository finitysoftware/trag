using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Domain.UnitTests.Presentation;

// Only a diff read whole as text can be cut into hunks: a limited preview, a binary file or a message has no hunk to take.
public sealed class DiffDocumentTests
{
    [Theory]
    [InlineData(DiffContentKind.Text, true)]
    [InlineData(DiffContentKind.Limited, false)]
    [InlineData(DiffContentKind.Binary, false)]
    [InlineData(DiffContentKind.Unavailable, false)]
    public void WhenADiffIsShown_ShouldSayWhetherItIsTheWholeTextOfTheChange(DiffContentKind kind, bool complete)
    {
        var diff = new FileDiff([], [], kind, "");
        Assert.Equal(complete, DiffDocument.From(diff, DiffPresentationBuilder.Build(diff, "a.txt", default)).IsCompleteText);
    }

    // The text an LFS pointer stands for is shown, but the index holds the pointer, so no patch of its lines applies.
    [Fact]
    public void WhenTheTextIsAnLfsFilesContent_ShouldNotBeCutIntoHunks()
    {
        var diff = new FileDiff([], [], DiffContentKind.Text, "") { IsLfsContent = true };
        Assert.False(DiffDocument.From(diff, DiffPresentationBuilder.Build(diff, "a.bin", default)).IsCompleteText);
    }
}
