using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views.Pickers;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

public sealed class TagUiTests
{
    // The tag of the chosen commit (offered by its menu, CommitMenuTests) is created from a dialog that takes a name and a
    // message, and says which commit it tags.
    [AvaloniaFact]
    public async Task WhenACommitIsChosenInTheHistory_ShouldCreateItsTagFromTheDialog()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);

        model.Tags.OpenCreateTagCommand.Execute(null); AppHarness.Layout(window);
        var overlay = window.Descendant<DialogHost>("CreateTagOverlay")!;
        var create = window.Descendant<Button>("ConfirmCreateTagButton")!;
        Assert.True(overlay.IsEffectivelyVisible); Assert.False(create.IsEffectivelyEnabled);
        Assert.NotEmpty(model.Tags.Draft.Subtitle);
        Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == model.Tags.Draft.Subtitle && text.IsEffectivelyVisible);
        Assert.False(window.Descendant<CheckBox>("PushTagBox")!.IsEffectivelyVisible);
        window.Descendant<TextBox>("NewTagNameEditor")!.Text = "v1.0";
        window.Descendant<TextBox>("TagMessageEditor")!.Text = "First release";
        AppHarness.Layout(window);
        Assert.True(create.IsEffectivelyEnabled);
        Assert.Same(model.Tags.CreateTagCommand, create.Command);
        await model.Tags.CreateTagCommand.ExecuteAsync(null); AppHarness.Layout(window);

        var request = Assert.IsType<TagRequest>(Assert.Single(h.Writer.Requests)).Tag;
        Assert.Equal(("v1.0", model.History.Page.Commits[0].Id, "First release"), (request.Name, request.Commit, request.Message));
        Assert.False(overlay.IsEffectivelyVisible);
        window.Close();
    }

    // A repository that signs its tags gets a dialog whose message is no longer optional, and says why.
    [AvaloniaFact]
    public async Task WhenTheRepositorySignsTags_ShouldShowTheMessageAsRequiredAndWhy()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = h.Reader.Snapshot with { SignsTags = true };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);

        model.Tags.OpenCreateTagCommand.Execute(null); AppHarness.Layout(window);

        var shown = window.Descendant<DialogHost>("CreateTagOverlay")!.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
        Assert.Contains("Message", shown);
        Assert.Contains("Tags are signed in this repository, so a tag needs a message.", shown);
        window.Close();
    }

    // With a remote, the dialog offers the push to it, checked.
    [AvaloniaFact]
    public async Task WhenTheRepositoryHasARemote_ShouldOfferToPushTheTagThereChecked()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);

        model.Tags.OpenCreateTagCommand.Execute(null); AppHarness.Layout(window);

        var push = window.Descendant<CheckBox>("PushTagBox")!;
        Assert.True(push.IsEffectivelyVisible); Assert.True(push.IsChecked);
        Assert.Equal("Push the tag to origin", push.Content);
        push.IsChecked = false; AppHarness.Layout(window);
        Assert.False(model.Tags.Draft.Push);
        window.Close();
    }

    // A row's Push and Delete are one pair with nothing between their buttons.
    [AvaloniaFact]
    public async Task WhenAPushableTagIsListed_ShouldSetPushAndDeleteSideBySide()
    {
        var h = new AppHarness();
        h.Branches.Tags.Add(new("v1.9", Snapshots.Id('b'), "Initial"));
        h.Reader.Snapshot = h.Synced() with { TagRefs = "one" };
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("fixture"); await model.Tags.List.Completion;
        var picker = new TagPicker { DataContext = model.Tags };
        var window = new Window { Content = picker, Width = 400, Height = 300 };
        window.Show(); AppHarness.Layout(window);

        var buttons = picker.GetVisualDescendants().OfType<Button>().ToArray();
        var (push, delete) = (buttons.Single(button => button.Classes.Contains("rowaction")), buttons.Single(button => button.Classes.Contains("trash")));
        Assert.True(push.IsEffectivelyVisible);
        var gap = AppHarness.Gap(window, push, delete);
        Assert.Equal(0, gap, 0.5);
        window.Close();
    }

    // The section appears with the first tag: its button names the highest one, and its list filters by name and offers
    // a push and a delete per row.
    [AvaloniaFact]
    public async Task WhenTheRepositoryHasTags_ShouldShowTheTagsSectionWithAFilteredListOfThem()
    {
        var h = new AppHarness();
        h.Branches.Tags.AddRange([new("v1.10", Snapshots.HeadId, "Release ten"), new("v1.9", Snapshots.Id('b'), "Initial")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var section = window.Descendant<Button>("TagPickerButton")!;
        Assert.False(section.IsEffectivelyVisible);

        h.Reader.Snapshot = h.Synced() with { TagRefs = "two" };
        await model.RefreshCommand.ExecuteAsync(null); await model.Tags.List.Completion; AppHarness.Layout(window);

        Assert.True(section.IsEffectivelyVisible);
        Assert.Contains(section.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "v1.10");
        Assert.Equal("2", window.Descendant<TextBlock>("TagCountLabel")!.Text);
        section.Flyout!.ShowAt(section); AppHarness.Layout(window);
        var picker = section.Flyout is Flyout { Content: TagPicker content } ? content : null;
        Assert.NotNull(picker); AppHarness.Layout(window);
        var list = picker!.Descendant<ItemsControl>("TagList")!;
        Assert.Equal(2, list.ItemCount);
        Assert.Contains(picker.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("rowaction") && button.IsEffectivelyVisible);
        Assert.Contains(picker.GetVisualDescendants().OfType<Button>(), button => button.Classes.Contains("trash"));
        picker.Descendant<TextBox>("TagSearchBox")!.Text = "v1.9"; AppHarness.Layout(window);
        Assert.Equal(1, list.ItemCount);
        section.Flyout.Hide();
        window.Close();
    }

    // Deleting asks with a danger button, and with a remote it offers to delete the tag there too, unchecked.
    [AvaloniaFact]
    public async Task WhenATagDeletionIsRequested_ShouldAskWithTheRemoteOptionUntilCanceled()
    {
        var h = new AppHarness();
        h.Branches.Tags.Add(new("v1.9", Snapshots.Id('b'), "Initial"));
        h.Reader.Snapshot = h.Synced() with { TagRefs = "one" };
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Tags.List.Completion; AppHarness.Layout(window);

        model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[0]); AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("DeleteTagOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("Delete tag v1.9?", overlay.Title);
        var remote = window.Descendant<CheckBox>("DeleteTagOnRemoteBox")!;
        Assert.True(remote.IsEffectivelyVisible); Assert.False(remote.IsChecked);
        Assert.Equal("Also delete it on origin", remote.Content);
        var hint = window.Descendant<TextBlock>("DeleteTagOnRemoteHint")!;
        Assert.True(hint.IsEffectivelyVisible);
        remote.IsChecked = true; AppHarness.Layout(window);
        Assert.False(hint.IsEffectivelyVisible);
        Assert.Contains("danger", window.Descendant<Button>("ConfirmDeleteTagButton")!.Classes);
        window.Descendant<Button>("CancelDeleteTagButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheTagDialogIsCanceled_ShouldCloseItAndCreateNothing()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0]; await model.History.Details.Completion; AppHarness.Layout(window);
        model.Tags.OpenCreateTagCommand.Execute(null); AppHarness.Layout(window);
        window.Descendant<TextBox>("NewTagNameEditor")!.Text = "v1.0"; AppHarness.Layout(window);

        window.Descendant<Button>("CancelCreateTagButton")!.Command!.Execute(null); AppHarness.Layout(window);

        Assert.False(window.Descendant<DialogHost>("CreateTagOverlay")!.IsEffectivelyVisible);
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }
}
