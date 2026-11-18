using Trag.Domain.Accounts;

namespace Trag.Domain.UnitTests.Accounts;

// Which remote addresses are SSH, as Git reads them: ssh:// and the scp-like [user@]host:path, whose colon comes before
// any slash, with or without the user the snapshot hides; a Windows path with a drive letter and a local path are not.
public sealed class RemoteAddressTests
{
    [Theory]
    [InlineData("ssh://git@github.com/finity/trag.git", true)]
    [InlineData("git@github.com:finity/trag.git", true)]
    [InlineData("github.com:finity/trag.git", true)]
    [InlineData("https://github.com/finity/trag.git", false)]
    [InlineData(@"C:\Users\a@b\trag", false)]
    [InlineData("C:/Users/a@b/trag", false)]
    [InlineData("/home/a@b:c/trag", false)]
    public void WhenAnAddressIsRead_ShouldTellWhetherGitReachesItOverSsh(string address, bool ssh) =>
        Assert.Equal(ssh, RemoteAddress.IsSsh(address));
}
