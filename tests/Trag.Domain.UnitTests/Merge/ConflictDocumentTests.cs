using Trag.Domain.Merge;

namespace Trag.Domain.UnitTests.Merge;

// Git's conflict markers read back into blocks, one block rewritten as a side or both without touching any other byte,
// and the pieces the merge view shows around each block.
public sealed class ConflictDocumentTests
{
    private const string Csproj = "<Project>\r\n  <PropertyGroup>\r\n<<<<<<< HEAD\r\n    <TargetFramework>net10.0</TargetFramework>\r\n=======\r\n"
        + "    <TargetFramework>net8.0</TargetFramework>\r\n>>>>>>> origin/dotnet8.0\r\n  </PropertyGroup>\r\n</Project>\r\n";

    // A diff3 conflict: the base sits between our side and the separator.
    private const string WithBase = "a\n<<<<<<< ours\nmine\n||||||| base\nold\n=======\ntheirs\n>>>>>>> theirs\nz";

    [Fact]
    public void WhenAConflictHasNoBase_ShouldFindItsBlockBetweenTheMarkers()
    {
        var document = ConflictDocument.Parse(Csproj)!;
        var block = Assert.Single(document.Blocks);
        Assert.Equal(new ConflictBlock(2, -1, 4, 6), block);
    }

    [Fact]
    public void WhenMineIsTaken_ShouldLeaveOnlyMySideAndKeepTheLineEndings()
    {
        var document = ConflictDocument.Parse(Csproj)!;
        Assert.Equal("<Project>\r\n  <PropertyGroup>\r\n    <TargetFramework>net10.0</TargetFramework>\r\n  </PropertyGroup>\r\n</Project>\r\n",
            document.Take(0, ConflictChoice.Mine));
    }

    [Fact]
    public void WhenTheirsIsTaken_ShouldLeaveOnlyTheirSideAndKeepTheLineEndings()
    {
        var document = ConflictDocument.Parse(Csproj)!;
        Assert.Equal("<Project>\r\n  <PropertyGroup>\r\n    <TargetFramework>net8.0</TargetFramework>\r\n  </PropertyGroup>\r\n</Project>\r\n",
            document.Take(0, ConflictChoice.Theirs));
    }

    [Fact]
    public void WhenBothAreTaken_ShouldLeaveMySideThenTheirs()
    {
        var document = ConflictDocument.Parse(Csproj)!;
        Assert.Contains("net10.0</TargetFramework>\r\n    <TargetFramework>net8.0", document.Take(0, ConflictChoice.Both));
    }

    [Fact]
    public void WhenAConflictCarriesItsBase_ShouldFindItsBlockWithTheBase()
    {
        var diff3 = ConflictDocument.Parse(WithBase)!;
        Assert.Equal(new ConflictBlock(1, 3, 5, 7), Assert.Single(diff3.Blocks));
    }

    [Fact]
    public void WhenMineIsTakenFromAConflictWithItsBase_ShouldLeaveTheBaseOut()
    {
        var diff3 = ConflictDocument.Parse(WithBase)!;
        Assert.Equal("a\nmine\nz", diff3.Take(0, ConflictChoice.Mine));
    }

    [Fact]
    public void WhenBothAreTakenFromAConflictWithItsBase_ShouldLeaveTheBaseOut()
    {
        var diff3 = ConflictDocument.Parse(WithBase)!;
        Assert.Equal("a\nmine\ntheirs\nz", diff3.Take(0, ConflictChoice.Both));
    }

    // Git's markers are exactly seven characters at the start of a line; a lone separator or a run of eight is text.
    [Fact]
    public void WhenLinesOnlyLookLikeMarkers_ShouldFindNoBlock()
    {
        Assert.Empty(ConflictDocument.Parse("Title\n=======\n\n>>>>>>>> eight\n<<<<<<<< eight\n")!.Blocks);
    }

    // The file is not Git's any more, so it is left to the editor.
    [Fact]
    public void WhenAConflictNeverCloses_ShouldNotReadTheFile()
    {
        Assert.Null(ConflictDocument.Parse("<<<<<<< HEAD\nmine\n=======\ntheirs\n"));
    }

    [Fact]
    public void WhenAConflictOpensInsideAnother_ShouldNotReadTheFile()
    {
        Assert.Null(ConflictDocument.Parse("<<<<<<< HEAD\n<<<<<<< HEAD\n=======\n>>>>>>> x\n"));
    }

