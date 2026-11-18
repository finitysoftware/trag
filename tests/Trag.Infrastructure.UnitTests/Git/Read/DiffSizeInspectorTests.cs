using System.Text;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;

namespace Trag.Infrastructure.UnitTests.Git.Read;

// What cat-file --batch-check answers decides whether a diff is read. The answers are Git 2.43.0's, which does not know
// GIT_NO_LAZY_FETCH: it asks the promisor remote for a version a partial clone did not download, the empty
// GIT_ALLOW_PROTOCOL refuses, and it stops after the sizes it already wrote.
public sealed class DiffSizeInspectorTests
{
    private static GitResult Answer(int exitCode, string output, string error) => new(exitCode, Encoding.ASCII.GetBytes(output), Encoding.ASCII.GetBytes(error));

    [Fact]
    public void WhenGitStopsAtAVersionThePromisorRemoteCouldNotSend_ShouldSayItWasNotDownloaded() =>
        Assert.Equal(DiffSize.NotDownloaded, DiffSizeInspector.Size(Answer(128, "8\n",
            "fatal: transport 'file' not allowed\nfatal: could not fetch 5626abf0f72e58d7a153368ba57db4c673c0e171 from promisor remote\n")));

    [Fact]
    public void WhenGitFailsForAnotherReason_ShouldFailWithItsExitCode() =>
        Assert.Equal(128, Assert.Throws<GitCommandException>(() => DiffSizeInspector.Size(Answer(128, "",
            "fatal: not a git repository (or any of the parent directories): .git\n"))).ExitCode);
}
