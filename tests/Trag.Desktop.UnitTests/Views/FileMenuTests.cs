using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Dialogs;
using Trag.Domain.Diff;
using Trag.Domain.Paths;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.History;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.Desktop.UnitTests.Views;

// A file's menu, in the file status lists and in a commit's files: the file opened, shown in the file manager or its path
// copied, an untracked one ignored, an unstaged one discarded, or a commit's version saved or restored or its changes to
// the file undone, then its history and its blame. The History section that follows one file names it in its header with a way
// back to the whole history, and draws no graph.
public sealed class FileMenuTests
{
    private static FileDiffMetadata Changed(string path) => new(Snapshots.Path(path), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'));

    [AvaloniaFact]
    public async Task WhenAStatusFileIsRightClicked_ShouldOfferToOpenItAndItsHistoryAndItsBlame()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("src/a.txt"), Snapshots.Staged("src/b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);

        var menu = RightClick(window, RowOf(window, "UnstagedList"));

        Assert.Equal([("Open", "external"), ("Open in File Explorer", "folder"), ("Copy path", "link"), ("Discard", "discard"), ("File history", "history"), ("Blame", "commit")],
            Visible(menu).Select(item => ((string)item.Header!, Assert.IsType<LineIcon>(item.Icon).Kind)));
        Assert.Equal((true, true), (menu.Items[5] is Separator, menu.Items[7] is Separator));
        AssertOpensFile(model, menu, "src/a.txt");
        await RunAsync(menu.Items.OfType<MenuItem>().Last());
        Assert.Equal((true, "src/a.txt"), (model.Blame.IsOpen, model.Blame.Path));
        model.Blame.CloseCommand.Execute(null); AppHarness.Layout(window);
        menu.Hide();
        AssertOpensFile(model, RightClick(window, RowOf(window, "StagedList")), "src/b.txt");
        window.Close();
    }

    // Without the mouse: a file row with focus opens its menu on the menu key or Shift+F10, in either list.
    [AvaloniaTheory]
    [InlineData("UnstagedList", "Menu")]
    [InlineData("StagedList", "Menu")]
    [InlineData("UnstagedList", "Shift+F10")]
    [InlineData("StagedList", "Shift+F10")]
    public async Task WhenAFocusedFileRowGetsTheMenuKey_ShouldOpenItsMenu(string list, string keys)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var row = RowOf(window, list);
        row.Focus();
        AppHarness.Layout(window);

        AppHarness.AskForMenu(window, keys);
        AppHarness.Layout(window);

        Assert.True(Assert.IsType<MenuFlyout>(row.GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout).IsOpen);
        window.Close();
    }

    // Ctrl+Down moves the focus without selecting; the menu key then selects the focused row alone, as a right-click
    // does, so the menu's Discard acts on the row whose menu is open.
    [AvaloniaFact]
    public async Task WhenTheMenuKeyOpensTheMenuOfAFocusedRowThatIsNotSelected_ShouldSelectThatRowAlone()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Click(window, rows[0], MouseButton.Left);
        AppHarness.Layout(window);

        window.KeyPress(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        window.KeyRelease(Key.Down, RawInputModifiers.Control, PhysicalKey.ArrowDown, null);
        window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        AppHarness.Layout(window);

        Assert.Equal(["b.txt"], model.FileStatus.UnstagedSelection.Select(row => row.PathText));
        Assert.True(Assert.IsType<MenuFlyout>(rows[1].GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout).IsOpen);
        window.Close();
    }