    [Fact]
    public void WhenMarkersCarryNoLabel_ShouldStillFindTheirBlocks()
    {
        Assert.Equal(2, ConflictDocument.Parse("<<<<<<<\na\n=======\nb\n>>>>>>>\n<<<<<<< x\nc\n=======\nd\n>>>>>>> y\n")!.Blocks.Count);
    }

    // conflict-marker-size makes Git write markers of that length (git.git keeps 32 for its AsciiDoc pages). Read with that
    // size, the block is found, and a seven-character underline inside it is the file's own text.
    [Fact]
    public void WhenTheMarkersAreAsLongAsTheFilesAttributeSays_ShouldFindTheBlockAndKeepAShorterUnderlineAsText()
    {
        var (open, separator, close) = (new string('<', 12), new string('=', 12), new string('>', 12));
        var text = $"{open} HEAD\nTitle\n=======\nmine\n{separator}\ntheirs\n{close} feature\nafter\n";

        var document = ConflictDocument.Parse(text, 12)!;

        Assert.Single(document.Blocks);
        Assert.Equal("Title\n=======\nmine\nafter\n", document.Take(0, ConflictChoice.Mine));
        Assert.Equal("theirs\nafter\n", document.Take(0, ConflictChoice.Theirs));
    }

    // A Markdown or AsciiDoc title underline is a line of seven equals signs. Inside a conflict it reads exactly like Git's
    // separator, so the same file can be told two stories: the underline is mine and the second line is the separator, or
    // the first line is the separator and the underline is theirs. Whichever story is true, taking a side must give that
    // side's lines and nothing else; the only answer right for both is to leave the block to the editor.
    [Theory]
    [InlineData("mine", "Title\n=======\nmine body\n")]
    [InlineData("mine", "Title\n")]
    [InlineData("theirs", "theirs body\n")]
    [InlineData("theirs", "mine body\n=======\ntheirs body\n")]
    public void WhenABlockHoldsASecondSeparatorLine_ShouldNeverHandOutTheOtherSidesLines(string side, string expected)
    {
        const string text = "<<<<<<< HEAD\nTitle\n=======\nmine body\n=======\ntheirs body\n>>>>>>> feature\n";

        var document = ConflictDocument.Parse(text);

        if (document is not null)
            Assert.Equal(expected, document.Take(0, side == "mine" ? ConflictChoice.Mine : ConflictChoice.Theirs));
    }

    // diff3 shows the base between my side and the separator; an underline in the base is a second separator as well:
    // either the base ends at the second line and theirs is one line, or it ends at the first and theirs carries the rest.
    [Theory]
    [InlineData("theirs\n")]
    [InlineData("old body\n=======\ntheirs\n")]
    public void WhenTheBaseOfADiff3BlockHoldsAnUnderline_ShouldNeverHandOutBaseLinesAsTheirs(string expected)
    {
        const string text = "<<<<<<< ours\nmine\n||||||| base\nOld title\n=======\nold body\n=======\ntheirs\n>>>>>>> theirs\n";

        var document = ConflictDocument.Parse(text);

        if (document is not null) Assert.Equal(expected, document.Take(0, ConflictChoice.Theirs));
    }

    // A deeply quoted e-mail reply starts with seven '>' and a space, as the closing marker does: either their side holds
    // the quote and the block closes at the last line, or the block closes at the quote and the rest is the file's own text.
    [Theory]
    [InlineData("mine\nafter\n")]
    [InlineData("mine\ntheir reply\n>>>>>>> feature\nafter\n")]
    public void WhenTheirSideHoldsALineLikeTheClosingMarker_ShouldNeverLeaveTheirLinesOrAMarkerInMySide(string expected)
    {
        const string text = "<<<<<<< HEAD\nmine\n=======\n>>>>>>> quoted reply\ntheir reply\n>>>>>>> feature\nafter\n";

        var document = ConflictDocument.Parse(text);

        if (document is not null) Assert.Equal(expected, document.Take(0, ConflictChoice.Mine));
    }

    // Git always writes a label after the base marker (its commit, "base", "parent of …", "Stash base"), so a bare line of
    // seven '|' in my side of a conflict without a base is my own text.
    [Fact]
    public void WhenADiff3BaseMarkerAppearsInsideOursOfANonDiff3File_ShouldNotTruncateOurs()
    {
        var document = ConflictDocument.Parse("x\n<<<<<<< HEAD\nY-main\n|||||||\nmore-main\n=======\nY-feature\n>>>>>>> feature\nz\n")!;

        Assert.Equal("x\nY-main\n|||||||\nmore-main\nz\n", document.Take(0, ConflictChoice.Mine));
    }

