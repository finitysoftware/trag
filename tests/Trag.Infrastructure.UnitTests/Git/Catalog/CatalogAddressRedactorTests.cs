using Trag.Infrastructure.Git.Catalog;

namespace Trag.Infrastructure.UnitTests.Git.Catalog;

// The address the picker stores next to a link. It is matched against what the user typed, so it keeps more of the
// address than the status view does; the credentials are what has to go.
public sealed class CatalogAddressRedactorTests
{
    [Fact]
    public void WhenTheAddressCarriesCredentials_ShouldRemoveThemAndKeepTheRestOfTheUrl() =>
        Assert.Equal("https://example.invalid/acme/repo.git", CatalogAddressRedactor.Redact("https://user:secret@example.invalid/acme/repo.git"));

    // The catalog keeps the address in repositories.json and matches it by host and path only, so a query or a fragment
    // is nothing to keep and may carry a token.
    [Fact]
    public void WhenUserinfoExists_ShouldDropQueryAndFragmentToo() =>
        Assert.Equal("https://example.invalid/acme/repo.git", CatalogAddressRedactor.Redact("https://user:secret@example.invalid/acme/repo.git?private_token=leaked#leaked"));

    // It goes through the diagnostics redaction, which leaves an address without user info as it is.
    [Fact]
    public void WhenTheAddressCarriesNoCredentials_ShouldKeepItInEitherForm()
    {
        Assert.Equal("git@example.invalid:acme/repo.git", CatalogAddressRedactor.Redact("git@example.invalid:acme/repo.git"));
        Assert.Equal("https://example.invalid/acme/repo.git", CatalogAddressRedactor.Redact("https://example.invalid/acme/repo.git"));
    }

    // The URL is rewritten from its scheme, server and path, so a query does not survive.
    [Fact]
    public void WhenAnAddressWithoutCredentialsHasAQuery_ShouldDropTheQuery() =>
        Assert.Equal("https://example.invalid/acme/repo.git", CatalogAddressRedactor.Redact("https://example.invalid/acme/repo.git?token=dropped"));

    [Fact]
    public void WhenTheAddressIsEmpty_ShouldShowNothing() => Assert.Null(CatalogAddressRedactor.Redact(""));

    [Fact]
    public void WhenTheAddressIsTooLong_ShouldShowNothing() => Assert.Null(CatalogAddressRedactor.Redact("https://example.invalid/" + new string('x', 2048)));

    [Fact]
    public void WhenTheAddressCarriesAControlCharacter_ShouldShowNothing() => Assert.Null(CatalogAddressRedactor.Redact("https://example.invalid/repo.git\nfetch"));
}
