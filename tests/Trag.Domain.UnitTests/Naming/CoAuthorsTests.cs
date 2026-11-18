using Trag.Domain.Naming;
using Trag.Domain.Writes;

namespace Trag.Domain.UnitTests.Naming;

// Co-authors live in the message as Co-authored-by trailers, in the trailer block GitHub and GitLab read: the last
// paragraph after the subject, when every line in it is a "Key: value" line.
public sealed class CoAuthorsTests
{
    private static readonly GitIdentity Ana = new("Ana Anić", "ana@example.invalid");
    private static readonly GitIdentity Marko = new("Marko M.", "marko@example.invalid");

    [Theory]
    [InlineData("Fix the parser", "Fix the parser\n\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix: the parser", "Fix: the parser\n\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix\n\nSee: the notes\nand this line.", "Fix\n\nSee: the notes\nand this line.\n\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix\n\nBody.\n\nSigned-off-by: Me <me@example.invalid>", "Fix\n\nBody.\n\nSigned-off-by: Me <me@example.invalid>\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix\r\n\r\nBody.\r\n", "Fix\r\n\r\nBody.\r\n\r\nCo-authored-by: Ana Anić <ana@example.invalid>\r\n")]
    [InlineData("Fix\n\n", "Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\n\n")]
    [InlineData("", "\n\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    // Git itself reads a last paragraph that is only an address as a trailer block, and puts its trailers under it.
    [InlineData("Fix\n\nhttps://example.invalid/issue/1", "Fix\n\nhttps://example.invalid/issue/1\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix\r\n\r\nSigned-off-by: Me <me@example.invalid>\r\n", "Fix\r\n\r\nSigned-off-by: Me <me@example.invalid>\r\nCo-authored-by: Ana Anić <ana@example.invalid>\r\n")]
    public void WhenACoAuthorIsAdded_ShouldEndTheTrailerBlockWithThemOrStartOne(string message, string expected) =>
        Assert.Equal(expected, CoAuthors.With(message, Ana));

    [Theory]
    [InlineData("Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>")]
    [InlineData("Fix\n\nco-authored-by: Ana <ANA@example.invalid>")]
    public void WhenTheCoAuthorIsAlreadyThere_ShouldLeaveTheMessageAsItIs(string message) =>
        Assert.Equal(message, CoAuthors.With(message, Ana));

    // Only the trailer block counts: a line in the body, or a value that is not an identity, names nobody.
    [Fact]
    public void WhenTheCoAuthorsAreRead_ShouldNameThoseInTheTrailerBlockInOrder()
    {
        var message = "Fix\n\nCo-authored-by: Nobody <nobody@example.invalid>\nwritten in the body.\n\n"
            + "Co-authored-by: Marko M. <marko@example.invalid>\nSigned-off-by: Me <me@example.invalid>\nCO-AUTHORED-BY: Ana Anić <ana@example.invalid>\nCo-authored-by: someone";

        Assert.Equal([Marko, Ana], CoAuthors.Of(message));
        Assert.Equal([Marko, Ana], CoAuthors.Of(message.Replace("\n", "\r\n")));
        Assert.Empty(CoAuthors.Of("Co-authored-by: Ana Anić <ana@example.invalid>"));
    }

    // A co-author added before anything was written leaves a message that is nothing but its trailers: Git would take
    // the first trailer for the subject. A subject that merely looks like a trailer ("fix: bug") after a blank line, or
    // trailers the user wrote with no co-author among them, are the user's own and stay.
    [Theory]
    [InlineData("\n\nCo-authored-by: Ana Anić <ana@example.invalid>", true)]
    [InlineData(" \r\n\r\nCo-authored-by: Ana Anić <ana@example.invalid>\r\nSigned-off-by: Me <me@example.invalid>\r\n", true)]
    [InlineData("Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>", false)]
    [InlineData("Signed-off-by: Me <me@example.invalid>", false)]
    [InlineData("\n\nFix", false)]
    [InlineData("\nfix: bug", false)]
    [InlineData("\n\nSigned-off-by: Me <me@example.invalid>", false)]
    public void WhenAMessageIsOnlyItsCoAuthors_ShouldSayItLacksASubject(string message, bool expected) =>
        Assert.Equal(expected, CoAuthors.LacksSubject(message));

    [Theory]
    [InlineData("Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>", "Fix")]
    [InlineData("Fix\r\n\r\nCo-authored-by: Ana <ANA@example.invalid>\r\n", "Fix\r\n")]
    [InlineData("Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\nSigned-off-by: Me <me@example.invalid>", "Fix\n\nSigned-off-by: Me <me@example.invalid>")]
    [InlineData("Fix\n\nCo-authored-by: Marko M. <marko@example.invalid>\nCo-authored-by: Ana Anić <ana@example.invalid>", "Fix\n\nCo-authored-by: Marko M. <marko@example.invalid>")]
    [InlineData("Fix\r\n\r\nCo-authored-by: Ana Anić <ana@example.invalid>\r\nCo-authored-by: Marko M. <marko@example.invalid>", "Fix\r\n\r\nCo-authored-by: Marko M. <marko@example.invalid>")]
    [InlineData("Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\nin the body.", "Fix\n\nCo-authored-by: Ana Anić <ana@example.invalid>\nin the body.")]
    public void WhenACoAuthorIsRemoved_ShouldTakeTheirLineAndAnEmptiedBlockOut(string message, string expected) =>
        Assert.Equal(expected, CoAuthors.Without(message, Ana));
}
