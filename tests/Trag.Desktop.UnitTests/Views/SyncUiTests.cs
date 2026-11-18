using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Controls;
using Trag.Desktop.Platform;
using Trag.Desktop.Views;
using Trag.Desktop.Views.Pickers;
using Trag.Domain.Accounts;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

public sealed class SyncUiTests
{
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task WhenTheChosenRemotesAddressChanges_ShouldKeepTheChosenRemoteAndBranchOnRefresh(bool hasUpstream, bool multipleRemotes)
    {
        var backend = Backend();
        if (!multipleRemotes) backend.Snapshot = backend.Snapshot with { Remotes = [backend.Snapshot.Remotes[0]] };
        await using var model = ShellHarness.Compose(backend, dispatcher: new ViewDispatcher());
        var window = new MainWindow { DataContext = model };
        try
        {
            window.Show();
            await model.Session.OpenRepositoryAsync("fixture");
            await model.Sync.SyncCompletion;
            AppHarness.Layout(window);
            model.Navigator.ShowHistoryCommand.Execute(null);
            model.Layout.ThemeIndex = 2;
            if (!hasUpstream) backend.Snapshot = backend.Snapshot with { TrackingRemote = null, UpstreamBranchRef = null };
            await model.CloseRepositoryCommand.ExecuteAsync(null);
            await model.Session.OpenRepositoryAsync("fixture");
            await model.Sync.SyncCompletion;
            AppHarness.Layout(window);
            var remotes = window.Descendant<Button>("SyncRemoteSelector")!;
            var branches = window.Descendant<Button>("SyncBranchSelector")!;
            if (!hasUpstream)
            {
                model.Sync.ChooseRemoteCommand.Execute(model.Sync.Selection.RemoteRows[0]);
                await model.Sync.SyncCompletion;
                AppHarness.Layout(window);
                model.Sync.ChooseBranchCommand.Execute(model.Sync.Selection.Branches[0]);
                await model.Sync.SyncCompletion;
                AppHarness.Layout(window);
            }
            var before = backend.SyncReads.Count;
            backend.Snapshot = backend.Snapshot with
            {
                Head = new string('c', 40),
                Remotes = backend.Snapshot.Remotes.Select(remote => remote.Name == "origin" ? remote with { PushAddress = "changed-push" } : remote).ToArray()
            };
            backend.SyncGate.IsClosed = true;

            await model.RefreshCommand.ExecuteAsync(null);
            AppHarness.Layout(window);
            backend.SyncGate.Open();
            await model.Sync.SyncCompletion;
            AppHarness.Layout(window);
            if (multipleRemotes) Assert.Equal("origin", Label(remotes));
            Assert.Equal("origin", model.Sync.Selection.Remote?.Name);
            Assert.Equal("changed-push", model.Sync.Selection.Remote?.PushAddress);
            Assert.Equal("refs/heads/trunk", model.Sync.Selection.SelectedBranch?.Branch.SourceRef);
            Assert.Equal(model.Sync.Selection.SelectedBranch!.Label, Label(branches));
            Assert.EndsWith("changed-push", model.Sync.Panel.Target.PushDestination);
            Assert.True(backend.SyncReads.Count == before + 1, string.Join(" | ", backend.SyncReads));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WhenTheSyncPanelIsReplaced_ShouldUpdateCountsAccountsAndCommandsWithoutReadingAgain()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend, dispatcher: new ViewDispatcher());
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        Assert.Single(backend.SyncReads);
        var push = window.Descendant<ToolButton>("PushButton")!;
        var accounts = window.Descendant<ComboBox>("SyncAccountSelector")!;
        var save = window.Descendant<Button>("SaveSyncAccountButton")!;
        Assert.Equal("Push · 1", push.Text);
        Assert.True(push.Pending);
        Assert.Contains("highlighted", push.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("iconTile")).Classes);
        Assert.Equal("alice", ((AccountChoice)accounts.SelectedItem!).Id);
        Assert.False(save.IsEffectivelyVisible);

