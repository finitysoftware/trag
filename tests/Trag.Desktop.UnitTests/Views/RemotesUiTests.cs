using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Dialogs;
using Trag.Domain.Writes;

namespace Trag.Desktop.UnitTests.Views;

// More → Remotes… opens the Remotes dialog over the window: the remotes listed with a change and a remove action each,
// the add form under them, and a remove that asks in place, with a red Remove, before anything is written.
public sealed class RemotesUiTests
{
    [AvaloniaFact]
    public async Task WhenRemotesIsChosenUnderMore_ShouldListTheRemotesAddOneAndAskBeforeRemovingOne()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var more = window.Descendant<Button>("MoreButton")!;
        more.Flyout!.ShowAt(more); AppHarness.Layout(window);
        var item = Assert.IsType<MenuFlyout>(more.Flyout).Items.OfType<MenuItem>().Single(item => item.Name == "RemotesMenuItem");
        Assert.Equal(("Remotes", "cloud"), (item.Header, Assert.IsType<LineIcon>(item.Icon).Kind));

        item.Command!.Execute(null); AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("RemotesOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal(("Remotes", "Where this repository fetches from and pushes to. Changes here stay in this repository; only a fetch after adding asks the server."),
            (overlay.Title, overlay.Subtitle));
        var list = window.Descendant<ItemsControl>("RemoteList")!;
        Assert.Equal(1, list.ItemCount);
        var add = window.Descendant<Button>("AddRemoteButton")!;
        Assert.True(add.IsEffectivelyVisible); Assert.False(add.IsEffectivelyEnabled);
        window.Descendant<TextBox>("NewRemoteNameEditor")!.Text = "upstream";
        window.Descendant<TextBox>("NewRemoteUrlEditor")!.Text = "https://github.com/acme/trag.git"; AppHarness.Layout(window);
        Assert.True(add.IsEffectivelyEnabled);
        var fetch = window.Descendant<CheckBox>("FetchAfterAddingBox")!;
        Assert.True(fetch.IsEffectivelyVisible); Assert.True(fetch.IsChecked);
        fetch.IsChecked = false;
        Assert.False(model.RemoteSettings.FetchAfterAdding);

        var trash = list.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("trash"));
        trash.Command!.Execute(trash.CommandParameter); AppHarness.Layout(window);

        Assert.False(add.IsEffectivelyVisible);
        Assert.Contains("Nothing is deleted on the server.", window.Descendant<TextBlock>("RemoteRemoval")!.Text);
        var remove = window.Descendant<Button>("RemoveRemoteButton")!;
        Assert.True(remove.IsEffectivelyVisible); Assert.Contains("danger", remove.Classes);
        window.Descendant<Button>("CancelRemoveRemoteButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(add.IsEffectivelyVisible); Assert.Empty(h.Writer.Requests);
        window.Descendant<Button>("CloseRemotesButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        window.Close();
    }

    // Enter in a field of the add or the change form does what the form's button does, as in the other dialogs.
    [AvaloniaFact]
    public async Task WhenEnterIsPressedInAFormsField_ShouldAddOrSaveAsItsButtonWould()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        var remotes = model.RemoteSettings;
        remotes.OpenCommand.Execute(null); remotes.FetchAfterAdding = false; AppHarness.Layout(window);

        foreach (var field in new[] { "NewRemoteNameEditor", "NewRemoteUrlEditor" })
        {
            (remotes.NewName, remotes.NewUrl) = ("upstream", "https://github.com/acme/trag.git"); AppHarness.Layout(window);
            await PressEnterAsync(window, field, () => remotes.NewName.Length == 0);
        }
        foreach (var field in new[] { "EditRemoteNameEditor", "EditRemoteUrlEditor" })
        {
            await remotes.BeginEditCommand.ExecuteAsync(remotes.Remotes[0]);
            remotes.EditUrl = "https://github.com/acme/moved.git"; AppHarness.Layout(window);
            await PressEnterAsync(window, field, () => !remotes.IsEditing);
        }

        Assert.Equal([typeof(RemoteAddRequest), typeof(RemoteAddRequest), typeof(RemoteEditRequest), typeof(RemoteEditRequest)],
            h.Writer.Requests.Select(request => request.GetType()));
        window.Close();
    }

    private static async Task PressEnterAsync(Window window, string field, Func<bool> done)
    {
        window.Descendant<TextBox>(field)!.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await AppHarness.Until(done, "Enter in " + field); AppHarness.Layout(window);
    }
}
