using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Writes;

public sealed class GitIdentityTests
{
    [Fact]
    public void WhenAnIdentityIsWrittenOut_ShouldReadAsGitWritesIt()
    {
        Assert.Equal("Ana <ana@example.invalid>", new GitIdentity("Ana", "ana@example.invalid").Display);
        Assert.Equal("Ana <ana@example.invalid>", GitIdentity.Format("Ana", "ana@example.invalid"));
    }

    // A typed co-author is read the way Git writes an identity, around the spaces a user leaves.
    [Fact]
    public void WhenATypedIdentityIsReadAsGitWritesIt_ShouldGiveItsNameAndEmail() =>
        Assert.Equal(new GitIdentity("Ana Anić", "ana@example.invalid"), GitIdentity.Parse("  Ana Anić  <ana@example.invalid> "));

    // Git cannot write a name or an email holding a line break or an angle bracket, and an email needs its @.
    [Theory]
    [InlineData("Ana")]
    [InlineData("<ana@example.invalid>")]
    [InlineData("Ana <ana>")]
    [InlineData("Ana <ana@example.invalid")]
    [InlineData("Ana <ana@example.invalid> and more")]
    [InlineData("Ana\n <ana@example.invalid>")]
    [InlineData("\nAna <ana@example.invalid>")]
    [InlineData("A>na <ana@example.invalid>")]
    [InlineData(">Ana <ana@example.invalid>")]
    [InlineData("Ana <a<na@example.invalid>")]
    public void WhenATypedIdentityIsNotOneGitCanWrite_ShouldGiveNone(string text) => Assert.Null(GitIdentity.Parse(text));
}
