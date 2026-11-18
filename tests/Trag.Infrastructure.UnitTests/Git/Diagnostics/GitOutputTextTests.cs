using System.Text;
using Trag.Infrastructure.Git.Diagnostics;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.UnitTests.Git.Diagnostics;

// Every writer and every transfer shows Git's output through this one pair of methods, so the order of the two streams
// and the redaction between them are asserted here instead of once per caller.
public sealed class GitOutputTextTests
{
    private static GitResult Result(string standardOutput, string standardError) =>
        new(1, Encoding.UTF8.GetBytes(standardOutput), Encoding.UTF8.GetBytes(standardError));

    // Standard error is where Git explains itself.
    [Fact]
    public void WhenGitWritesToBothStreams_ShouldShowStandardErrorFirst() =>
        Assert.Equal("the reason\nthe listing", GitOutputText.Raw(Result("the listing", "the reason")));

    [Fact]
    public void WhenBothStreamsAreEmpty_ShouldStillSeparateThemWithANewline() => Assert.Equal("\n", GitOutputText.Raw(Result("", "")));

    [Fact]
    public void WhenTheOutputCarriesCredentials_ShouldShowItOnlyThroughTheSharedRedaction()
    {
        var details = GitOutputText.Details(Result("done", "fatal: https://user:password@example.invalid/repo"));

        Assert.DoesNotContain("password", details);
        Assert.Contains("fatal:", details);
        Assert.Contains("done", details);
        Assert.Equal(GitOutputRedaction.Redact(GitOutputText.Raw(Result("done", "fatal: https://user:password@example.invalid/repo"))), details);
    }

    // A verification runs whether or not the command ever returned.
    [Fact]
    public void WhenTheCommandNeverReturned_ShouldHaveNoDetails() => Assert.Equal("", GitOutputText.Details(null));
}
