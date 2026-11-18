using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views.Repositories;
using Trag.Desktop.Views.Shell;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views.Repositories;

public sealed class RepositoriesUiTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "trag-catalog-ui");
    private static RepositoryEntry Entry(string name) => new(Path.Combine(Root, name), name, DateTimeOffset.UtcNow.AddDays(-1));

    [AvaloniaFact]
    public async Task WhenACloneBeginsFromAnAccountGroup_ShouldRealizeItsRowsAndBindTheCloneDialogUntilEscape()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Entries.Add(Entry("alpha"));
        h.Catalog.Probes[Path.Combine(Root, "alpha")] = new(RepositoryLinkStatus.Valid, "main", "https://github.com/acme/alpha.git", Path.Combine(Root, "alpha"));
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow),
            new("acme/alpha", "https://github.com/acme/alpha.git", "https://github.com/acme/alpha", "main", false, DateTimeOffset.UtcNow)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.Equal(2, group.Rows.Count);
        var remoteList = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows);
        Assert.Equal(2, remoteList.ItemCount); Assert.InRange(remoteList.GetRealizedContainers().Count(), 1, 2);
        var sandbox = group.Rows.Single(row => row.FullName == "contoso/sandbox");
        Assert.False(sandbox.IsCloned); Assert.True(group.Rows.Single(row => row.FullName == "acme/alpha").IsCloned);
        model.Catalog.BeginCloneCommand.Execute(sandbox); AppHarness.Layout(window);
        Assert.True(model.Catalog.Clone.IsCloneDialogOpen);
        var dialog = window.Descendant<DialogHost>("CloneDialog")!;
        Assert.True(dialog.IsEffectivelyVisible);
        var target = window.Descendant<TextBox>("CloneTargetEditor")!;
        Assert.Equal(model.Catalog.Clone.CloneTarget, target.Text);
        Assert.True(window.Descendant<Button>("ConfirmCloneButton")!.IsEffectivelyEnabled);
        target.Text = ""; AppHarness.Layout(window);
        Assert.False(window.Descendant<Button>("ConfirmCloneButton")!.IsEffectivelyEnabled);
        Assert.Same(model.Catalog.Clone.CancelCloneCommand, window.Descendant<Button>("CancelCloneButton")!.Command);
        var submodules = window.Descendant<CheckBox>("CloneSubmodulesBox")!;
        Assert.Equal((true, "Clone submodules too"), (submodules.IsChecked, submodules.Content));
        submodules.IsChecked = false;
        Assert.False(model.Catalog.Clone.CloneSubmodules);
        // The dialog focused its folder editor when it opened; Escape closes it from there, like every other dialog.
        Assert.Equal("CloneTargetEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);
        Assert.False(model.Catalog.Clone.IsCloneDialogOpen); Assert.False(dialog.IsEffectivelyVisible); Assert.Empty(h.Catalog.Clones);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenCloneIsDoubleClicked_ShouldStartOneCloneWhoseCancelRestoresTheDialog()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        h.Catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories();
        await model.Catalog.CatalogCompletion;
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);
        model.Catalog.BeginCloneCommand.Execute(Assert.Single(group.Rows));
        AppHarness.Layout(window);
        var clone = model.Catalog.Clone;
        var confirm = window.Descendant<Button>("ConfirmCloneButton")!;

        AppHarness.DoubleTap(window, confirm);
        await h.Catalog.CloneGate.Started;
        AppHarness.Layout(window);

        Assert.Single(h.Catalog.Clones);
        Assert.False(confirm.IsEffectivelyEnabled);
        Assert.False(window.Descendant<TextBox>("CloneTargetEditor")!.IsEffectivelyEnabled);
        var cancel = window.Descendant<Button>("CancelRunningCloneButton")!;
        Assert.True(cancel.IsEffectivelyVisible);
        Assert.True(cancel.IsEffectivelyEnabled);
        Assert.False(window.Descendant<Button>("CancelCloneButton")!.IsEffectivelyVisible);
        Assert.Same(clone.CancelCloneCommand, cancel.Command);
        AppHarness.DoubleTap(window, cancel);
        Assert.True(h.Catalog.CloneGate.Token.IsCancellationRequested);
        h.Catalog.CloneGate.Cancel();
        await clone.ConfirmCloneCommand.ExecutionTask!;
        AppHarness.Layout(window);

        Assert.True(confirm.IsEffectivelyEnabled);
        Assert.True(window.Descendant<TextBox>("CloneTargetEditor")!.IsEffectivelyEnabled);
        Assert.False(cancel.IsEffectivelyVisible);
        Assert.True(window.Descendant<Button>("CancelCloneButton")!.IsEffectivelyVisible);
        Assert.True(clone.IsCloneDialogOpen);
        Assert.Equal("Clone canceled.", clone.CloneStatus);
        window.Close();
    }

    // The clone dialog covers the window below its title bar, as every other dialog does: the toolbar and the actions
    // beside it take no click while it is open, so a click cannot pull the focus out of it and let the window's
    // shortcuts run behind it.
    [AvaloniaFact]
    public async Task WhenTheCloneDialogIsOpen_ShouldCoverTheToolbarAndTheActionsLikeEveryDialog()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);

        model.Catalog.BeginCloneCommand.Execute(Assert.Single(group.Rows)); AppHarness.Layout(window);
        // A hit test reads the composed scene, which the next render tick brings up to date with the open dialog.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); AppHarness.Layout(window);

        Control[] bars = [window.GetVisualDescendants().OfType<RepositoriesToolBar>().Single(), window.GetVisualDescendants().OfType<ShellActionsBar>().Single()];
        foreach (var button in bars.Select(bar => bar.GetVisualDescendants().OfType<Button>().First(button => button.IsEffectivelyVisible && button.IsEffectivelyEnabled)))
        {
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            var hit = window.InputHitTest(center) as Visual;
            Assert.False(hit is not null && (hit == button || button.IsVisualAncestorOf(hit)), (button.Name ?? button.Content?.ToString()) + " takes the click");
        }
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenARowIsDoubleTapped_ShouldOpenALocalRepositoryOrOfferToCloneARemoteOne()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Entries.Add(Entry("alpha"));
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var locals = view.Descendant<ListBox>("LocalRepositoriesList")!;
        locals.SelectedIndex = 0; AppHarness.Layout(window);
        AppHarness.DoubleTap(window, locals.GetRealizedContainers().OfType<ListBoxItem>().First());
        await AppHarness.Until(() => model.Repository.HasRepository, "the double-tapped repository to open");
        Assert.EndsWith("alpha", model.Repository.Snapshot!.Root); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section);

        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        view = window.GetVisualDescendants().OfType<RepositoriesView>().Single(); // the page is rebuilt when it is shown again
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var remoteList = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows);
        remoteList.SelectedIndex = 0; AppHarness.Layout(window);
        AppHarness.DoubleTap(window, remoteList.GetRealizedContainers().OfType<ListBoxItem>().First());
        Assert.True(model.Catalog.Clone.IsCloneDialogOpen); Assert.Same(group.Rows[0], model.Catalog.Clone.CloneSource);
        model.Catalog.Clone.CancelCloneCommand.Execute(null);
        window.Close();
    }

    // Enter does on a hosted repository what a double-click does, as it opens a local one: the clone dialog opens for a
    // repository not cloned yet.
    [AvaloniaFact]
    public async Task WhenEnterIsPressedOnAHostedRepository_ShouldDoWhatADoubleClickDoes()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var remoteList = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows);
        remoteList.SelectedIndex = 0; AppHarness.Layout(window);
        remoteList.GetRealizedContainers().OfType<ListBoxItem>().First().Focus();

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);

        Assert.True(model.Catalog.Clone.IsCloneDialogOpen); Assert.Same(group.Rows[0], model.Catalog.Clone.CloneSource);
        model.Catalog.Clone.CancelCloneCommand.Execute(null);
        window.Close();
    }

    // The menus bind through the list's UserControl.
    [AvaloniaFact]
    public async Task WhenARowMenuOpens_ShouldReachTheCatalogCommandsWithTheRowAsParameter()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Entries.Add(Entry("alpha"));
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);

        var localMenu = OpenMenu(window, view.Descendant<ListBox>("LocalRepositoriesList")!);
        Assert.Same(model.Catalog.RevealCommand, Item(localMenu, "Reveal in Explorer").Command);
        Assert.Same(model.Catalog.Local.LocalRows[0], Item(localMenu, "Reveal in Explorer").CommandParameter);
        Assert.Same(model.Catalog.OpenEntryCommand, Item(localMenu, "Open").Command);
        Assert.Same(model.Catalog.Local.RequestRemoveRepositoryCommand, Item(localMenu, "Remove from list").Command);

        var remoteMenu = OpenMenu(window, view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows));
        Assert.Same(model.Catalog.BeginCloneCommand, Item(remoteMenu, "Clone").Command);
        Assert.Same(group.Rows[0], Item(remoteMenu, "Clone").CommandParameter);
        Assert.Same(model.Catalog.OpenWebCommand, Item(remoteMenu, "View on GitHub").Command);
        window.Close();
    }

    // A long list scrolls with its bar beside the rows, at the edge of the column, not over the rows' ⋮ buttons.
    [AvaloniaFact]
    public async Task WhenTheListIsLongerThanThePage_ShouldScrollWithTheBarRightOfTheRowsActions()
    {
        var h = new AppHarness();
        for (var index = 0; index < 30; index++) h.Catalog.Entries.Add(Entry("repository" + index));
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var scroll = view.Descendant<ScrollViewer>("CatalogScroll")!;
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>().Single(bar => bar.TemplatedParent == scroll && bar.Orientation == Avalonia.Layout.Orientation.Vertical);
        var kebab = view.Descendant<ListBox>("LocalRepositoriesList")!.GetRealizedContainers().OfType<ListBoxItem>().First()
            .GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);

        Assert.True(bar.IsEffectivelyVisible);
        var kebabRight = kebab.TranslatePoint(new Point(kebab.Bounds.Width, 0), view)!.Value.X;
        Assert.True(bar.TranslatePoint(default, view)!.Value.X >= kebabRight, $"bar at {bar.TranslatePoint(default, view)!.Value.X}, actions end at {kebabRight}");
        window.Close();
    }

    private static MenuFlyout OpenMenu(Window window, ListBox list)
    {
        var kebab = list.GetRealizedContainers().OfType<ListBoxItem>().First().GetVisualDescendants().OfType<Button>().Single(button => button.Flyout is MenuFlyout);
        kebab.Flyout!.ShowAt(kebab); AppHarness.Layout(window);
        return (MenuFlyout)kebab.Flyout;
    }
    private static MenuItem Item(MenuFlyout menu, string header) => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    // The local repositories fold under the chevron at the right of their header and open again; the page keeps the
    // choice when it is rebuilt.
    [AvaloniaFact]
    public async Task WhenTheLocalFoldIsUsed_ShouldFoldTheListAndKeepItFoldedWhenThePageComesBack()
    {
        var h = new AppHarness();
        h.Catalog.Entries.Add(Entry("alpha"));
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        Assert.True(view.Descendant<ListBox>("LocalRepositoriesList")!.IsEffectivelyVisible);

        Use(window, view.Descendant<Button>("LocalFoldButton")!);
        Assert.False(view.Descendant<ListBox>("LocalRepositoriesList")!.IsEffectivelyVisible);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        Assert.False(view.Descendant<ListBox>("LocalRepositoriesList")!.IsEffectivelyVisible);
        Use(window, view.Descendant<Button>("LocalFoldButton")!);

        Assert.True(view.Descendant<ListBox>("LocalRepositoriesList")!.IsEffectivelyVisible);
        window.Close();
    }

    // An account's group offers the fold once it has repositories to show, and folds only its own list.
    [AvaloniaFact]
    public async Task WhenAnAccountGroupHasRepositories_ShouldOfferAFoldThatFoldsItsList()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Entries.Add(Entry("alpha"));
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var fold = view.Descendant<Button>("AccountFoldButton")!;
        Assert.False(fold.IsEffectivelyVisible);
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var remoteList = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows);
        Assert.True(remoteList.IsEffectivelyVisible);

        Use(window, fold);

        Assert.False(remoteList.IsEffectivelyVisible);
        Assert.True(view.Descendant<ListBox>("LocalRepositoriesList")!.IsEffectivelyVisible);
        window.Close();
    }

    // The whole header row folds and opens the list, not only its chevron; a right click, or a header whose search shows
    // no rows to fold, leaves it as it is.
    [AvaloniaFact]
    public async Task WhenTheLocalHeaderIsClickedAnywhere_ShouldFoldAndOpenTheList()
    {
        var h = new AppHarness();
        h.Catalog.Entries.Add(Entry("alpha"));
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var header = (Control)view.Descendant<Button>("LocalFoldButton")!.Parent!;
        var list = view.Descendant<ListBox>("LocalRepositoriesList")!;

        Click(window, header, new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), MouseButton.Right);
        model.Catalog.Filter.CatalogSearch = "nothing like it"; AppHarness.Layout(window);
        Click(window, header, new Point(header.Bounds.Width / 2, header.Bounds.Height / 2));
        model.Catalog.Filter.CatalogSearch = ""; AppHarness.Layout(window);
        Assert.True(list.IsEffectivelyVisible);
        Click(window, header, new Point(header.Bounds.Width / 2, header.Bounds.Height / 2));
        Assert.False(list.IsEffectivelyVisible);
        Click(window, header, new Point(4, header.Bounds.Height / 2));

        Assert.True(list.IsEffectivelyVisible);
        window.Close();
    }

    // An account's header folds its list wherever it is clicked once there are repositories to fold; its load button
    // only loads.
    [AvaloniaFact]
    public async Task WhenAnAccountHeaderIsClicked_ShouldFoldItsListAndLeaveTheLoadButtonToLoad()
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var header = (Panel)view.Descendant<Button>("AccountFoldButton")!.Parent!;
        var title = header.Children.OfType<TextBlock>().Single();
        var group = Assert.Single(model.Catalog.AccountGroups);

        Click(window, title, new Point(4, title.Bounds.Height / 2));
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var remoteList = view.GetVisualDescendants().OfType<ListBox>().Single(list => list.ItemsSource == group.VisibleRows);
        Assert.True(remoteList.IsEffectivelyVisible);
        Use(window, header.Children.OfType<Button>().Single(button => button.Command == group.LoadCommand));
        await (group.LoadCommand.ExecutionTask ?? Task.CompletedTask); AppHarness.Layout(window);
        Click(window, title, new Point(4, title.Bounds.Height / 2), MouseButton.Right);
        Assert.True(remoteList.IsEffectivelyVisible);
        Click(window, title, new Point(4, title.Bounds.Height / 2));

        Assert.False(remoteList.IsEffectivelyVisible);
        window.Close();
    }

    // A full list is checked a few links at a time while it stays in use: every row is there from the start, the search
    // narrows it and a row is chosen with the mouse while the check runs, and each row says what its check found after.
    [AvaloniaFact]
    public async Task WhenRefreshRunsOverFiveHundredEntries_ShouldStayResponsive()
    {
        var h = new AppHarness();
        for (var index = 0; index < RepositoryEntry.MaximumListed; index++) h.Catalog.Entries.Add(Entry("repository" + index));
        var links = new TaskCompletionSource();
        h.Catalog.ReadProbe = async (path, _) => { await links.Task; return new(RepositoryLinkStatus.Valid, "main", null, path); };
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        Assert.Equal(RepositoryEntry.MaximumListed, model.Catalog.Local.LocalRows.Count);

        var search = view.Descendant<TextBox>("CatalogSearchEditor")!;
        Click(window, search, new Point(search.Bounds.Width / 2, search.Bounds.Height / 2));
        window.KeyTextInput("repository42"); AppHarness.Layout(window);
        var row = view.Descendant<ListBox>("LocalRepositoriesList")!.GetRealizedContainers().OfType<ListBoxItem>().First();
        Click(window, row, new Point(row.Bounds.Width / 3, row.Bounds.Height / 2));

        Assert.True(model.Catalog.State.IsBusy);
        Assert.Equal(11, model.Catalog.Filter.VisibleLocalRows.Count);
        Assert.Same(model.Catalog.Filter.VisibleLocalRows[0], model.Catalog.State.SelectedLocal);
        links.SetResult();
        await AppHarness.Until(() => !model.Catalog.State.IsBusy, "the links to be checked");
        Assert.All(model.Catalog.Local.LocalRows, checkedRow => Assert.Equal("Valid", checkedRow.StatusText));
        window.Close();
    }

    // A link the check found broken does not open on a double-click; the picker's message says why instead.
    [AvaloniaFact]
    public async Task WhenAnEntryIsMissing_ShouldNotOpenAndSayWhyOnDoubleClick()
    {
        var h = new AppHarness();
        h.Catalog.Entries.Add(Entry("gone"));
        h.Catalog.Probes[Path.Combine(Root, "gone")] = new(RepositoryLinkStatus.Missing);
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var locals = view.Descendant<ListBox>("LocalRepositoriesList")!;
        locals.SelectedIndex = 0; AppHarness.Layout(window);

        AppHarness.DoubleTap(window, locals.GetRealizedContainers().OfType<ListBoxItem>().First());
        await (model.Catalog.OpenEntryCommand.ExecutionTask ?? Task.CompletedTask); AppHarness.Layout(window);

        Assert.False(model.Repository.HasRepository);
        Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section);
        Assert.Equal("Folder not found · move it back or remove this entry", model.Catalog.State.Message);
        window.Close();
    }

    // A real click in the middle of a button.
    private static void Use(Window window, Button button)
    {
        Assert.True(button.IsEffectivelyVisible);
        Click(window, button, new Point(button.Bounds.Width / 2, button.Bounds.Height / 2));
    }

    private static void Click(Window window, Visual target, Point at, MouseButton button = MouseButton.Left)
    {
        var point = target.TranslatePoint(at, window)!.Value;
        window.MouseDown(point, button); window.MouseUp(point, button); AppHarness.Layout(window);
    }

    // The bar under the search runs in a room of its own, a little apart from the list, so the list keeps its place when
    // the catalog is read.
    [AvaloniaFact]
    public async Task WhenTheCatalogFinishesReading_ShouldKeepTheListWhereItWas()
    {
        var h = new AppHarness();
        h.Catalog.Entries.Add(Entry("alpha"));
        var link = new TaskCompletionSource<RepositoryProbe>();
        h.Catalog.ReadProbe = (_, _) => link.Task;
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); AppHarness.Layout(window);
        var view = window.GetVisualDescendants().OfType<RepositoriesView>().Single();
        var list = view.Descendant<ScrollViewer>("CatalogScroll")!;
        var bar = view.GetVisualDescendants().OfType<ProgressBar>().Single();
        var reading = (bar.IsEffectivelyVisible, bar.TranslatePoint(new(0, bar.Bounds.Height), view)!.Value.Y, list.TranslatePoint(default, view)!.Value.Y);

        link.SetResult(new(RepositoryLinkStatus.Valid, "main", null, Path.Combine(Root, "alpha")));
        await AppHarness.Until(() => !model.Catalog.State.IsBusy, "the catalog"); AppHarness.Layout(window);

        Assert.True(reading.Item1);
        Assert.True(reading.Item3 - reading.Item2 >= 4, $"the list starts {reading.Item3 - reading.Item2} under the bar");
        Assert.Equal(reading.Item3, list.TranslatePoint(default, view)!.Value.Y);
        window.Close();
    }
}
