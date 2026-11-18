using Trag.Domain.Naming;

namespace Trag.Domain.UnitTests.Naming;

// A message Git commits as an edited one loses every line that starts with its comment character; the one chosen is the
// first of Git's own candidates no line of the message starts with.
public sealed class MessageLinesTests
{
    [Theory]
    [InlineData("Fix the build", "#")]
    [InlineData("#12 Fix\n\n;13 body\n@14", "!")]
    [InlineData("Fix\n#12", ";")]
    public void WhenGitCommitsTheMessageAsEdited_ShouldMarkItsOwnLinesWithACharacterNoLineStartsWith(string message, string comment) =>
        Assert.Equal(comment, MessageLines.CommentChar(message));
}
