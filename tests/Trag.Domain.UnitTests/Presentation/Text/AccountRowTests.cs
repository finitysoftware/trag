using System.Globalization;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;

namespace Trag.Domain.UnitTests.Presentation.Text;

// The two sentences under an account name in Settings.
public sealed class AccountRowTests
{
    private static AccountRow Row(bool persistent, DateTimeOffset verified) =>
        new(new("id", AccountProvider.GitHub, "42", "octocat", new("https://api.github.com"), new("https://github.com"),
            persistent, verified, null));

    [Fact]
    public void WhenTheAccountIsSaved_ShouldSayItLivesInTheCredentialStore() =>
        Assert.Equal("Saved in your OS credential store", Row(true, DateTimeOffset.UnixEpoch).StorageText);

    [Fact]
    public void WhenTheAccountIsForThisSessionOnly_ShouldSayToSignInAgainAfterClosing() =>
        Assert.Equal("This session only · sign in again after closing Trag", Row(false, DateTimeOffset.UnixEpoch).StorageText);

    [Fact]
    public void WhenTheIdentityWasChecked_ShouldGiveTheLocalTimeAndSayRepositoryAccessIsCheckedOnSync()
    {
        var verified = new DateTimeOffset(2026, 9, 18, 12, 30, 0, TimeSpan.Zero);

        var text = Row(true, verified).IdentityText;

        Assert.Equal("Identity checked " + verified.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + " · repository access is checked when you sync", text);
    }

    [Fact]
    public void WhenACommandActsOnTheRow_ShouldGetTheAccountItself()
    {
        var row = Row(true, DateTimeOffset.UnixEpoch);

        Assert.Equal("id", row.Account.Id);
        Assert.Equal(row.Account.Display, row.Display);
    }
}
