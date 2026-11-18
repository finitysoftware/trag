using Trag.Domain.Presentation.Text;
using Trag.Domain.Sync;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the Fetch button says about fetching on its own, and the intervals the user chooses from.
public sealed class AutoFetchTextTests
{
    [Fact]
    public void WhenTheFetchRunsOnItsOwn_ShouldSayHowOftenOnTheFetchButton()
    {
        Assert.Equal("Fetch (Ctrl+Shift+F) · Trag also fetches every minute", AutoFetchText.FetchTip(AutoFetchInterval.OneMinute, false));
    }

    [Fact]
    public void WhenTheFetchIsOff_ShouldShowOnlyTheShortcutOnTheFetchButton()
    {
        Assert.Equal("Fetch (Ctrl+Shift+F)", AutoFetchText.FetchTip(AutoFetchInterval.Off, false));
    }

    [Fact]
    public void WhenTheFetchIsStopped_ShouldSayOnTheFetchButtonThatAFetchResumesIt()
    {
        Assert.EndsWith("Fetch resumes it", AutoFetchText.FetchTip(AutoFetchInterval.FiveMinutes, true));
    }

    [Fact]
    public void WhenTheUserChoosesAnInterval_ShouldOfferOffAndThreeIntervals()
    {
        Assert.Equal(["Off", "Every minute", "Every 5 minutes", "Every 15 minutes"], AutoFetchText.Options.Select(option => option.Label));
    }
}
