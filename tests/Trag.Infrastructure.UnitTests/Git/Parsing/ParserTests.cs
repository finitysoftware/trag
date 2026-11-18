using System.Text;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

public sealed class ParserTests
{
    [Fact]
    public void WhenARenamedFileIsAlsoChangedInTheWorkingTree_ShouldKeepBothPathsAndShowItStagedAndUnstaged()
    {
        var records = Encoding.UTF8.GetBytes("2 RM N... 100644 100644 100644 abc def R100 new\tname\nč.cs\0old name.cs\0");
        var file = Assert.Single(StatusParser.Parse(records).Files);
        Assert.True(file.IsStaged);
        Assert.True(file.IsUnstaged);
        Assert.Equal("new\tname\nč.cs", file.Path.Display);
        Assert.Equal("old name.cs", file.OriginalPath!.Display);
    }

    // An unmerged record is a conflict, and keeps Git's two letters for which side did what.
    [Theory]
    [InlineData("UU")]
    [InlineData("AA")]
    [InlineData("DU")]
    public void WhenARecordIsUnmerged_ShouldMarkTheConflictWithItsLetters(string letters)
    {
        var record = Encoding.UTF8.GetBytes("u " + letters + " N... 100644 100644 100644 100644 aaa bbb ccc conflict.txt\0");

        var file = Assert.Single(StatusParser.Parse(record).Files);

        Assert.Equal((true, letters[0], letters[1], "conflict.txt"), (file.IsConflict, file.Index, file.Worktree, file.Path.Display));
    }

    // The submodule field says whether the submodule moved and whether it has changes of its own, modified or untracked.
    [Theory]
    [InlineData("SC..", true, false)]
    [InlineData("S.M.", false, true)]
    [InlineData("S..U", false, true)]
    [InlineData("SCMU", true, true)]
    public void WhenASubmoduleFieldSaysMovedModifiedOrUntracked_ShouldSetEachFlag(string field, bool moved, bool ownChanges)
    {
        var record = Encoding.UTF8.GetBytes("1 .M " + field + " 160000 160000 160000 aaa bbb sub\0");

        var file = Assert.Single(StatusParser.Parse(record).Files);

        Assert.Equal((true, moved, ownChanges), (file.IsSubmodule, file.IsSubmoduleMoved, file.SubmoduleHasOwnChanges));
    }

    // The working tree's mode, the last of the record's modes, says whether a file is there: a submodule's entry (160000) and
    // nothing (000000) are not one, whatever the submodule field and the letters say. Records as Git 2.43.0 and 2.55.0 write
    // them for a submodule replaced by a file and back, moved, renamed and in conflict with a file.
    [Theory]
    [InlineData("1 .T S... 160000 160000 100644 aaa aaa sub", false)]
    [InlineData("1 T. S... 160000 100644 100644 aaa bbb sub", false)]
    [InlineData("1 T. S... 100644 160000 160000 aaa bbb sub", true)]
    [InlineData("1 .T SC.. 100644 100644 160000 aaa aaa sub", true)]
    [InlineData("1 .M SC.. 160000 160000 160000 aaa aaa sub", true)]
    [InlineData("2 R. S... 160000 160000 160000 aaa aaa R100 sub2\0sub", true)]
    [InlineData("u UA S... 000000 000000 160000 160000 000 000 ccc p", true)]
    [InlineData("u AU S... 000000 160000 000000 000000 000 bbb 000 p", true)]
    [InlineData("u AU N... 000000 100644 000000 100644 000 bbb 000 p~HEAD", false)]
    public void WhenTheWorkingTreeHoldsASubmoduleOrNothing_ShouldSaySo(string record, bool noFile)
    {
        var file = Assert.Single(StatusParser.Parse(Encoding.UTF8.GetBytes(record + "\0")).Files);

        Assert.Equal(noFile, file.WorkingTreeHasNoFile);
    }

    // A type change and a copy keep Git's letter, and the copy its source; a path git add -N only intended to add is one.
    [Fact]
    public void WhenTheRecordIsATypeChangeACopyOrAnIntentToAdd_ShouldKeepTheLetter()
    {
        var records = Encoding.UTF8.GetBytes("1 .T N... 100644 100644 120000 aaa aaa link\0"
            + "2 C. N... 100644 100644 100644 aaa aaa C100 copy.txt\0source.txt\0"
            + "1 .A N... 000000 000000 100644 0000000000000000000000000000000000000000 0000000000000000000000000000000000000000 new.txt\0");

        var files = StatusParser.Parse(records).Files;

        Assert.Equal('T', files[0].Worktree);
        Assert.Equal(('C', "source.txt"), (files[1].Index, files[1].OriginalPath!.Display));
        Assert.True(files[2].IsIntentToAdd);
    }

    [Fact]
    public void WhenAPathIsNotUtf8_ShouldKeepItsBytes()
    {
        var file = Assert.Single(StatusParser.Parse([(byte)'?', (byte)' ', 0xff, 0xfe, 0]).Files);
        Assert.Equal(new byte[] { 0xff, 0xfe }, file.Path.Bytes);
    }

    // The record Git 2.55 lists for an untracked file named with one character.
    [Fact]
    public void WhenAnUntrackedPathIsOneCharacter_ShouldReadIt() => Assert.Equal("b", Assert.Single(StatusParser.Parse("? b\0"u8.ToArray()).Files).Path.Display);

    [Fact]
    public void WhenARecordIsIncomplete_ShouldRejectIt() => Assert.Throws<FormatException>(() => StatusParser.Parse("? file"u8.ToArray()));

    // A read of a few paths asks for no header; a whole-repository read cannot do without one, and says so.
    [Fact]
    public void WhenNeitherTheBranchNorTheCommitWasReported_ShouldHaveNoHeaderAndSaySoWhenAskedForIt()
    {
        var without = StatusParser.Parse("? file.txt\0"u8.ToArray());
        Assert.False(without.HasHeader);
        Assert.Equal("Missing branch metadata.", Assert.Throws<FormatException>(() => without.Header()).Message);
    }

    [Fact]
    public void WhenNoCommitYetAndNoBranchWereReported_ShouldHaveAHeaderWithNoValues()
    {
        var detached = StatusParser.Parse("# branch.oid (initial)\0# branch.head (detached)\0"u8.ToArray());
        Assert.True(detached.HasHeader);
        Assert.Equal((null, null, null), detached.Header());
    }

    [Fact]
    public void WhenOnlyTheBranchWasReported_ShouldHaveNoHeader()
    {
        var onlyBranch = StatusParser.Parse("# branch.head main\0"u8.ToArray());
        Assert.False(onlyBranch.HasHeader);
    }

    [Fact]
    public void WhenOnlyTheCommitWasReported_ShouldHaveNoHeader()
    {
        var onlyCommit = StatusParser.Parse("# branch.oid (initial)\0"u8.ToArray());
        Assert.False(onlyCommit.HasHeader);
    }
}
