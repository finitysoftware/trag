using System.Text;
using Trag.Infrastructure.Accounts.Credentials;

namespace Trag.Infrastructure.UnitTests.Accounts.Credentials;

// Git's own credentials written into an address are hidden from what Git printed as the broker hides an account's.
public sealed class CredentialRedactorTests
{
    // Git sends the user and password unescaped, and a server echoes them so.
    [Fact]
    public void WhenTheAddressCarriesAnEscapedPassword_ShouldHideItAndItsBasicFormAsGitSendsThem()
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("al ice:p@ss"));

        var redacted = CredentialRedactor.ForAddress("https://al%20ice:p%40ss@example.invalid/team/project.git")!.Redact("echo p@ss and " + basic);

        Assert.Equal("echo [credential] and [credential]", redacted);
    }

    [Theory]
    [InlineData("https://example.invalid/team/project.git")]
    [InlineData("https://alice@example.invalid/team/project.git")]
    [InlineData("https://alice:@example.invalid/team/project.git")]
    public void WhenTheAddressCarriesNoPassword_ShouldHaveNothingToHide(string address) =>
        Assert.Null(CredentialRedactor.ForAddress(address));
}
