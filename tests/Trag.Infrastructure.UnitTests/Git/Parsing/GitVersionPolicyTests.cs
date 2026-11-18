using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// Reading a version and deciding whether it is usable are two steps; the decision is here.
public sealed class GitVersionPolicyTests
{
    [Theory]
    [InlineData("git version 2.43.0\n", "2.43.0")]
    [InlineData("git version 2.55.0.windows.3", "2.55.0")]
    [InlineData("git version 3.0.0", "3.0.0")]
    public void WhenTheGitVersionIsSupported_ShouldAcceptIt(string input, string expected) => Assert.Equal(expected, GitVersionPolicy.Require(input).ToString());

    [Theory]
    [InlineData("git version 2.42.9", RepositoryError.GitTooOld)]
    [InlineData("not git", RepositoryError.InvalidGitVersion)]
    [InlineData("git version 2.invalid.0", RepositoryError.InvalidGitVersion)]
    [InlineData("git version 2.56.dev", RepositoryError.InvalidGitVersion)]
    public void WhenTheGitVersionIsUnusable_ShouldExplainWhy(string input, RepositoryError expected) =>
        Assert.Equal(expected, Assert.Throws<RepositoryException>(() => GitVersionPolicy.Require(input)).Error);
}
