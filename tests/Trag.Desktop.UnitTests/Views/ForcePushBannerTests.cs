using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Controls;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The banner above the file status for a branch rewritten after it was pushed: what it says, the question that replaces
// it once asked, and the force push its answer sends; meanwhile the toolbar's Pull is off and its tooltip says why.
public sealed class ForcePushBannerTests
{
    [AvaloniaFact]
    public async Task WhenTheBranchWasRewrittenAfterItWasPushed_ShouldTurnPullOffAndAskBeforeTheRemoteBranchIsReplaced()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = h.Synced();
        h.Sync.State = (snapshot, remote, branch) => FakeGitSync.DefaultState(snapshot, remote, branch) with { Ahead = 2, Behind = 1, Rewritten = true, TrackingHead = Snapshots.Id('e') };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Border>("ForcePushNotice")!.IsEffectivelyVisible);
        Assert.Equal("main no longer matches origin/main", window.Descendant<TextBlock>("ForcePushTitle")!.Text);
        Assert.False(window.Descendant<TextBlock>("ForcePushQuestion")!.IsEffectivelyVisible);
        var pull = window.Descendant<ToolButton>("PullButton")!;
        Assert.False(pull.IsEffectivelyEnabled); Assert.False(pull.Pending);
        Assert.StartsWith("Pull is off: this branch was rewritten after it was pushed", Assert.IsType<string>(ToolTip.GetTip(pull)));
        Assert.True(ToolTip.GetShowOnDisabled(pull));

        window.Descendant<Button>("RequestForcePushButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<TextBlock>("ForcePushQuestion")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("RequestForcePushButton")!.IsEffectivelyVisible);
        var confirm = window.Descendant<Button>("ConfirmForcePushButton")!;
        Assert.True(confirm.IsEffectivelyVisible); Assert.True(confirm.IsEffectivelyEnabled);

        confirm.Command!.Execute(null);
        await AppHarness.Until(() => h.Writer.Requests.Count == 1 && !model.Repository.IsWriting, "the force push");
        Assert.Equal(SyncAction.ForcePush, Assert.IsType<SyncRequest>(h.Writer.Requests[0]).Action);
        window.Close();
    }
}
