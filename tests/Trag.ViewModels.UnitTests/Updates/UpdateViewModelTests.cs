using Trag.Application.Updates;
using Trag.Domain.Updates;
using Trag.TestSupport;
using Trag.ViewModels.Updates;

namespace Trag.ViewModels.UnitTests.Updates;

// Settings -> About asks the update model and shows what it says; Download opens the release's page in the browser.
public sealed class UpdateViewModelTests
{
    private static readonly UpdateOffer Newer = UpdateOffer.From(true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0")!;

    [Fact]
    public async Task WhenTheUserChecks_ShouldShowWhatTheCheckLearned()
    {
        var update = new UpdateViewModel(new UpdateModel(new FakeUpdateChecker { Answer = Newer }, "1.0.0"));
        var changed = new List<string?>();
        update.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await update.CheckCommand.ExecuteAsync(null);

        Assert.Equal(("There is a new update! Version 1.1.0", true, true, false), (update.Status, update.HasStatus, update.OffersDownload, update.CanCheck));
        Assert.Contains(nameof(UpdateViewModel.Status), changed); Assert.Contains(nameof(UpdateViewModel.OffersDownload), changed);
    }

    [Fact]
    public async Task WhenANewerReleaseIsDownloaded_ShouldOpenItsPageInTheBrowser()
    {
        Uri? opened = null;
        var update = new UpdateViewModel(new UpdateModel(new FakeUpdateChecker { Answer = Newer }, "1.0.0")) { OpenBrowser = uri => { opened = uri; return true; } };
        await update.CheckCommand.ExecuteAsync(null);

        update.DownloadCommand.Execute(null);

        Assert.Equal(Newer.Download, opened);
    }
}