    // A line like a labelled base marker in my side of a diff3 conflict tells two stories: the base starts at it and holds
    // the second one, or my side holds it and the base starts at the second. Either way my side must come out whole.
    [Theory]
    [InlineData("mine\nafter\n")]
    [InlineData("mine\n||||||| quoted\nmore mine\nafter\n")]
    public void WhenMySideHoldsALineLikeALabelledBaseMarker_ShouldNeverTruncateMySide(string expected)
    {
        const string text = "<<<<<<< HEAD\nmine\n||||||| quoted\nmore mine\n||||||| 1a2b3c4\nold\n=======\ntheirs\n>>>>>>> feature\nafter\n";

        var document = ConflictDocument.Parse(text);

        if (document is not null) Assert.Equal(expected, document.Take(0, ConflictChoice.Mine));
    }

    // Git splits lines at LF only, so a file whose lines end with CR alone is one line to it: the conflict holds each
    // side's whole file, and Git ends it with an LF before the next marker.
    [Fact]
    public void WhenLineEndingsAreCarriageReturnOnly_ShouldStillFindTheBlocks()
    {
        var document = ConflictDocument.Parse("<<<<<<< HEAD\na\rB-main\rc\r\n=======\na\rB-feature\rc\r\n>>>>>>> feature\n")!;

        Assert.Equal(new ConflictBlock(0, -1, 2, 4), Assert.Single(document.Blocks));
        Assert.Equal("a\rB-feature\rc\r\n", document.Take(0, ConflictChoice.Theirs));
    }

    // A byte order mark and CRLF outside the block are the file's own, and a side taken keeps them.
    [Fact]
    public void WhenTheFileHasABomAndCrLf_ShouldWriteBackTheSameCharactersOutsideTheBlock()
    {
        var document = ConflictDocument.Parse("﻿a\r\n<<<<<<< HEAD\r\nmine\r\n=======\r\ntheirs\r\n>>>>>>> b\r\nz\r\n")!;
        Assert.Equal("﻿a\r\nmine\r\nz\r\n", document.Take(0, ConflictChoice.Mine));
    }

    // Git writes a label after the opening and closing markers; whitespace after them is still a marker line. A
    // separator is the marker alone, so one with spaces after it is text and the block never closes.
    [Fact]
    public void WhenAMarkerLineHasTrailingSpacesOrTabs_ShouldStillFindTheBlockButNotTakeASpacedSeparator()
    {
        var spaced = ConflictDocument.Parse("<<<<<<< HEAD \t\nmine\n=======\ntheirs\n>>>>>>> b\t\n")!;
        Assert.Equal("mine\n", spaced.Take(0, ConflictChoice.Mine));
        Assert.Null(ConflictDocument.Parse("<<<<<<< HEAD\nmine\n======= \ntheirs\n>>>>>>> b\n"));
    }

    [Fact]
    public void WhenTheFileIsEmpty_ShouldFindNoBlock()
    {
        Assert.Empty(ConflictDocument.Parse("")!.Blocks);
    }

    [Fact]
    public void WhenABlockSitsInALongerFile_ShouldShowItWithAFewLinesAroundAndMarkWhatWasLeftOut()
    {
        var lines = Enumerable.Range(1, 10).Select(i => "line " + i + "\n");
        var text = string.Concat(lines.Take(6)) + "<<<<<<< HEAD\nmine\n=======\ntheirs\n>>>>>>> x\n" + string.Concat(lines.Skip(6));
        var parts = ConflictDocument.Parse(text)!.Parts();
        Assert.Equal(3, parts.Count);
        Assert.True(parts[0].GapBefore);
        Assert.Equal([(4, "line 4"), (5, "line 5"), (6, "line 6")], parts[0].Lines.Select(line => (line.Number, line.Text)));
        Assert.True(parts[1].IsBlock);
        Assert.Equal(("Conflict 1 of 1", "mine", "theirs"), (parts[1].Title, parts[1].Mine!.Single().Text, parts[1].Theirs!.Single().Text));
        Assert.Equal(8, parts[1].Mine!.Single().Number);
        Assert.Equal([12, 13, 14], parts[2].Lines.Select(line => line.Number));
        Assert.False(parts[2].GapBefore);
    }
}
