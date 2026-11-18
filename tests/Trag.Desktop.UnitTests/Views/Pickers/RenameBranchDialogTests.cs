using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Dialogs;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Writes;

namespace Trag.Desktop.UnitTests.Views.Pickers;

// The rename dialog over the window: the branch's name to change, Rename only for a new name no other branch has, the
// reason a name is refused, and Cancel.
public sealed class RenameBranchDialogTests
{
    [AvaloniaFact]
    public async Task WhenABranchIsRenamed_ShouldOfferRenameOnlyForANewFreeName()
    {
        var h = new AppHarness();
        h.Branches.Local.AddRange(["topic", "other"]);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Branches.List.Completion;

        model.Branches.RequestRenameCommand.Execute(model.Branches.Rows.Local.Single(row => row.Name == "topic"));
        AppHarness.Layout(window);

        var dialog = window.Descendant<RenameBranchDialog>("RenameBranchOverlay")!;
        var (editor, rename) = (window.Descendant<TextBox>("RenameBranchEditor")!, window.Descendant<Button>("ConfirmRenameBranchButton")!);
        Assert.Equal((true, "topic", false), (dialog.IsOpen, editor.Text, rename.IsEffectivelyEnabled));
        Assert.Contains(window.Descendant<StackPanel>("RenameBranchFields")!.Children.OfType<TextBlock>(), text => text.Text == BranchText.RenameSubtitle("topic"));
        editor.Text = "other";
        AppHarness.Layout(window);
        Assert.Equal((false, true), (rename.IsEffectivelyEnabled, window.Descendant<TextBlock>("RenameBranchError")!.IsEffectivelyVisible));
        editor.Text = "feature/topic";
        AppHarness.Layout(window);
        Assert.True(rename.IsEffectivelyEnabled);
        editor.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await AppHarness.Until(() => !dialog.IsOpen, "the rename to close the dialog");
        var sent = Assert.IsType<BranchRenameRequest>(Assert.Single(h.Writer.Requests));
        Assert.Equal(("topic", "feature/topic"), (sent.Name, sent.NewName));

        model.Branches.RequestRenameCommand.Execute(model.Branches.Rows.Local.Single(row => row.Name == "topic"));
        AppHarness.Layout(window);
        window.Descendant<Button>("CancelRenameBranchButton")!.Command!.Execute(null);
        AppHarness.Layout(window);
        Assert.False(dialog.IsOpen);
        window.Close();
    }
}
