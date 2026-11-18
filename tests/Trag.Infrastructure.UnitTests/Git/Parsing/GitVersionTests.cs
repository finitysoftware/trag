using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// Builds append their own mark to the release number (Git for Windows, Apple, VFS for Git, a release candidate); only the
// release number is read. Git built from a snapshot of its source without tags says GIT in place of the patch number,
// read as patch 0 of the release in development.
public sealed class GitVersionTests
{
    [Theory]
    [InlineData("git version 2.43.0.windows.1", "2.43.0")]
    [InlineData("git version 2.39.3 (Apple Git-146)", "2.39.3")]
    [InlineData("git version 2.50.0.vfs.0.0", "2.50.0")]
    [InlineData("git version 2.55.0-rc1", "2.55.0")]
    [InlineData("git version 2.56.GIT", "2.56.0")]
    public void WhenGitReportsAVendorSuffix_ShouldParseIt(string output, string expected) => Assert.Equal(expected, GitVersion.Parse(output).ToString());

    // A program that answers as Git but with a sign in its version is no Git.
    [Theory]
    [InlineData("git version -2.43.0")]
    [InlineData("git version 2.-9.3 (Apple Git-146)")]
    public void WhenAVersionNumberHasASign_ShouldRefuseItAsNoVersion(string output) =>
        Assert.Equal(RepositoryError.InvalidGitVersion, Assert.Throws<RepositoryException>(() => GitVersion.Parse(output)).Error);
}
