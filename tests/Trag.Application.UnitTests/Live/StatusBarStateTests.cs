using Trag.Application.Live;

namespace Trag.Application.UnitTests.Live;

public sealed class StatusBarStateTests
{
    [Fact]
    public void WhenNoBannerHasText_ShouldRaiseNoBannerFlag()
    {
        var bar = new StatusBarState();
        Assert.False(bar.HasError); Assert.False(bar.HasNotice); Assert.False(bar.HasWarning);
    }

    [Fact]
    public void WhenEveryBannerHasText_ShouldRaiseEveryBannerFlag()
    {
        var bar = new StatusBarState();
        bar.ErrorMessage = "no"; bar.Notice = "note"; bar.RepositoryWarning = "warn";
        Assert.True(bar.HasError); Assert.True(bar.HasNotice); Assert.True(bar.HasWarning);
    }

    [Fact]
    public void WhenOnlyASyncLeftDetails_ShouldShowThemAndAnnounceTheChange()
    {
        var bar = new StatusBarState();
        var notified = new List<string?>();
        bar.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
        bar.SyncDetails = "sync output";
        Assert.Equal("sync output", bar.OperationDetails); Assert.True(bar.HasOperationDetails);
        Assert.Contains(nameof(StatusBarState.OperationDetails), notified); Assert.Contains(nameof(StatusBarState.HasOperationDetails), notified);
    }

    [Fact]
    public void WhenAWriteLeftDetailsToo_ShouldShowTheWrites()
    {
        var bar = new StatusBarState();
        bar.SyncDetails = "sync output";
        bar.WriteDetails = "write output";
        Assert.Equal("write output", bar.OperationDetails);
    }
}
