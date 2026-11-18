using System.Text;
using Trag.Domain.Naming;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Writes;

// A commit message is offered to Git only within 64 KB of UTF-8, which the editor's limit in characters does not
// guarantee; a message written that Git will not be offered says why under it.
public sealed class CommitRequestTests
{
    // 40 000 Cyrillic letters are far under the editor's 65 536 characters, and twice the bytes Git is offered.
    private static readonly string Cyrillic = new('ж', 40_000);

    [Fact]
    public void WhenTheMessageIsUnderTheCharacterLimitButOverSixtyFourKilobytes_ShouldBeInvalid()
    {
        Assert.True(Cyrillic.Length < 65_536);
        Assert.True(Encoding.UTF8.GetByteCount(Cyrillic) > GitLimits.CommitMessageBytes);
        Assert.False(CommitRequest.IsValidMessage(Cyrillic));
    }

    [Fact]
    public void WhenTheMessageIsExactlySixtyFourKilobytes_ShouldBeValid() =>
        Assert.True(CommitRequest.IsValidMessage(new string('a', GitLimits.CommitMessageBytes)));

    // 65 535 one-byte letters and one of two bytes: under the limit counted in characters, one byte over it in UTF-8.
    [Fact]
    public void WhenTheMessageIsOneByteOverSixtyFourKilobytes_ShouldBeInvalid() =>
        Assert.False(CommitRequest.IsValidMessage(new string('a', GitLimits.CommitMessageBytes - 1) + "ж"));

    // The limit counts the message as the editor holds it, Windows' line breaks included, though Git is offered line
    // feeds alone.
    [Fact]
    public void WhenTheMessageFitsOnlyWithoutItsCarriageReturns_ShouldBeInvalid()
    {
        var message = "Subject" + string.Concat(Enumerable.Repeat("\r\na", 21_844));
        Assert.True(Encoding.UTF8.GetByteCount(MessageLines.ForGit(message)) <= GitLimits.CommitMessageBytes);
        Assert.False(CommitRequest.IsValidMessage(message));
    }

    // Git is told to keep the message verbatim: a carriage return of its own, spaces at the end of a line and no line
    // break at the end reach it as written.
    [Fact]
    public void WhenTheMessageHasALoneCarriageReturnTrailingSpacesAndNoFinalLineBreak_ShouldBeOfferedAsWritten()
    {
        const string message = "Subject  \rstill the subject\n\nBody  ";
        Assert.True(CommitRequest.IsValidMessage(message));
        Assert.Equal(message, MessageLines.ForGit(message));
    }

    [Fact]
    public void WhenAWrittenMessageIsNotOneGitIsOffered_ShouldSayWhy()
    {
        Assert.Equal(WriteText.CommitMessageInvalid, CommitRequest.MessageHint(Cyrillic));
        Assert.Equal(WriteText.SubjectMissing, CommitRequest.MessageHint("\n\nCo-authored-by: Ana Anić <ana@example.invalid>"));
        Assert.Equal("", CommitRequest.MessageHint("Fix the parser"));
        Assert.Equal("", CommitRequest.MessageHint(""));
    }
}
