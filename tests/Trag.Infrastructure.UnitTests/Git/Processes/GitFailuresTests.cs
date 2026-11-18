using System.ComponentModel;
using Trag.Domain.Errors;
using Trag.Infrastructure.Git.Processes;

namespace Trag.Infrastructure.UnitTests.Git.Processes;

// The one list of failures a write, a synchronization or a clone answers with a sentence: the file system, a path or an
// argument, output Trag cannot read, the repository, the process and Git itself. Anything else is a defect and escapes.
public sealed class GitFailuresTests
{
    public static TheoryData<Exception> Answered => new()
    {
        new IOException(), new UnauthorizedAccessException(), new ArgumentException(), new NotSupportedException(), new FormatException(),
        new RepositoryException(RepositoryError.ReadFailed), new GitCommandException(1), new GitOutputLimitException(), new Win32Exception()
    };

    [Theory]
    [MemberData(nameof(Answered))]
    public void WhenGitTheFileSystemOrTheOutputFails_ShouldBeAnsweredWithASentence(Exception error) => Assert.True(GitFailures.IsExpected(error));

    [Fact]
    public void WhenTragItselfFails_ShouldLetTheDefectEscape() => Assert.False(GitFailures.IsExpected(new InvalidOperationException()));
}
