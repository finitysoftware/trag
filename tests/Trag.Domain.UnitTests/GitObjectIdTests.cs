using System.Text;

namespace Trag.Domain.UnitTests;

public sealed class GitObjectIdTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567", true)]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567", true)]
    [InlineData("0123456789abcdef0123456789abcdef0123456", false)]
    [InlineData("0123456789abcdef0123456789abcdef012345678", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456g", false)]
    [InlineData("", false)]
    public void WhenAnIdIsChecked_ShouldAcceptExactlyFortyHexCharactersOfEitherCaseAsTextAndAsBytes(string value, bool expected)
    {
        Assert.Equal(expected, GitObjectId.IsValid(value));
        Assert.Equal(expected, GitObjectId.IsValid(Encoding.ASCII.GetBytes(value)));
    }

    [Fact]
    public void WhenAnIdHasTheSha256Length_ShouldAcceptIt() => Assert.True(GitObjectId.IsValid(new string('a', 64)));

    [Fact]
    public void WhenAValidIdIsValidated_ShouldPass() => GitObjectId.Validate(new string('f', 40));

    // Git gets nothing but a full id, because a name could be an option, a path, a ref or a range.
    [Theory]
    [InlineData("HEAD")]
    [InlineData("main")]
    [InlineData("--upload-pack=calc.exe")]
    [InlineData("1111111")]
    public void WhenANameIsValidated_ShouldRefuseIt(string name) => Assert.Throws<FormatException>(() => GitObjectId.Validate(name));

    [Fact]
    public void WhenAnInvalidIdIsValidated_ShouldNameTheProblemWithoutNamingTheValue() =>
        Assert.Equal("Invalid object ID.", Assert.Throws<FormatException>(() => GitObjectId.Validate("head")).Message);

    [Fact]
    public void WhenNoLengthIsGiven_ShouldShortenToSevenCharacters() => Assert.Equal("0123456", GitObjectId.Short("0123456789abcdef"));

    [Fact]
    public void WhenALengthIsGiven_ShouldShortenToThatLength() => Assert.Equal("0123456789", GitObjectId.Short("0123456789abcdef", 10));

    [Fact]
    public void WhenTheIdIsShorterThanTheLength_ShouldKeepTheWholeId() => Assert.Equal("abc", GitObjectId.Short("abc"));
}