        await model.RefreshCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.Equal("Push · 2", push.Text);
        Assert.Same(model.Sync.Panel.Accounts.Selected, accounts.SelectedItem);
        accounts.SelectedItem = model.Sync.Panel.Accounts.Items[1];
        AppHarness.Layout(window);
        Assert.True(save.IsEffectivelyVisible);
        Assert.False(push.IsEffectivelyEnabled);
        Assert.False(window.Descendant<ToolButton>("FetchButton")!.IsEffectivelyEnabled);

        await model.Sync.SaveSyncAccountCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        Assert.Equal(3, backend.SyncReads.Count);
        Assert.Equal("bob", ((AccountChoice)accounts.SelectedItem!).Id);
        Assert.False(save.IsEffectivelyVisible);
        Assert.True(push.IsEffectivelyEnabled);
        Assert.Equal("Push · 3", push.Text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheSyncBranchChanges_ShouldClearTheOldPresentationAndApplyOneConsistentResult()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend, dispatcher: new ViewDispatcher());
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        backend.SyncGate.IsClosed = true;
        var branches = window.Descendant<Button>("SyncBranchSelector")!;
        model.Sync.ChooseBranchCommand.Execute(model.Sync.Selection.Branches[1]);
        await backend.SyncGate.Arrival(2);
        AppHarness.Layout(window);
        Assert.Equal("Push", window.Descendant<ToolButton>("PushButton")!.Text);
        Assert.False(window.Descendant<TextBlock>("SyncPushWarningText")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<ToolButton>("PushButton")!.IsEffectivelyEnabled);

