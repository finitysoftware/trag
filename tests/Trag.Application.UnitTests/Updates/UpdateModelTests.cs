using Trag.Application.Updates;
using Trag.Domain.Updates;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Updates;

// The update check the user asks for on the About page: what it says while it asks and once it knows, when it can be
// asked again, and when it offers the download.
public sealed class UpdateModelTests
{
    private static readonly UpdateOffer Newer = UpdateOffer.From(true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0")!;

    [Fact]
    public void WhenNothingWasCheckedYet_ShouldSayNothingAndOfferTheCheck()
    {
        var model = new UpdateModel(new FakeUpdateChecker(), "1.0.0");
        Assert.Equal(("", false, true, false), (model.Status, model.HasStatus, model.CanCheck, model.OffersDownload));
    }

    [Fact]
    public async Task WhenTheCheckRuns_ShouldSaySoAndNotAskASecondTime()
    {
        var checker = new FakeUpdateChecker(); checker.Gate.IsClosed = true;
        var model = new UpdateModel(checker, "1.0.0");
        var checking = model.CheckAsync();

        await model.CheckAsync();

        Assert.Equal(("Checking for updates…", true, false), (model.Status, model.HasStatus, model.CanCheck));
        Assert.Single(checker.Versions);
        checker.Gate.Open(); await checking;
    }

    [Fact]
    public async Task WhenANewerReleaseIsOut_ShouldSaySoAndOfferItsDownloadInPlaceOfTheCheck()
    {
        var model = new UpdateModel(new FakeUpdateChecker { Answer = Newer }, "1.0.0");

        await model.CheckAsync();

        Assert.Equal(("There is a new update! Version 1.1.0", true, false), (model.Status, model.OffersDownload, model.CanCheck));
        Assert.Equal(Newer.Download, model.Offer!.Download);
    }

    [Fact]
    public async Task WhenTragIsTheNewestRelease_ShouldSaySoAndOfferTheCheckAgain()
    {
        var model = new UpdateModel(new FakeUpdateChecker { Answer = UpdateOffer.From(false, "1.0.0", null) }, "1.0.0");

        await model.CheckAsync();

        Assert.Equal(("Trag is up to date.", false, true), (model.Status, model.OffersDownload, model.CanCheck));
    }

    // No service to ask yet, no network, or an answer that is not one: the user can try again.
    [Fact]
    public async Task WhenTheServiceCouldNotBeAsked_ShouldSaySoAndOfferTheCheckAgain()
    {
        var checker = new FakeUpdateChecker();
        var model = new UpdateModel(checker, "1.0.0");

        await model.CheckAsync();

        Assert.Equal(("Could not check for updates.", false, true), (model.Status, model.OffersDownload, model.CanCheck));
        checker.Answer = Newer;
        await model.CheckAsync();
        Assert.Equal(2, checker.Versions.Count); Assert.True(model.OffersDownload);
    }

    [Fact]
    public async Task WhenTheBuildCarriesItsCommit_ShouldAskWithTheVersionAlone()
    {
        var checker = new FakeUpdateChecker();

        await new UpdateModel(checker, "1.0.0+6a8ecfbd0c2e").CheckAsync();

        Assert.Equal(["1.0.0"], checker.Versions);
    }
}
