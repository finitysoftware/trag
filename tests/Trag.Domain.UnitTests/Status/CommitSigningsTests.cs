using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Status;

// Whether and how Git signs a commit, as its configuration says: commit.gpgSign turns it on (a boolean the configuration
// reads by Git's rule), and gpg.format picks the kind of key, OpenPGP when it is not set.
public sealed class CommitSigningsTests
{
    [Theory]
    [InlineData(null, CommitSigning.OpenPgp)]
    [InlineData("openpgp", CommitSigning.OpenPgp)]
    [InlineData("ssh", CommitSigning.Ssh)]
    [InlineData("x509", CommitSigning.X509)]
    public void WhenTheConfigurationSignsCommits_ShouldNameTheKindOfKey(string? format, CommitSigning expected) =>
        Assert.Equal(expected, CommitSignings.Of(true, format));

    [Fact]
    public void WhenTheConfigurationDoesNotSignCommits_ShouldSaySo() => Assert.Equal(CommitSigning.None, CommitSignings.Of(false, "ssh"));

    // A key may ask for its passphrase, typed at a person's pace: a write that makes signed commits waits as a commit does,
    // with Cancel as the way out, and an unsigned one keeps its limit.
    [Fact]
    public void WhenAWriteMakesSignedCommits_ShouldWaitWithoutALimit()
    {
        Assert.Null(CommitSignings.Limit(CommitSigning.Ssh, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(5), CommitSignings.Limit(CommitSigning.None, TimeSpan.FromMinutes(5)));
    }
}
