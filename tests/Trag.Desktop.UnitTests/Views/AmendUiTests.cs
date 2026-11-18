using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Trag.Domain.History;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The amend box beside the Commit button: checked, the editor holds the last commit's message, the button says what it
// does now, and a commit the upstream has already comes with its warning; unchecked, the draft and the button are back.
public sealed class AmendUiTests
{
    [AvaloniaFact]
    public async Task WhenTheAmendBoxIsClicked_ShouldTurnTheCommitAreaIntoAnAmendOfTheLastCommitAndBack()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = h.Synced();
        h.History.Details = id => new CommitDetails(Snapshots.Commit(id, "Last subject"), "Last subject\n\nIts body", []);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        var box = window.Descendant<CheckBox>("AmendBox")!;
        var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        var button = window.Descendant<Button>("CommitButton")!;
        var warning = window.Descendant<TextBlock>("AmendWarning")!;
        editor.Text = "Draft";
        Assert.True(box.IsEffectivelyEnabled); Assert.Equal("Amend last commit", box.Content);
        Assert.Equal(("Commit staged", "Commit staged changes (Ctrl+Enter)"), (button.Content, ToolTip.GetTip(button)));
        Assert.False(warning.IsEffectivelyVisible);

        Click(window, box); await model.Commit.AmendCompletion; AppHarness.Layout(window);

        Assert.True(box.IsChecked);
        Assert.Equal("Last subject\n\nIts body", editor.Text);
        Assert.Equal(("Amend commit", "Replace the last commit with this message and whatever is staged (Ctrl+Enter)"), (button.Content, ToolTip.GetTip(button)));
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(warning.IsEffectivelyVisible); Assert.Contains("already on origin/main", warning.Text);

        Click(window, box); AppHarness.Layout(window);

        Assert.False(box.IsChecked); Assert.Equal("Draft", editor.Text);
        Assert.Equal("Commit staged", button.Content); Assert.False(warning.IsEffectivelyVisible);
        window.Close();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
}
