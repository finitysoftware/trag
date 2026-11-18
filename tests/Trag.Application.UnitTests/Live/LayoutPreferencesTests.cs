using Trag.Application.Live;
using Trag.TestSupport;
using Trag.Domain.Presentation;

namespace Trag.Application.UnitTests.Live;

public sealed class LayoutPreferencesTests
{
    [Fact]
    public async Task WhenTheSavedLayoutArrivesAfterTheUserChangedIt_ShouldNotOverwriteTheUsersLayout()
    {
        FakeLayoutStore store = new() { Preferences = new(0, 0.3) };
        store.LoadGate.IsClosed = true;
        var layout = new LayoutPreferences(store, new StatusBarState());
        var loading = layout.LoadLayoutAsync();
        layout.DiffMode = 1; layout.DiffSplitRatio = 0.6; layout.DiffSplitRatio = 0.7;
        store.LoadGate.Open(); await loading; await layout.SaveCompletion;
        Assert.Equal(new(1, 0.7, 0.36), store.Saved); Assert.Equal(0.7, layout.DiffSplitRatio);
    }

    // A file edited by hand, or written by an older version, is taken as far as it makes sense.
    [Fact]
    public async Task WhenTheSavedLayoutHoldsValuesOutOfRange_ShouldRestoreThemNormalized()
    {
        var layout = new LayoutPreferences(new FakeLayoutStore { Preferences = new(9, 10, -8) }, new StatusBarState());
        await layout.LoadLayoutAsync();
        Assert.Equal((0, 1, 0), (layout.DiffMode, layout.DiffSplitRatio, layout.DiffFilesRatio));
    }

    [Fact]
    public async Task WhenTheSavedLayoutLoads_ShouldRestoreItWithoutWritingIt()
    {
        var (store, layout) = await LoadedAsync();
        Assert.Equal(2, layout.ThemeIndex); Assert.Equal(ThemeChoice.DarkEspresso, layout.Theme); Assert.Equal(0, layout.HistoryPanelRatio); Assert.Equal(1, layout.StagePanelRatio); Assert.Equal(0.7, layout.CommitPanelRatio);
        Assert.Equal(140, layout.CommitMessageHeight);
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task WhenTheUserChangesSeveralParts_ShouldWriteThemTogetherAfterThePause()
    {
        var (store, layout) = await LoadedAsync();
        layout.ThemeIndex = 0; layout.HistoryPanelRatio = 1; layout.StagePanelRatio = 0; layout.CommitPanelRatio = 0.6; layout.CommitMessageHeight = 180;
        await layout.SaveCompletion;
        Assert.Equal(new(1, 0, 1, 0, 1, 0, 0.6, MessageHeight: 180), store.Saved);
    }

    [Fact]
    public async Task WhenASaveFails_ShouldShowANoticeAndKeepTheLayoutForTheSession()
    {
        var bar = new StatusBarState();
        FakeLayoutStore store = new() { SaveFailure = new IOException("disk") };
        var layout = new LayoutPreferences(store, bar);
        layout.DiffMode = 1;
        await layout.SaveCompletion;
        Assert.Contains("could not be saved", bar.Notice); Assert.Equal(1, layout.DiffMode);
    }

    // A layout file that cannot be read keeps the defaults for the session, says so, and is not written over by them.
    [Fact]
    public async Task WhenTheSavedLayoutIsCorrupt_ShouldShowNotLoadedAndKeepDefaults()
    {
        var bar = new StatusBarState();
        FakeLayoutStore store = new() { LoadFailure = new System.Text.Json.JsonException("corrupt") };
        var layout = new LayoutPreferences(store, bar);

        await layout.LoadLayoutAsync();

        Assert.Equal(Trag.Domain.Presentation.Text.LayoutText.NotLoaded, bar.Notice);
        Assert.Equal((0, 0.5, 0), (layout.DiffMode, layout.DiffSplitRatio, layout.ThemeIndex));
        Assert.Equal(0, store.Saves);
    }

    // A layout with a saved theme and panel ratios, loaded.
    private static async Task<(FakeLayoutStore Store, LayoutPreferences Layout)> LoadedAsync()
    {
        FakeLayoutStore store = new() { Preferences = new(1, 0, 1, 2, 0, 1, 0.7, MessageHeight: 140) };
        var layout = new LayoutPreferences(store, new StatusBarState());
        await layout.LoadLayoutAsync();
        return (store, layout);
    }
}
