using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Shell;

// The status bar's Details: the user opens it over the last operation's git output, and it folds away once none is
// left; the output of an earlier operation shows again while the later one's is cleared.
public sealed class StatusDetailsTests
{
    [Fact]
    public async Task WhenTheWritesDetailsAreCleared_ShouldShowTheSyncsAgainStillExpanded()
    {
        await using var model = Expanded();
        model.StatusBar.WriteDetails = "";
        Assert.Equal("sync output", model.StatusBar.OperationDetails); Assert.True(model.IsSyncDetailsExpanded);
    }

    [Fact]
    public async Task WhenNoDetailsAreLeft_ShouldCollapse()
    {
        await using var model = Expanded();
        model.StatusBar.WriteDetails = "";
        model.StatusBar.SyncDetails = "";
        Assert.False(model.StatusBar.HasOperationDetails); Assert.False(model.IsSyncDetailsExpanded);
    }

    // A sync and then a write left details, and the user expanded them.
    private static ShellViewModel Expanded()
    {
        var model = new ShellHarness().Shell();
        model.StatusBar.SyncDetails = "sync output";
        model.StatusBar.WriteDetails = "write output";
        model.IsSyncDetailsExpanded = true;
        return model;
    }
}
