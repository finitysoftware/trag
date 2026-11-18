using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Domain.UnitTests.Accounts;

// The repository a remote's HTTPS address names under an account's own name on that account's host: the one Trag can
// create from the account when the host does not have it. An organization, a group path or another host names none.
public sealed class AccountRepositoryTests
{
    private static readonly AccountInfo GitHub = FakeAccountService.GitHub(user: "nadia");
    private static readonly AccountInfo SelfHosted = FakeAccountService.GitLab(host: "https://git.example.com/gitlab/");

    [Theory]
    [InlineData("https://github.com/nadia/sample-app")]
    [InlineData("https://github.com/Nadia/sample-app.git")]
    [InlineData("https://github.com/nadia/sample-app/")]
    public void WhenTheAddressNamesARepositoryUnderTheAccountsOwnName_ShouldNameItForThatAccount(string address) =>
        Assert.Equal(new AccountRepository(GitHub, "sample-app"), AccountRepository.At(address, [SelfHosted, GitHub]));

    [Fact]
    public void WhenTheAccountsHostLivesUnderAPath_ShouldReadTheOwnerAfterIt() =>
        Assert.Equal(new AccountRepository(SelfHosted, "trag"), AccountRepository.At("https://git.example.com/gitlab/bob/trag.git", [GitHub, SelfHosted]));

    [Theory]
    [InlineData("https://github.com/acme/sample-app")]
    [InlineData("https://github.com/nadia/team/sample-app")]
    [InlineData("https://github.com/nadia")]
    [InlineData("https://example.com/nadia/sample-app")]
    [InlineData("git@github.com:nadia/sample-app.git")]
    [InlineData("not an address")]
    public void WhenTheAddressNamesNoRepositoryUnderAnAccountsOwnName_ShouldNameNone(string address) =>
        Assert.Null(AccountRepository.At(address, [GitHub, SelfHosted]));
}
