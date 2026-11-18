using Trag.Domain.Updates;

namespace Trag.Domain.UnitTests.Updates;

// What the update service answered, taken only as a whole answer: a newer release needs its version and a download on
// Finity Software's own site over HTTPS.
public sealed class UpdateOfferTests
{
    [Fact]
    public void WhenANewerReleaseIsOfferedOnFinitySoftwaresSite_ShouldOfferItsDownload()
    {
        var offer = UpdateOffer.From(true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0/win-x64");
        Assert.Equal((true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0/win-x64"), (offer!.IsAvailable, offer.LatestVersion, offer.Download!.AbsoluteUri));
    }

    [Fact]
    public void WhenNoNewerReleaseIsOut_ShouldOfferNoDownload()
    {
        var offer = UpdateOffer.From(false, "1.0.0", null);
        Assert.Equal((false, null), (offer!.IsAvailable, offer.Download));
    }

    [Theory]
    [InlineData(null, "https://finitysoftware.io/download")]
    [InlineData("", "https://finitysoftware.io/download")]
    [InlineData("1.1.0", null)]
    [InlineData("1.1.0", "not a link")]
    [InlineData("1.1.0", "http://finitysoftware.io/download")]
    [InlineData("1.1.0", "https://example.com/download")]
    [InlineData("1.1.0", "https://finitysoftware.io.example.com/download")]
    [InlineData("1.1.0", "https://notfinitysoftware.io/download")]
    public void WhenANewerReleaseComesWithoutItsVersionOrATrustedDownload_ShouldBeNoAnswer(string? latest, string? download) =>
        Assert.Null(UpdateOffer.From(true, latest, download));

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.2.0-beta.1+6a8ecfbd0c2e", "1.2.0-beta.1")]
    public void WhenTheBuildCarriesItsCommit_ShouldBeAskedAboutWithItsVersionAlone(string informational, string version) =>
        Assert.Equal(version, VersionNumber.Of(informational));
}
