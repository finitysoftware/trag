using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Domain.Presentation;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Branches;
using Trag.ViewModels.Status;

namespace Trag.Desktop.UnitTests.Views;

// Every gesture in Window.KeyBindings and DesktopKeys is pressed for real; a typo in a Gesture string fails here.
public sealed class ShortcutTests
{
    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        AppHarness.Layout(window);
    }
    private const RawInputModifiers CtrlShift = RawInputModifiers.Control | RawInputModifiers.Shift;

    [AvaloniaFact]
    public async Task WhenTheWindowGesturesArePressed_ShouldRunTheToolbarCommands()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        window.Focus();
        var reads = h.Reader.Calls;
        Press(window, Key.F5, PhysicalKey.F5);
        await AppHarness.Until(() => !model.Repository.IsBusy && h.Reader.Calls == reads + 1, "F5 refresh");
        Press(window, Key.F, PhysicalKey.F, CtrlShift);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 1, "Ctrl+Shift+F fetch");
        Assert.Equal(SyncAction.Fetch, Assert.IsType<SyncRequest>(h.Writer.Requests[^1]).Action);
        window.Focus();
        Press(window, Key.P, PhysicalKey.P, CtrlShift);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 2, "Ctrl+Shift+P push");
        Assert.Equal(SyncAction.Push, Assert.IsType<SyncRequest>(h.Writer.Requests[^1]).Action);
        model.Commit.CommitMessage = "From the keyboard"; window.Focus();
        Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 3, "Ctrl+Enter commit");
        Assert.Equal("From the keyboard", Assert.IsType<CommitRequest>(h.Writer.Requests[^1]).Message);
        window.Focus();
        Press(window, Key.K, PhysicalKey.K, CtrlShift);
        Assert.True(model.Stash.Draft.IsOpen);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(model.Stash.Draft.IsOpen); Assert.Equal(3, h.Writer.Requests.Count);
        h.Reader.Snapshot = h.Synced(); // pull needs a clean tree
        await model.RefreshCommand.ExecuteAsync(null); await model.Sync.SyncCompletion; AppHarness.Layout(window); window.Focus();
        Press(window, Key.L, PhysicalKey.L, CtrlShift);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 4, "Ctrl+Shift+L pull");
        Assert.Equal(SyncAction.Pull, Assert.IsType<SyncRequest>(h.Writer.Requests[^1]).Action);
        window.Focus();
        Press(window, Key.O, PhysicalKey.O, RawInputModifiers.Control);
        await AppHarness.Until(() => model.Navigator.Section == WorkspaceSection.Repositories, "Ctrl+O repositories");
        model.Catalog.BackCommand.Execute(null); AppHarness.Layout(window); window.Focus();
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        await AppHarness.Until(() => !model.Repository.HasRepository, "Ctrl+W close");
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenEscapeIsPressedWithNoDialogOpen_ShouldCancelTheRunningOperation()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        h.History.Gate.IsClosed = true;
        model.Navigator.ShowHistoryCommand.Execute(null); await h.History.Gate.Started; AppHarness.Layout(window);
        window.Focus();
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.True(h.History.Gate.Token.IsCancellationRequested);
        h.History.Gate.Cancel(); await model.History.HistoryCompletion;
        window.Close();
    }

    // The repository list hides the open repository: there F5 reads the list again, and no key commits the hidden draft,
    // synchronizes, opens Stash or Merge over the list, or closes the repository behind it.
    [AvaloniaFact]
    public async Task WhenTheRepositoryListShows_ShouldRunOnlyTheListsShortcuts()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"), Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        model.Commit.CommitMessage = "Hidden draft";
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        window.Focus();
        var (reads, lists) = (h.Reader.Calls, h.Catalog.AccountsReads);

        Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        foreach (var (key, physical) in new[] { (Key.F, PhysicalKey.F), (Key.L, PhysicalKey.L), (Key.P, PhysicalKey.P), (Key.K, PhysicalKey.K), (Key.M, PhysicalKey.M) })
            Press(window, key, physical, CtrlShift);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Press(window, Key.F5, PhysicalKey.F5);
        await model.Catalog.CatalogCompletion; AppHarness.Layout(window);

        Assert.Empty(h.Writer.Requests);
        Assert.Equal((false, false, true), (model.Stash.Draft.IsOpen, model.MergeDialog.Merge.Draft.IsOpen, model.Repository.HasRepository));
        Assert.Equal((reads, lists + 1), (h.Reader.Calls, h.Catalog.AccountsReads));
        window.Close();
    }

    // The navigation keys lead into the open repository from the list as the sidebar does, even while its
    // search box has the focus: Ctrl+F goes to the history search, Ctrl+1 and Ctrl+2 to the file status and history lists.
    [AvaloniaTheory]
    [InlineData(Key.F, PhysicalKey.F, WorkspaceSection.Search, "HistorySearchEditor")]
    [InlineData(Key.D1, PhysicalKey.Digit1, WorkspaceSection.FileStatus, "UnstagedList")]
    [InlineData(Key.D2, PhysicalKey.Digit2, WorkspaceSection.History, "HistoryList")]
    public async Task WhenFocusIsInTheCatalogSearch_ShouldLeaveTheListForTheSectionCtrlFOrADigitNames(Key key, PhysicalKey physical, WorkspaceSection section, string focused)
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        window.Descendant<TextBox>("CatalogSearchEditor")!.Focus(); AppHarness.Layout(window);

        Press(window, key, physical, RawInputModifiers.Control);
        await model.History.HistoryCompletion; AppHarness.Layout(window);

        Assert.Equal((section, focused), (model.Navigator.Section, (window.FocusManager!.GetFocusedElement() as Control)?.Name));
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheDesktopKeysArePressed_ShouldSwitchSectionsFocusTheirEditorsAndOpenSettings()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.Focus();
        Press(window, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);
        await model.History.HistoryCompletion; AppHarness.Layout(window);
        Assert.Equal(WorkspaceSection.History, model.Navigator.Section);
        Assert.Equal("HistoryList", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        Press(window, Key.D3, PhysicalKey.Digit3, RawInputModifiers.Control);
        await model.History.HistoryCompletion; AppHarness.Layout(window);
        Assert.Equal(WorkspaceSection.Search, model.Navigator.Section);
        Assert.Equal("HistorySearchEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        window.Focus();
        Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        Assert.Equal("UnstagedList", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        await model.History.HistoryCompletion; AppHarness.Layout(window);
        Assert.Equal(WorkspaceSection.Search, model.Navigator.Section);
        Assert.Equal("HistorySearchEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        window.KeyTextInput("author"); Assert.Equal("author", model.History.SearchQuery);
        window.Focus();
        Press(window, Key.D1, PhysicalKey.Digit1, RawInputModifiers.Control);
        Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);
        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control);
        Assert.Equal("CommitMessageEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        window.Focus();
        Press(window, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "Ctrl+, settings");
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);
        Assert.Null(window.SettingsHost.ActiveSettings);
        window.Close();
    }

    // Ctrl+, opens Settings from the commit message too, where the window's other bindings also reach.
    [AvaloniaFact]
    public async Task WhenCtrlCommaIsPressedInTheCommitMessage_ShouldOpenSettings()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control);
        Assert.Equal("CommitMessageEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);

        Press(window, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);

        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "Ctrl+, settings");
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);
        window.Close();
    }

    // Ctrl+F goes to the history search from the commit message too, as the navigation keys do from every field outside a
    // dialog; the draft stays as it was typed, for Ctrl+1 or Ctrl+K to come back to.
    [AvaloniaFact]
    public async Task WhenCtrlFIsPressedInTheCommitMessage_ShouldGoToTheHistorySearchAndKeepTheDraft()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Modified("a.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control);
        window.KeyTextInput("Half a message"); AppHarness.Layout(window);

        Press(window, Key.F, PhysicalKey.F, RawInputModifiers.Control);
        await model.History.HistoryCompletion; AppHarness.Layout(window);

        Assert.Equal((WorkspaceSection.Search, "HistorySearchEditor"), (model.Navigator.Section, (window.FocusManager!.GetFocusedElement() as Control)?.Name));
        Assert.Equal("Half a message", model.Commit.CommitMessage);
        window.Close();
    }

    // Closing Settings ends the editing of the accounts, so what read them reads them again.
    [AvaloniaFact]
    public async Task WhenSettingsClose_ShouldEndTheEditingOfTheAccounts()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var edited = 0;
        model.Workbench.Accounts.Edited += () => edited++;
        Press(window, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "Ctrl+, settings");
        Assert.Equal(0, edited);

        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);

        await AppHarness.Until(() => edited == 1, "accounts edited");
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenCtrlKIsPressedDuringAMerge_ShouldFocusTheMergeMessage()
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Conflict("shared.txt")], operation: RepositoryOperation.Merge,
            merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", ""));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window); window.Focus();
        Press(window, Key.K, PhysicalKey.K, RawInputModifiers.Control);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        Assert.Equal("MergeMessageEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenEscapeIsPressedInThePublishOrCreateBranchOverlay_ShouldCloseItFromAnyControlWithoutCancellingWork()
    {
        var h = new AppHarness(); h.Reader.Snapshot = Snapshots.Create(remotes: [Snapshots.Remote()]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        h.History.Gate.IsClosed = true;
        model.Navigator.ShowHistoryCommand.Execute(null); await h.History.Gate.Started; AppHarness.Layout(window);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.Equal("PublishRemoteSelector", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(model.Publish.Draft.IsOpen); Assert.False(h.History.Gate.Token.IsCancellationRequested);
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        window.Descendant<Button>("CancelCreateBranchButton")!.Focus(); AppHarness.Layout(window);
        Assert.Equal("CancelCreateBranchButton", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(model.Branches.Draft.IsOpen); Assert.False(h.History.Gate.Token.IsCancellationRequested);
        h.History.Gate.Cancel(); await model.History.HistoryCompletion;
        window.Close();
    }

    // A dialog that names no control to focus, or whose control is hidden for what it shows now, still takes the focus
    // when it opens, so its keys reach it: Remotes, Publish without an account, and Go to on a branch's tip, where Enter
    // switches.
    [AvaloniaFact]
    public async Task WhenADialogHasNoVisibleControlToFocus_ShouldStillTakeItsKeysWhenItOpens()
    {
        var h = new AppHarness(); var tip = Snapshots.Id('b');
        h.Branches.Local.Clear(); h.Branches.Local.AddRange(["main", "old"]); h.Branches.Heads["old"] = tip;
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", tip), Snapshots.Commit(tip, "Initial")], false, page);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);

        model.RemoteSettings.OpenCommand.Execute(null); AppHarness.Layout(window);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.RemoteSettings.IsOpen);

        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.Publish.Draft.IsOpen);

        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion; AppHarness.Layout(window);
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.True(model.GoTo.Dialog.IsTip);
        Press(window, Key.Enter, PhysicalKey.Enter);
        await AppHarness.Until(() => h.Writer.Requests.Count == 1 && !model.Repository.IsWriting, "switch from Enter");
        Assert.Equal(BranchAction.Switch, Assert.IsType<BranchRequest>(h.Writer.Requests[0]).Action);
        window.Close();
    }

    // An open dialog keeps the keyboard: Tab goes round its own controls, and the window's shortcuts do nothing behind the
    // scrim, so Ctrl+Enter in the new branch's name commits nothing and Ctrl+2 leaves the section as it is.
    [AvaloniaFact]
    public async Task WhenADialogIsOpen_ShouldKeepTabAndTheWindowShortcutsInsideIt()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        model.Commit.CommitMessage = "Not from the dialog";
        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var dialog = window.Descendant<Control>("NewBranchNameEditor")!.FindAncestorOfType<Trag.Desktop.Dialogs.DialogHost>()!;
        for (var i = 0; i < 12; i++)
        {
            Press(window, Key.Tab, PhysicalKey.Tab);
            Assert.True(window.FocusManager!.GetFocusedElement() is Visual focused && dialog.IsVisualAncestorOf(focused), "Tab " + i + " left the dialog");
        }
        window.Descendant<TextBox>("NewBranchNameEditor")!.Focus(); AppHarness.Layout(window);
        Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        Press(window, Key.D2, PhysicalKey.Digit2, RawInputModifiers.Control);
        Assert.Empty(h.Writer.Requests); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.True(model.Branches.Draft.IsOpen);
        window.Close();
    }

    // An open flyout keeps the keyboard as a dialog does: in the branch picker's search Ctrl+Enter commits nothing,
    // Ctrl+Shift+P pushes nothing, no dialog or Settings opens under the picker, and Ctrl+O leaves no picker of the
    // hidden repository over the list. Escape closes the flyout.
    [AvaloniaFact]
    public async Task WhenAFlyoutIsOpen_ShouldRunNoWindowShortcutBehindIt()
    {
        var h = new AppHarness(); h.Reader.Snapshot = h.Synced(Snapshots.Staged("b.txt"));
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        model.Commit.CommitMessage = "Not from the branch picker";
        var button = window.Descendant<Button>("BranchButton")!;
        button.Flyout!.ShowAt(button); await model.Branches.List.Completion; AppHarness.Layout(window);
        Assert.Equal("BranchSearchEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);

        Press(window, Key.Enter, PhysicalKey.Enter, RawInputModifiers.Control);
        Press(window, Key.P, PhysicalKey.P, CtrlShift);
        Press(window, Key.K, PhysicalKey.K, CtrlShift);
        Press(window, Key.OemComma, PhysicalKey.Comma, RawInputModifiers.Control);
        Press(window, Key.W, PhysicalKey.W, RawInputModifiers.Control);
        Press(window, Key.O, PhysicalKey.O, RawInputModifiers.Control);
        await AppHarness.Until(() => !model.Repository.IsWriting, "no write");

        Assert.Empty(h.Writer.Requests); Assert.Null(window.SettingsHost.ActiveSettings);
        Assert.Equal((WorkspaceSection.FileStatus, true, false, true), (model.Navigator.Section, model.Repository.HasRepository, model.Stash.Draft.IsOpen, button.Flyout.IsOpen));
        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(button.Flyout.IsOpen);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAnOverlayOpens_ShouldFocusItsControlCloseOnEscapeAndConfirmOnEnter()
    {
        var h = new AppHarness(); var stash = h.Stashes.Add("On main: spike");
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")], stashHead: stash.Id);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion; AppHarness.Layout(window);

        model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
        Assert.Equal("StashMessageEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.Stash.Draft.IsOpen);
        model.Stash.OpenStashCommand.Execute(null); AppHarness.Layout(window);
        Press(window, Key.Enter, PhysicalKey.Enter);
        await AppHarness.Until(() => !model.Repository.IsWriting && h.Writer.Requests.Count == 1, "stash from Enter");
        Assert.Equal(StashAction.Push, Assert.IsType<StashRequest>(h.Writer.Requests[^1]).Action); Assert.False(model.Stash.Draft.IsOpen);

        model.Stash.Drop.Request(model.Stash.Rows[0]); AppHarness.Layout(window);
        Assert.Equal("CancelDropStashButton", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.Null(model.Stash.Drop.Subject);

        model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
        model.Discard.DiscardSelectedCommand.Execute(null); AppHarness.Layout(window);
        Assert.Equal("CancelDiscardButton", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.Discard.Confirmation.IsOpen);

        await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.Equal("NewBranchNameEditor", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.Branches.Draft.IsOpen);

        model.Branches.Delete.Request(new BranchRow(new("topic", "refs/heads/topic", Snapshots.HeadId, false), false)); AppHarness.Layout(window);
        Assert.Equal("CancelDeleteBranchButton", (window.FocusManager.GetFocusedElement() as Control)?.Name);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.Null(model.Branches.Delete.Subject);

        model.GoTo.Target.IsOpen = true; model.GoTo.Target.Name = "look"; AppHarness.Layout(window);
        var editor = (TextBox?)window.FocusManager.GetFocusedElement();
        Assert.Equal("GoToBranchNameEditor", editor?.Name); Assert.Equal("look", editor?.SelectedText);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.GoTo.Target.IsOpen);

        model.GoTo.Keep.Name = "kept-from-abc"; model.GoTo.Keep.IsOpen = true; AppHarness.Layout(window);
        editor = (TextBox?)window.FocusManager.GetFocusedElement();
        Assert.Equal("KeepBranchNameEditor", editor?.Name); Assert.Equal("kept-from-abc", editor?.SelectedText);
        Press(window, Key.Escape, PhysicalKey.Escape); Assert.False(model.GoTo.Keep.IsOpen);
        Assert.Single(h.Writer.Requests);
        window.Close();
    }
}
