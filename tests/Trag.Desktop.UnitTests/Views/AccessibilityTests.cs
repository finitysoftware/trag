using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.UnitTests.Dialogs.Settings;
using Trag.Domain.Blame;
using Trag.Domain.Catalog;
using Trag.Domain.Diff;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Views;

// What a screen reader is told about the controls the keyboard reaches: each has a name of its own, never the type of the
// panel inside it or the dump of the row it shows.
public sealed class AccessibilityTests
{
    [AvaloniaFact]
    public async Task WhenEveryViewAndDialogIsShown_ShouldNameEveryControlTheKeyboardReaches()
    {
        var unnamed = new List<string>();
        await RepositoriesAsync(unnamed);
        await WorkspaceAsync(unnamed);
        await RewriteAsync(unnamed);
        await OperationAsync(unnamed, "merge", Snapshots.Create(branch: "trunk", head: "head", operation: RepositoryOperation.Merge,
            files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")], merge: new("incoming", "head", "Merge message", "merge", "")));
        await OperationAsync(unnamed, "rebase", Snapshots.Rebasing(Snapshots.Rebase(step: 2, total: 3), [Snapshots.Conflict("f.cs")]));
        await OperationAsync(unnamed, "revert", Snapshots.Create(operation: RepositoryOperation.Revert, files: [Snapshots.Conflict("a.txt")]) with { PickHead = Snapshots.Id('b') });
        await OperationAsync(unnamed, "stash conflicts", Snapshots.Create(operation: RepositoryOperation.Conflict, files: [Snapshots.Conflict("a.txt"), Snapshots.Staged("b.txt")]));
        await SettingsAsync(unnamed);

        Assert.True(unnamed.Count == 0, string.Join(Environment.NewLine, unnamed));
    }

    [AvaloniaFact]
    public async Task WhenADialogOpens_ShouldPresentItToAScreenReaderAsAWindowNamedByItsTitle()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var dialog = window.Descendant<DialogHost>("CreateBranchOverlay")!;
        dialog.IsOpen = true; AppHarness.Layout(window);

        var peer = ControlAutomationPeer.CreatePeerForElement(dialog);

        Assert.Equal((AutomationControlType.Window, dialog.Title), (peer.GetAutomationControlType(), peer.GetName()));
        window.Close();
    }

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "trag-accessibility-ui");

