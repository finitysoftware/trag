using Trag.Infrastructure.Platform.Secrets;

namespace Trag.Infrastructure.UnitTests.Platform.Secrets;

// The account id both OS credential stores are asked for. The id ends up in a target name or a lookup attribute, so a
// blank, an over-long or a control-carrying value has no meaning there.
public sealed class SecretIdTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\ncontrol")]
    [InlineData("has\u0000control")]
    public void WhenAnIdCannotNameACredential_ShouldRefuseIt(string id)
    {
        Assert.Equal("Invalid account ID.", Assert.Throws<ArgumentException>(() => SecretId.Validate(id)).Message);
    }

    [Fact]
    public void WhenAnIdIsTwoHundredCharactersLong_ShouldAcceptIt() => SecretId.Validate(new string('a', 200));

    [Fact]
    public void WhenAnIdIsLongerThanTwoHundredCharacters_ShouldRefuseIt() => Assert.Throws<ArgumentException>(() => SecretId.Validate(new string('a', 201)));

    [Fact]
    public void WhenAnIdIsAnOrdinaryAccountId_ShouldAcceptIt() => SecretId.Validate("account-0123456789abcdef");
}
