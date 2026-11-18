using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Dialogs;

// The red question before a discard: Git's refusal stays in it with the reason, and a discard that is running is not
// closed under the user's hands.
public sealed class DiscardDialogTests
{
    [AvaloniaFact]
    public async Task WhenDiscardIsRefusedByGit_ShouldStayOpenWithTheReason()
    {
        var h = Changed();
        h.Writer.Result = GitWriteResult.Rejected("Git could not restore a.txt.");
        await using var model = h.Shell();
        var window = h.Window(model);
        await AskAsync(model, window);

        await model.Discard.Confirmation.ConfirmCommand.ExecuteAsync(null);
        AppHarness.Layout(window);

        Assert.True(Overlay(window).IsEffectivelyVisible);
        Assert.Contains("Git could not restore a.txt.", Overlay(window).GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text ?? ""));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenEscapeIsPressedWhileTheDiscardRuns_ShouldNotClose()
    {
        var h = Changed();
        h.Writer.Gate.IsClosed = true;
        await using var model = h.Shell();
        var window = h.Window(model);
        await AskAsync(model, window);
        var confirming = model.Discard.Confirmation.ConfirmCommand.ExecuteAsync(null);
        await h.Writer.Gate.Started;
        AppHarness.Layout(window);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        AppHarness.Layout(window);

        Assert.True(Overlay(window).IsEffectivelyVisible);
        Assert.True(model.Discard.Confirmation.IsOpen);
        h.Writer.Gate.Open();
        await confirming;
        window.Close();
    }

    // One modified file, asked about.
    private static AppHarness Changed()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        return h;
    }

    private static async Task AskAsync(ShellViewModel model, MainWindow window)
    {
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        window.Descendant<ListBox>("UnstagedList")!.SelectedItems!.Add(model.FileStatus.UnstagedFiles[0]);
        model.Discard.DiscardSelectedCommand.Execute(null);
        AppHarness.Layout(window);
        Assert.True(Overlay(window).IsEffectivelyVisible);
    }

    private static DialogHost Overlay(Window window) => window.Descendant<DialogHost>("DiscardOverlay")!;
}