    private static async Task RepositoriesAsync(List<string> unnamed)
    {
        var h = new AppHarness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Entries.Add(new(Path.Combine(Root, "alpha"), "alpha", DateTimeOffset.UtcNow.AddDays(-1)));
        h.Catalog.Probes[Path.Combine(Root, "alpha")] = new(RepositoryLinkStatus.Valid, "main", "https://github.com/acme/alpha.git", Path.Combine(Root, "alpha"));
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow),
            new("acme/alpha", "https://github.com/acme/alpha.git", "https://github.com/acme/alpha", "main", false, DateTimeOffset.UtcNow)];
        await using var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        Collect(unnamed, "repositories", window);
        window.Descendant<ListBox>("LocalRepositoriesList")!.SelectedIndex = 0; AppHarness.Layout(window);
        Collect(unnamed, "local repository", window);
        var group = model.Catalog.AccountGroups.Single();
        await group.LoadCommand.ExecuteAsync(null); AppHarness.Layout(window);
        foreach (var row in group.Rows)
        {
            group.SelectedRow = row; AppHarness.Layout(window);
            Collect(unnamed, row.FullName, window);
        }
        window.Close();
    }

    // History shown and an older commit's rewrite dialog open over it, with a row for each commit.
    private static async Task RewriteAsync(List<string> unnamed)
    {
        var h = new AppHarness();
        var older = Snapshots.Id('b');
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", older), Snapshots.Commit(older, "Older commit")], false, page);
        h.Branches.Rewrites[older] = new(Snapshots.Id('c'),
            [new(older, "Older commit", "Older commit", RewriteAction.Pick, "Older commit"),
             new(Snapshots.HeadId, "Update image", "Update image", RewriteAction.Pick, "Update image")], 0);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture");
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[1]; await model.History.Details.Completion;
        await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null); AppHarness.Layout(window);
        Collect(unnamed, "rewrite", window.Descendant<DialogHost>("RewriteOverlay")!);
        window.Close();
    }

    // File status with a diff, the three pickers, history with a commit's files, search, blame, then every dialog of the window.
    private static async Task WorkspaceAsync(List<string> unnamed)
    {
        var h = new AppHarness();
        var stash = h.Stashes.Add("On main: spike");
        h.Branches.Tags.Add(new("v1.9", Snapshots.Id('b'), "Initial"));
        h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt"), Snapshots.Untracked("b.txt"), Snapshots.Staged("c.txt")], stashHead: stash.Id);
        h.Diff.Diff = (_, _, _) => new([], [new(' ', " a", 1, 1), new('-', "-b", 2), new('+', "+B", null, 2)], DiffContentKind.Text, "");
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image")], false, page);
        h.History.Details = id => new(Snapshots.Commit(id, "Update image"), "Body",
            [new FileDiffMetadata(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'))]);
        var commit = new BlameCommit(Snapshots.Id('a'), "Ana", DateTimeOffset.UtcNow, "Add a", Snapshots.Path("a.txt"));
        h.History.Blames = (_, _) => new([new(1, "one", commit)]);
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Stash.List.Completion; AppHarness.Layout(window);
        window.Descendant<ListBox>("UnstagedList")!.SelectedIndex = 0;
        await model.WorkingDiff.Diff.Completion; AppHarness.Layout(window);
        Collect(unnamed, "file status", window);

        foreach (var picker in new[] { "BranchButton", "StashPickerButton", "TagPickerButton" })
        {
            var button = window.Descendant<Button>(picker)!;
            button.Flyout!.ShowAt(button); AppHarness.Layout(window);
            Collect(unnamed, picker, window);
            button.Flyout.Hide(); AppHarness.Layout(window);
        }

        model.Navigator.ShowHistoryCommand.Execute(null); AppHarness.Layout(window);
        await AppHarness.Until(() => window.Descendant<ListBox>("HistoryList")!.ItemCount > 0, "the history page");
        window.Descendant<ListBox>("HistoryList")!.SelectedIndex = 0;
        await AppHarness.Until(() => window.Descendant<ListBox>("CommitFilesList") is { ItemCount: > 0 }, "the commit's files");
        AppHarness.Layout(window);
        Collect(unnamed, "history", window);
        model.Navigator.ShowSearchCommand.Execute(null); AppHarness.Layout(window);
        Collect(unnamed, "search", window);

        await model.Blame.OpenAsync("a.txt", null); AppHarness.Layout(window);
        Collect(unnamed, "blame", window);
        model.Blame.CloseCommand.Execute(null); AppHarness.Layout(window);

        foreach (var dialog in window.GetVisualDescendants().OfType<DialogHost>().ToList())
        {
            dialog.IsOpen = true; AppHarness.Layout(window);
            Collect(unnamed, dialog.Name!, dialog);
            dialog.IsOpen = false; AppHarness.Layout(window);
        }
        window.Close();
    }

    private static async Task OperationAsync(List<string> unnamed, string state, RepositorySnapshot snapshot)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = snapshot;
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        Collect(unnamed, state, window);
        window.Close();
    }

    private static async Task SettingsAsync(List<string> unnamed)
    {
        var settings = SettingsHarness.Window(new FakeAccountService()); settings.Show(); await settings.Initialization; AppHarness.Layout(settings);
        foreach (var section in new[] { "IntegrationsSection", "GitSection", "AppearanceSection", "AboutSection" })
        {
            SettingsHarness.Choose(settings, section);
            Collect(unnamed, section, settings);
        }
        foreach (var dialog in settings.GetVisualDescendants().OfType<DialogHost>().ToList())
        {
            dialog.IsOpen = true; AppHarness.Layout(settings);
            Collect(unnamed, dialog.Name!, dialog);
            dialog.IsOpen = false; AppHarness.Layout(settings);
        }
        settings.Close();
    }

    // A control the keyboard can reach inside the root whose name is empty, or is only what ToString makes of the object it
    // shows. Text a reader reads as itself, such as a selectable path, names itself. Open flyouts are walked with the window.
    // A dialog's own name is its title, which only its subject gives it, so the dialog test holds that.
    private static void Collect(List<string> unnamed, string state, Visual root)
    {
        foreach (var control in Controls(root).OfType<Control>().Where(control => control.IsEffectivelyVisible && control.Focusable && control is not TextBlock))
        {
            var name = ControlAutomationPeer.CreatePeerForElement(control).GetName();
            if (string.IsNullOrWhiteSpace(name) || control is ContentControl { Content: { } content and not string } && name == content.ToString())
                unnamed.Add($"{state}: {control.GetType().Name} {control.Name} \"{name}\"");
        }
    }

    private static IEnumerable<Visual> Controls(Visual root)
    {
        foreach (var visual in root.GetVisualDescendants())
        {
            yield return visual;
            if (visual is Popup { IsOpen: true, Child: { } child })
                foreach (var inner in Controls(child).Prepend(child)) yield return inner;
        }
    }
}
