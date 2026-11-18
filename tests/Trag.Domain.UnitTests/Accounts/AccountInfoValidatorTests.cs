using Trag.Domain.Accounts;

namespace Trag.Domain.UnitTests.Accounts;

// What an account entry on disk has to be before Trag uses it. Every clause here is what stops an edited settings
// file from pointing a token at a different host.
public sealed class AccountInfoValidatorTests
{
    private static readonly Uri GitLabApi = AccountMapping.GitLabApiBase("https://gitlab.example");

    private static AccountInfo GitLab(Uri? api = null, Uri? origin = null, string userId = "1", string name = "alice", bool persistent = true, string? id = null) =>
        new(id ?? AccountIdentity.StableId(AccountProvider.GitLab, api ?? GitLabApi, userId), AccountProvider.GitLab, userId,
            name, api ?? GitLabApi, origin ?? new Uri(GitLabApi, "../../"), persistent, DateTimeOffset.UnixEpoch, null);

    private static AccountInfo GitHub(string api = "https://api.github.com/", string origin = "https://github.com/") =>
        new(AccountIdentity.StableId(AccountProvider.GitHub, new(api), "7"), AccountProvider.GitHub, "7", "alice",
            new(api), new(origin), true, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void WhenAGitLabEntryMatchesItsOwnAddresses_ShouldBeValid() => Assert.True(AccountInfoValidator.Valid(GitLab()));

    [Fact]
    public void WhenAGitHubEntryMatchesItsOwnAddresses_ShouldBeValid() => Assert.True(AccountInfoValidator.Valid(GitHub()));

    // A session-only account is never on disk.
    [Fact]
    public void WhenTheEntryIsSessionOnly_ShouldBeRefused() => Assert.False(AccountInfoValidator.Valid(GitLab(persistent: false)));

    [Fact]
    public void WhenTheIdIsMadeUp_ShouldBeRefused() => Assert.False(AccountInfoValidator.Valid(GitLab(id: "account-0000")));

    // The id follows from the provider, the API address and the user id: changing any one of them invalidates it.
    [Fact]
    public void WhenTheIdBelongsToAnotherProvider_ShouldBeRefused() =>
        Assert.False(AccountInfoValidator.Valid(GitLab(id: AccountIdentity.StableId(AccountProvider.GitHub, GitLabApi, "1"))));

    [Fact]
    public void WhenTheIdBelongsToAnotherUser_ShouldBeRefused() =>
        Assert.False(AccountInfoValidator.Valid(GitLab(id: AccountIdentity.StableId(AccountProvider.GitLab, GitLabApi, "2"))));

    [Theory]
    [InlineData("http://gitlab.example/api/v4/")]
    [InlineData("https://user:secret@gitlab.example/api/v4/")]
    public void WhenTheApiAddressIsNotCleanHttps_ShouldBeRefused(string api) => Assert.False(AccountInfoValidator.Valid(GitLab(api: new(api))));

    [Fact]
    public void WhenAGitLabApiAddressDoesNotFollowFromItsOrigin_ShouldBeRefused() =>
        Assert.False(AccountInfoValidator.Valid(GitLab(origin: new("https://elsewhere.example/"))));

    [Theory]
    [InlineData("https://api.github.com.evil.invalid/", "https://github.com/")]
    [InlineData("https://api.github.com/", "https://github.com.evil.invalid/")]
    public void WhenAGitHubEntryPointsAnywhereButTheRealGitHub_ShouldBeRefused(string api, string origin) => Assert.False(AccountInfoValidator.Valid(GitHub(api, origin)));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has\u0001control")]
    public void WhenTheNameIsBlankOrCarriesAControlCharacter_ShouldBeRefused(string name) => Assert.False(AccountInfoValidator.Valid(GitLab(name: name)));

    [Fact]
    public void WhenTheNameHasTwoHundredCharacters_ShouldBeValid() => Assert.True(AccountInfoValidator.Valid(GitLab(name: new string('a', 200))));

    [Fact]
    public void WhenTheNameHasMoreThanTwoHundredCharacters_ShouldBeRefused() => Assert.False(AccountInfoValidator.Valid(GitLab(name: new string('a', 201))));

    [Fact]
    public void WhenTheProviderDoesNotExist_ShouldBeRefused() =>
        Assert.False(AccountInfoValidator.Valid(GitLab() with { Provider = (AccountProvider)99 }));
}
