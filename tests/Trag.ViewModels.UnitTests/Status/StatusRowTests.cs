using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.History;
using Trag.ViewModels.Status;

namespace Trag.ViewModels.UnitTests.Status;

public sealed class StatusRowTests
{
    [Fact]
    public void WhenAStatusPathHasControlCharacters_ShouldEscapeThem()
    {
        var row = new StatusRow(new FileStatus(Snapshots.Path("a\tb\nc\\d"), '.', 'M'), false);
        Assert.Equal("a\\tb\\nc\\\\d", row.PathText);
    }

    [Fact]
    public void WhenAStatusRowIsShown_ShouldDescribeEveryCode()
    {
        var row = new StatusRow(new FileStatus(Snapshots.Path("a\tb\nc\\d"), '.', 'M'), false);
        Assert.Equal("—", row.OriginalPathText); Assert.Equal("Unstaged · working tree", row.Source);
        Assert.Equal("M", row.Code); Assert.Equal("Modified", row.Description);
        var conflict = new StatusRow(Snapshots.Conflict("c"), false);
        Assert.Equal("!", conflict.Code); Assert.Equal("Unresolved conflict", conflict.Description);
        var renamed = new StatusRow(Snapshots.Renamed("old.txt", "new.txt"), true);
        Assert.Equal("old.txt", renamed.OriginalPathText); Assert.Equal("R", renamed.Code); Assert.Equal("Renamed", renamed.Description); Assert.Equal("Staged · index", renamed.Source);
        foreach (var (code, text) in new[] { ('A', "Added"), ('D', "Deleted"), ('C', "Copied"), ('T', "Type changed"), ('?', "Untracked"), ('!', "Ignored"), ('X', "Changed") })
            Assert.Equal(text, new StatusRow(new FileStatus(Snapshots.Path("f"), code, code), true).Description);
    }

    [Fact]
    public void WhenACommitFileWasRenamed_ShouldShowBothPaths()
    {
        Assert.Equal("old.txt → new.txt", new CommitFileRow(new(Snapshots.Path("new.txt"), "R", "", "", "", "", Snapshots.Path("old.txt")), Snapshots.Id('c')).Description);
    }

    [Fact]
    public void WhenACommitFilePathHasControlCharacters_ShouldEscapeIt()
    {
        Assert.Equal("a\\tb", new CommitFileRow(new(Snapshots.Path("a\tb"), "M", "", "", "", ""), Snapshots.Id('c')).PathText);
    }
}
