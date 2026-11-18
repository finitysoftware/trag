using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

public sealed class LfsTextTests
{
    // A Git LFS file shows the size of each side it has, and says when the content of the side shown is not here.
    [Fact]
    public void WhenAnLfsFileIsShown_ShouldGiveItsSizesAndWhetherItsContentIsHere()
    {
        Assert.Equal("Git LFS file · 8 bytes → 1.5 KB", LfsText.File(8, 1536, downloaded: true));
        Assert.Equal("Git LFS file · 2.5 MB. Its content is not downloaded.", LfsText.File(null, 2621440, downloaded: false));
        Assert.Equal("Git LFS file · 1 byte", LfsText.File(1, null, downloaded: true));
        Assert.Equal("Git LFS file · 3 GB", LfsText.File(null, 3L * 1024 * 1024 * 1024, downloaded: true));
    }

    // Through a Git LFS extension a pointer records the size of what the extension stored, not the file's, so no size is given.
    [Fact]
    public void WhenNoSizeOfTheFileIsKnown_ShouldNameItAnLfsFileAlone()
    {
        Assert.Equal("Git LFS file", LfsText.File(null, null, downloaded: true));
        Assert.Equal("Git LFS file. Its content is not downloaded.", LfsText.File(null, null, downloaded: false));
    }

    // Trag knows Git LFS only by its filter in Git's configuration, which git lfs install writes; Git LFS installed
    // without that filter needs git lfs install, so every text that refuses for want of it names that command.
    [Fact]
    public void WhenGitLfsIsNotSetUpForGit_ShouldSayToRunGitLfsInstall()
    {
        Assert.All(new[] { LimitationText.LfsNotInstalled, IndexOutcomeText.LfsNotInstalled, MergeOutcomeText.IncomingLfs("pull") },
            text => Assert.Contains("git lfs install", text, StringComparison.Ordinal));
    }
}
