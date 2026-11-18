using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Views.Pickers;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// The lines under the author in the commit area: whether Git signs the commit, and its co-authors. Add co-author opens
// the people to choose from, a chosen one is a Co-authored-by line in the editor and a chip, and the chip's × takes the
// line out again; like the editor, neither changes the message while a write is running.
public sealed class CommitComposerUiTests
{
    [AvaloniaFact]
    public async Task WhenTheRepositorySignsCommits_ShouldSaySoUnderTheAuthor()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var signing = window.Descendant<TextBlock>("CommitSigningText")!;
        Assert.False(signing.IsEffectivelyVisible);

        h.Reader.Snapshot = h.Reader.Snapshot with { Signing = CommitSigning.Ssh };
        await model.RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);

        Assert.Equal(("Commits are signed with your SSH key.", true), (signing.Text, signing.IsEffectivelyVisible));
        window.Close();
    }

    // A message pasted longer than Git takes stays whole in the editor, and the line under it says it is too long, instead
    // of the editor cutting it to a length that commits with its end silently lost.
    [AvaloniaFact]
    public async Task WhenALongerMessageIsPasted_ShouldKeepItWholeAndSayItIsTooLong()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Staged("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Descendant<TextBox>("CommitMessageEditor")!.Focus();

        window.KeyTextInput("Subject\n\n" + new string('a', 70_000)); AppHarness.Layout(window);

        Assert.Equal(70_009, model.Commit.CommitMessage.Length);
        Assert.Equal((WriteText.CommitMessageInvalid, true), (window.Descendant<TextBlock>("CommitMessageHint")!.Text, window.Descendant<TextBlock>("CommitMessageHint")!.IsEffectivelyVisible));
        Assert.False(model.Commit.CommitCommand.CanExecute(null));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenACoAuthorIsChosenAndRemoved_ShouldWriteTheLineAndShowItAsAChipUntilRemoved()
    {
        var h = new AppHarness();
        h.History.Authors.AddRange([new("Ana Anić", "ana@example.invalid"), new("Marko M.", "marko@example.invalid")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        var chips = window.Descendant<ItemsControl>("CoAuthorList")!;
        editor.Text = "Fix the parser";
        Assert.False(chips.IsEffectivelyVisible);

        var add = window.Descendant<Button>("AddCoAuthorButton")!;
        Assert.True(add.IsEffectivelyVisible);
        add.Flyout!.ShowAt(add); await model.Commit.CoAuthorPicker.People.Completion; AppHarness.Layout(window);
        var picker = add.Flyout is Flyout { Content: CoAuthorPicker content } ? content : null;
        Assert.NotNull(picker); AppHarness.Layout(window);
        var choices = picker!.Descendant<ItemsControl>("CoAuthorChoices")!;
        Assert.Equal(2, choices.ItemCount);
        picker.Descendant<TextBox>("CoAuthorSearchBox")!.Text = "anić"; AppHarness.Layout(window);
        Assert.Equal(1, choices.ItemCount);
        var ana = choices.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible);
        ana.Command!.Execute(ana.CommandParameter); AppHarness.Layout(window);

        Assert.Equal("Fix the parser\n\nCo-authored-by: Ana Anić <ana@example.invalid>", editor.Text);
        Assert.False(add.Flyout.IsOpen);
        Assert.True(chips.IsEffectivelyVisible);
        Assert.Contains(chips.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ana Anić" && text.IsEffectivelyVisible);
        var remove = chips.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "RemoveCoAuthorButton");
        model.Commit.Repository.IsWriting = true; AppHarness.Layout(window);
        Assert.False(add.IsEffectivelyEnabled); Assert.False(remove.IsEffectivelyEnabled);
        model.Commit.Repository.IsWriting = false; AppHarness.Layout(window);
        Click(window, remove); AppHarness.Layout(window);

        Assert.Equal("Fix the parser", editor.Text);
        Assert.False(chips.IsEffectivelyVisible);
        window.Close();
    }

    // A co-author chosen before anything was written leaves the box without a subject; Commit waits, and the area says why.
    [AvaloniaFact]
    public async Task WhenTheMessageHasCoAuthorsButNoSubject_ShouldSayWhyCommitWaits()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var hint = window.Descendant<TextBlock>("CommitMessageHint")!;
        Assert.False(hint.IsEffectivelyVisible);

        model.Commit.CoAuthorPicker.ChooseCommand.Execute(new GitIdentity("Ana Anić", "ana@example.invalid")); AppHarness.Layout(window);

        Assert.Equal(("Write the commit's subject above its Co-authored-by lines.", true), (hint.Text, hint.IsEffectivelyVisible));
        window.Close();
    }

    // The editor counts characters and Git is offered 64 KB of UTF-8: a message of letters two bytes each fits the
    // editor and not Git, and the area says so instead of only keeping Commit back.
    [AvaloniaFact]
    public async Task WhenTheMessageExceedsSixtyFourKilobytesInBytes_ShouldDisableCommitAndExplain()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Staged("a.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        var hint = window.Descendant<TextBlock>("CommitMessageHint")!;

        editor.Text = new string('ж', 40_000); AppHarness.Layout(window);

        Assert.Equal(40_000, editor.Text!.Length);
        Assert.False(window.Descendant<Button>("CommitButton")!.IsEffectivelyEnabled);
        Assert.Equal((WriteText.CommitMessageInvalid, true), (hint.Text, hint.IsEffectivelyVisible));
        window.Close();
    }

    // The editor is closed to typing while a write runs and opens again with the text it had.
    [AvaloniaFact]
    public async Task WhenTheEditorIsDisabledDuringAWrite_ShouldKeepTypedTextAfterwards()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var editor = window.Descendant<TextBox>("CommitMessageEditor")!;
        editor.Text = "Half written";

        model.Commit.Repository.IsWriting = true; AppHarness.Layout(window);
        var closed = editor.IsEffectivelyEnabled;
        model.Commit.Repository.IsWriting = false; AppHarness.Layout(window);

        Assert.False(closed);
        Assert.True(editor.IsEffectivelyEnabled);
        Assert.Equal(("Half written", "Half written"), (editor.Text, model.Commit.CommitMessage));
        window.Close();
    }

    // Ctrl+Enter in the message commits what is written, once, and is not a line break the editor adds to it first.
    [AvaloniaFact]
    public async Task WhenCtrlEnterIsPressedInsideTheMessage_ShouldCommitOnceAndInsertNoNewline()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Staged("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Descendant<TextBox>("CommitMessageEditor")!.Focus();
        window.KeyTextInput("From the message"); AppHarness.Layout(window);

        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null); AppHarness.Layout(window);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 1, "Ctrl+Enter commit");
        AppHarness.Layout(window);

        Assert.Equal("From the message", Assert.IsType<CommitRequest>(Assert.Single(h.Writer.Requests)).Message);
        window.Close();
    }

    // The commit message is written in the file status section; while history shows, Ctrl+Enter does not commit the draft
    // the user cannot see.
    [AvaloniaFact]
    public async Task WhenCtrlEnterIsPressedWhileHistoryShows_ShouldNotCommitTheHiddenDraft()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Staged("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        model.Commit.CommitMessage = "Hidden draft";
        model.Navigator.ShowHistoryCommand.Execute(null); AppHarness.Layout(window);
        window.Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null); AppHarness.Layout(window);

        Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
    }
}
