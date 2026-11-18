using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Presentation.Text;

// A sentence that names a commit is given the commit's id and shows it short, the way every view does.
public sealed class CommitIdTextTests
{
    private static readonly string Id = Snapshots.Id('c');

    [Fact]
    public void WhenATextNamesACommit_ShouldShowItsShortId()
    {
        Assert.Equal("Cherry-pick commit ccccccc?", CherryPickText.Title(Id));
        Assert.Equal("Revert commit ccccccc?", RevertText.Title(Id));
        Assert.Equal("a.txt has changes that are not committed. Restoring it from ccccccc replaces them.", FileVersionText.RestoreQuestion("a.txt", Id));
        Assert.Equal("3 commits, from ccccccc to the tip", RewriteText.Subtitle(3, Id));
        Assert.Equal("Tag v1 goes; commit ccccccc stays in the history.", TagText.DeleteQuestion("v1", Id));
        Assert.StartsWith("ccccccc", TagText.RowDetail(Id, "Release"));
    }
}
