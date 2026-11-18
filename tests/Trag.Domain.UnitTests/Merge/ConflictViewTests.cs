using Trag.Domain.Merge;

namespace Trag.Domain.UnitTests.Merge;

public sealed class ConflictViewTests
{
    [Fact]
    public void WhenAFilesMarkersDoNotClose_ShouldShowNoBlocksAndSayToUseTheEditorOrASide()
    {
        var broken = ConflictView.From(new(string.Join('\n', "a", "<<<<<<< HEAD", "mine", ""), "hash"));
        Assert.Equal((false, "", "The conflict markers in this file are incomplete. Open it in the editor, or use a side."), (broken.Parts.Count != 0, broken.Hash, broken.Message));
    }

    // The file keeps its hash, so marking it resolved writes over the bytes that were read.
    [Fact]
    public void WhenNoBlockIsLeftInAFile_ShouldShowNoBlocksAndSayToMarkItResolved()
    {
        var settled = ConflictView.From(new("a\nb\n", "hash"));
        Assert.Equal((false, "hash", "No conflict block is left in this file. Mark it as resolved when its content is right."), (settled.Parts.Count != 0, settled.Hash, settled.Message));
    }
}