    // A submodule's row opens it as a repository of its own, first in its menu, in either list.
    [AvaloniaFact]
    public async Task WhenASubmoduleRowIsRightClicked_ShouldOfferToOpenTheSubmoduleFirst()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), '.', 'M', IsSubmodule: true, SubmoduleIsCheckedOut: true), new FileStatus(Snapshots.Path("lib"), 'M', '.', IsSubmodule: true, SubmoduleIsCheckedOut: true)]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);

        var unstaged = Visible(RightClick(window, RowOf(window, "UnstagedList"))).First();
        Assert.Equal(("Open submodule", "open"), ((string)unstaged.Header!, Assert.IsType<LineIcon>(unstaged.Icon).Kind));
        Assert.Same(model.Files.OpenSubmoduleCommand, unstaged.Command);
        Assert.Equal("Open submodule", (string)Visible(RightClick(window, RowOf(window, "StagedList"))).First().Header!);
        window.Close();
    }

    // A submodule never checked out, or deleted from the working tree, has no repository in its folder: from there Git would
    // find the superproject, so Open submodule would open the same repository again.
    [AvaloniaFact]
    public async Task WhenASubmoduleIsNotCheckedOut_ShouldNotOfferToOpenIt()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), '.', 'D', IsSubmodule: true, WorkingTreeHasNoFile: true), new FileStatus(Snapshots.Path("lib"), 'M', '.', IsSubmodule: true)]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);

        var unstaged = RightClick(window, RowOf(window, "UnstagedList"));
        Assert.Equal("Copy path", (string)Visible(unstaged).First().Header!);
        unstaged.Hide();
        Assert.Equal("Copy path", (string)Visible(RightClick(window, RowOf(window, "StagedList"))).First().Header!);
        window.Close();
    }

    // A submodule and a repository inside the working tree are folders, which Open and Open in File Explorer, made for a
    // file, could only report missing; their path can still be copied.
    [AvaloniaFact]
    public async Task WhenTheRowIsASubmoduleOrAnEmbeddedRepository_ShouldNotOfferOpenAndRevealThatAlwaysFail()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(Snapshots.Path("sub"), 'M', 'M', IsSubmodule: true, SubmoduleIsCheckedOut: true), Snapshots.Untracked("inner/")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        ListBoxItem Row(string path) => rows.Single(row => ((StatusRow)row.DataContext!).PathText == path);
        string[] Offered(MenuFlyout menu) => [.. Visible(menu).Take(2).Select(item => (string)item.Header!)];

        var inner = RightClick(window, Row("inner/"));
        Assert.Equal(["Copy path", "Ignore"], Offered(inner));
        inner.Hide();
        var unstaged = RightClick(window, Row("sub"));
        Assert.Equal(["Open submodule", "Copy path"], Offered(unstaged));
        unstaged.Hide();
        Assert.Equal(["Open submodule", "Copy path"], Offered(RightClick(window, RowOf(window, "StagedList"))));
        window.Close();
    }

    // Discard in an unstaged file's menu is the list's own Discard, with its question, on the file right-clicked: the click
    // makes that file the selection.
    [AvaloniaFact]
    public async Task WhenAnUnstagedFileIsRightClicked_ShouldOfferToDiscardThatFile()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Click(window, rows[0], MouseButton.Left);
        AppHarness.Layout(window);

        var discard = Visible(RightClick(window, rows[1])).Single(item => (string)item.Header! == "Discard");
        await RunAsync(discard);

        Assert.Same(model.Discard.DiscardSelectedCommand, discard.Command);
        Assert.Contains("danger", discard.Classes);
        Assert.Equal(["b.txt"], model.Discard.Confirmation.Subject!.Paths.Select(path => path.Display));
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // A right-click inside a selection of several files keeps them, and Discard asks about all of them, as the list's own
    // Discard does.
    [AvaloniaFact]
    public async Task WhenOneOfSeveralSelectedFilesIsRightClicked_ShouldOfferToDiscardTheSelection()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Modified("b.txt")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        Click(window, rows[0], MouseButton.Left);
        Click(window, rows[1], MouseButton.Left, RawInputModifiers.Control);
        AppHarness.Layout(window);

        await RunAsync(Visible(RightClick(window, rows[1])).Single(item => (string)item.Header! == "Discard"));

        Assert.Equal(["a.txt", "b.txt"], model.Discard.Confirmation.Subject!.Paths.Select(path => path.Display));
        Assert.Empty(h.Writer.Requests);
        window.Close();
    }

    // An untracked file can be ignored by a line of .gitignore: the file, every file with its extension, or its folder.
    [AvaloniaFact]
    public async Task WhenAnUntrackedFileIsRightClicked_ShouldOfferToIgnoreItItsExtensionOrItsFolder()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Untracked("logs/out.log"), Snapshots.Untracked("Makefile")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();

        var menu = RightClick(window, rows[1]);
        var ignore = Visible(menu).Single(item => (string)item.Header! == "Ignore");
        var choices = Visible(ignore).ToArray();

        Assert.Equal("blocked", Assert.IsType<LineIcon>(ignore.Icon).Kind);
        Assert.Equal(["This file", "All *.log files", "Folder logs/"], choices.Select(item => (string)item.Header!));
        Assert.All(choices, item => Assert.Same(model.Files.IgnoreCommand, item.Command));
        await RunAsync(choices[1]);
        Assert.Equal("*.log", Assert.IsType<IgnoreRequest>(Assert.Single(h.Writer.Requests)).Pattern);
        menu.Hide();
        Assert.Equal(["This file"], Visible(Visible(RightClick(window, rows[0])).Single(item => (string)item.Header! == "Ignore")).Select(item => (string)item.Header!));
        window.Close();
    }

    // A name that is not text of its own, or one with a line break, cannot be a line of .gitignore: the menu leaves out
    // what cannot be written, and Ignore itself once nothing is left.
    [AvaloniaFact]
    public async Task WhenAnUntrackedFileNameCannotBeALineOfGitignore_ShouldOfferOnlyWhatCanBeWritten()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [new FileStatus(new GitPath([(byte)'a', 0xFF]), '?', '?'), Snapshots.Untracked("logs/a\nb")]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        AppHarness.Layout(window);
        var rows = window.Descendant<ListBox>("UnstagedList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();

        var notText = RightClick(window, rows[0]);
        Assert.DoesNotContain(Visible(notText), item => (string?)item.Header == "Ignore");
        notText.Hide();
        var lineBreak = Visible(RightClick(window, rows[1])).Single(item => (string?)item.Header == "Ignore");

        Assert.Equal(["Folder logs/"], Visible(lineBreak).Select(item => (string?)item.Header));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenACommitsFileIsRightClicked_ShouldOfferToOpenItSaveOrRestoreItsVersionAndShowItsHistoryAndItsBlame()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = await CommitFilesAsync(h, model);

        var menu = RightClick(window, RowOf(window, "CommitFilesList"));

        Assert.Equal([("Open", "external"), ("Open in File Explorer", "folder"), ("Copy path", "link"), ("Save this version", "file"), ("Restore this version", "reset"),
                ("Revert changes in this file", "discard"), ("File history", "history"), ("Blame", "commit")],
            Visible(menu).Select(item => ((string)item.Header!, Assert.IsType<LineIcon>(item.Icon).Kind)));
        Assert.Equal((true, true), (menu.Items[3] is Separator, menu.Items[7] is Separator));
        var revert = Visible(menu).Single(item => (string)item.Header! == "Revert changes in this file");
        Assert.Same(model.Files.RevertFileCommand, revert.Command);
        Assert.Equal("a.txt", Assert.IsType<CommitFileRow>(revert.CommandParameter).File.Path.Display);
        var save = Visible(menu).Single(item => (string)item.Header! == "Save this version");
        Assert.Same(model.Files.SaveVersionCommand, save.Command);
        Assert.Equal("a.txt", Assert.IsType<CommitFileRow>(save.CommandParameter).File.Path.Display);
        AssertOpensFile(model, menu, "a.txt");
        await RunAsync(Visible(menu).Single(item => (string)item.Header! == "Restore this version"));
        Assert.Equal((Snapshots.HeadId, "a.txt"), Assert.IsType<RestoreFileRequest>(Assert.Single(h.Writer.Requests)) is var restore ? (restore.Commit, restore.Path.Display) : default);
        await RunAsync(Visible(menu).Single(item => (string)item.Header! == "File history"));
        Assert.Equal("a.txt", model.History.FilePath);
        window.Close();
    }

    // Without the mouse: the menu key on a commit's file row with focus chooses that file, as a right-click does, and opens its
    // menu, so its actions act on the file whose menu is open.
    [AvaloniaFact]
    public async Task WhenAFocusedCommitFileRowGetsTheMenuKey_ShouldChooseItAndOpenItsMenu()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = await CommitFilesAsync(h, model, "a.txt", "b.txt");
        var row = (ListBoxItem)window.Descendant<ListBox>("CommitFilesList")!.ContainerFromIndex(1)!;
        row.Focus();
        AppHarness.Layout(window);

        window.KeyPress(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        window.KeyRelease(Key.Apps, RawInputModifiers.None, PhysicalKey.ContextMenu, null);
        AppHarness.Layout(window);

        Assert.Equal("b.txt", model.History.Details.Panel.SelectedFile?.File.Path.Display);
        Assert.True(Assert.IsType<MenuFlyout>(row.GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout).IsOpen);
        window.Close();
    }

    // Restoring over the file's own changes would lose them, so it asks first, with Restore to confirm.
    [AvaloniaFact]
    public async Task WhenARestoreWouldLoseTheFilesChanges_ShouldAskFirst()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);
        await using var model = h.Shell();
        var window = await CommitFilesAsync(h, model);

        await RunAsync(Visible(RightClick(window, RowOf(window, "CommitFilesList"))).Single(item => (string)item.Header! == "Restore this version"));
        AppHarness.Layout(window);

        var overlay = window.Descendant<DialogHost>("RestoreFileOverlay")!;
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal(("Restore file", FileVersionText.RestoreQuestion("a.txt", Snapshots.HeadId)), (overlay.Title, overlay.Subtitle));
        Assert.Empty(h.Writer.Requests);
        await RunAsync(window.Descendant<Button>("ConfirmRestoreFileButton")!);
        AppHarness.Layout(window);
        Assert.Equal((false, "a.txt"), (overlay.IsEffectivelyVisible, Assert.IsType<RestoreFileRequest>(Assert.Single(h.Writer.Requests)).Path.Display));
        window.Close();
    }

    private static async Task<Window> CommitFilesAsync(AppHarness h, ShellViewModel model, params string[] paths)
    {
        h.History.Details = id => new(Snapshots.Commit(id, "Change"), "Body", paths is [] ? [Changed("a.txt")] : [.. paths.Select(Changed)]);
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null);
        await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[0];
        await model.History.Details.Completion;
        AppHarness.Layout(window);
        return window;
    }

    [AvaloniaFact]
    public async Task WhenTheHistoryFollowsAFile_ShouldNameItWithAWayBackAndDrawNoGraph()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.History.ShowFileHistory("src/a.txt");
        await model.History.HistoryCompletion;
        AppHarness.Layout(window);

        var chip = window.Descendant<Border>("FileHistoryChip")!;
        var back = window.Descendant<Button>("ClearFileHistoryButton")!;
        Assert.True(chip.IsEffectivelyVisible);
        Assert.Contains(chip.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "src/a.txt");
        Assert.Equal(("Show the whole history", "Show the whole history"), (ToolTip.GetTip(back), AutomationProperties.GetName(back)));
        Assert.False(window.Descendant<TextBlock>("GraphCaption")!.IsEffectivelyVisible);
        Assert.All(window.GetVisualDescendants().OfType<CommitGraphCell>(), cell => Assert.False(cell.IsEffectivelyVisible));

        Click(window, back, MouseButton.Left);
        await model.History.HistoryCompletion;
        AppHarness.Layout(window);
        Assert.Equal((null, false, true), (model.History.FilePath, chip.IsEffectivelyVisible, window.Descendant<TextBlock>("GraphCaption")!.IsEffectivelyVisible));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenACommitOfAFilesHistoryRenamedIt_ShouldSayWhatTheFileCameFrom()
    {
        var h = new AppHarness();
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.Id('c'), "Change a") with { Path = Snapshots.Path("src/a.txt") },
            Snapshots.Commit(Snapshots.Id('b'), "Rename a") with { Path = Snapshots.Path("src/a.txt"), OriginalPath = Snapshots.Path("src/old.txt") }], false, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.History.ShowFileHistory("src/a.txt");
        await model.History.HistoryCompletion;
        AppHarness.Layout(window);

        var rows = window.Descendant<ListBox>("HistoryList")!.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
        string[] Texts(ListBoxItem row) => [.. row.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? "")];

        Assert.DoesNotContain(rows[0].GetVisualDescendants().OfType<Border>(), badge => badge.Classes.Contains("pathbadge") && badge.IsEffectivelyVisible);
        Assert.Contains("from src/old.txt", Texts(rows[1]));
        window.Close();
    }

    // The first three entries are the file actions' commands, each on the row's path.
    private static void AssertOpensFile(ShellViewModel model, MenuFlyout menu, string path)
    {
        var items = Visible(menu).Take(3).ToArray();
        Assert.Equal<object>([model.Files.OpenCommand, model.Files.RevealCommand, model.Files.CopyPathCommand], items.Select(item => item.Command!));
        Assert.All(items, item => Assert.Equal(path, Assert.IsType<GitPath>(item.CommandParameter).Display));
    }

    private static IEnumerable<MenuItem> Visible(MenuFlyout menu) => menu.Items.OfType<MenuItem>().Where(item => item.IsVisible);
    private static IEnumerable<MenuItem> Visible(MenuItem menu) => menu.Items.OfType<MenuItem>().Where(item => item.IsVisible);

    private static ListBoxItem RowOf(Window window, string list) =>
        window.Descendant<ListBox>(list)!.GetVisualDescendants().OfType<ListBoxItem>().First();

    private static MenuFlyout RightClick(Window window, ListBoxItem row)
    {
        Click(window, row, MouseButton.Right);
        AppHarness.Layout(window);
        return Assert.IsType<MenuFlyout>(row.GetVisualDescendants().OfType<Control>().First(control => control.ContextFlyout is not null).ContextFlyout);
    }

    private static Task RunAsync(MenuItem item) => RunAsync(item.Command!, item.CommandParameter);
    private static Task RunAsync(Button button) => RunAsync(button.Command!, button.CommandParameter);

    private static Task RunAsync(System.Windows.Input.ICommand command, object? parameter)
    {
        Assert.True(command.CanExecute(parameter));
        if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asynchronous) return asynchronous.ExecuteAsync(parameter);
        command.Execute(parameter);
        return Task.CompletedTask;
    }

    private static void Click(Window window, Control control, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, button, modifiers); window.MouseUp(point, button, modifiers);
    }
}