        backend.SyncGate.Open();
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        Assert.Equal(2, backend.SyncReads.Count);
        Assert.Equal("shared", Label(branches));
        Assert.Equal("shared", model.Sync.Selection.SelectedBranch!.Branch.Display);
        var warning = window.Descendant<TextBlock>("SyncPushWarningText")!;
        Assert.True(warning.IsEffectivelyVisible);
        Assert.Equal("Push sends trunk into origin/shared.", warning.Text);
        Assert.Equal(model.Sync.Panel.Target.PushDestination, ToolTip.GetTip(window.Descendant<ToolButton>("PushButton")!));
        window.Close();
    }

    // The upstream branch is a picker like the current branch's: the chosen row checked, a search with Enter, and a choice
    // that closes it and shows on the button.
    [AvaloniaFact]
    public async Task WhenAnUpstreamBranchIsChosenInItsPicker_ShouldShowItOnTheButtonAndCloseThePicker()
    {
        var backend = Backend();
        await using var model = ShellHarness.Compose(backend, dispatcher: new ViewDispatcher());
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        var button = window.Descendant<Button>("SyncBranchSelector")!;
        Assert.Equal("trunk", Label(button));

        button.Flyout!.ShowAt(button); AppHarness.Layout(window);
        var picker = Assert.IsType<UpstreamPicker>(Assert.IsType<Flyout>(button.Flyout).Content);
        AppHarness.Layout(window);
        Assert.True(model.Sync.IsUpstreamPickerOpen);
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Choose the upstream branch");
        Assert.Equal([("trunk", true, true), ("shared", false, false)], Rows(picker).Select(row => (Label(row), Checked(row), row.Classes.Contains("current"))));
        var search = picker.Descendant<TextBox>("UpstreamSearchBox")!;
        Assert.True(search.IsFocused);
        search.Text = "sha"; AppHarness.Layout(window);
        var shared = Assert.Single(Rows(picker));
        shared.Command!.Execute(shared.CommandParameter); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.False(button.Flyout.IsOpen);
        Assert.Equal("shared", Label(button));

        button.Flyout.ShowAt(button); AppHarness.Layout(window);
        Assert.Equal("", search.Text);
        search.Text = "tru"; search.Focus();
        TopLevel.GetTopLevel(search)!.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.False(button.Flyout.IsOpen);
        Assert.Equal("trunk", Label(button));

        model.Sync.Selection.RemoteName = "backup"; await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.Equal("Choose branch", Label(button));
        backend.Gate.IsClosed = true;
        var fetch = model.Sync.FetchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.False(button.IsEffectivelyEnabled);
        backend.Gate.Open(); await fetch;
        window.Close();
    }

    // With several remotes the remote is a picker too: the chosen one checked, and a choice that closes it. The branch
    // tracks a remote that was removed, so none is chosen yet.
    [AvaloniaFact]
    public async Task WhenARemoteIsChosenInItsPicker_ShouldShowItOnTheButtonAndCloseThePicker()
    {
        var backend = Backend();
        backend.Snapshot = backend.Snapshot with { TrackingRemote = "gone" };
        await using var model = ShellHarness.Compose(backend, dispatcher: new ViewDispatcher());
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        AppHarness.Layout(window);
        var button = window.Descendant<Button>("SyncRemoteSelector")!;
        Assert.Equal("Choose remote", Label(button));

        button.Flyout!.ShowAt(button); AppHarness.Layout(window);
        var picker = Assert.IsType<RemotePicker>(Assert.IsType<Flyout>(button.Flyout).Content);
        AppHarness.Layout(window);
        Assert.True(model.Sync.IsRemotePickerOpen);
        Assert.Contains(picker.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Choose the remote");
        Assert.Equal([("origin", false, false), ("backup", false, false)], Rows(picker).Select(row => (Label(row), Checked(row), row.Classes.Contains("current"))));
        var backup = Rows(picker)[1];
        backup.Command!.Execute(backup.CommandParameter); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.False(button.Flyout.IsOpen);
        Assert.Equal("backup", Label(button));

        button.Flyout.ShowAt(button); AppHarness.Layout(window);
        Assert.Equal([("origin", false, false), ("backup", true, true)], Rows(picker).Select(row => (Label(row), Checked(row), row.Classes.Contains("current"))));
        button.Flyout.Hide();
        backend.Gate.IsClosed = true;
        var fetch = model.Sync.FetchCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Assert.False(button.IsEffectivelyEnabled);
        backend.Gate.Open(); await fetch;
        window.Close();
    }

    private static Button[] Rows(UserControl picker) => [.. picker.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("branch"))];

    private static string? Label(Button button) => button.GetVisualDescendants().OfType<TextBlock>().First().Text;

    private static bool Checked(Button row) => row.GetVisualDescendants().OfType<LineIcon>().Single(icon => icon.Kind == "check").IsEffectivelyVisible;

    private static FakeSyncWorkspace Backend()
    {
        var account = "alice";
        var backend = new FakeSyncWorkspace
        {
            Snapshot = Snapshots.Create(branch: "trunk", trackingRemote: "origin", upstreamBranchRef: "refs/heads/trunk",
                remotes: [new("origin", "fetch", "push"), new("backup", "backup-fetch", "backup-push")])
        };
        backend.ReadSync = (snapshot, remote, selected) =>
        {
            SyncBranch[] branches = [new("refs/heads/trunk", "refs/remotes/" + remote + "/trunk"), new("refs/heads/shared", "refs/remotes/" + remote + "/shared")];
            var source = selected ?? (snapshot.TrackingRemote == remote ? snapshot.UpstreamBranchRef : null);
            var branch = branches.FirstOrDefault(item => item.SourceRef == source);
            var address = snapshot.Remotes.Single(item => item.Name == remote);
            return new(new(remote, address.FetchAddress, address.PushAddress, "config"), branches, branch, backend.SyncReads.Count, 0, "Local comparison")
            {
                Accounts = new([new("alice", "Alice"), new("bob", "Bob")], account, "Using " + account)
            };
        };
        backend.Handle = request =>
        {
            if (request is SyncRequest { Action: SyncAction.SelectAccount, AccountId: { } chosen }) account = chosen;
            return new(GitWriteOutcome.Succeeded, "Synced");
        };
        return backend;
    }
}
