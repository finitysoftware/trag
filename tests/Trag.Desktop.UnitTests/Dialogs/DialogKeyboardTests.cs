using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.UnitTests.Dialogs.Settings;
using Trag.Domain.Accounts;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;
using Trag.Domain.Rebase;
using Trag.Domain.Setup;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Status;

namespace Trag.Desktop.UnitTests.Dialogs;

// Every dialog from the keyboard alone: it opens with the keyboard on the control it names (or on itself when it names
// none), Tab and Shift+Tab go round its own controls and never reach the window behind it, Escape closes it, and Enter as
// it opens does its default: the action of the form it opened on, Cancel where the question's Cancel has the keyboard, or
// nothing where nothing is typed yet to act on.
public sealed class DialogKeyboardTests
{
    public enum EnterDoes { Confirms, Cancels, Nothing }

    private static readonly string Older = Snapshots.Id('b');

    private static readonly PatchLine[] TwoChanges =
    [
        new(' ', " a", 1, 1), new(' ', " b", 2, 2), new(' ', " c", 3, 3), new('-', "-d", 4), new('+', "+D", null, 4), new(' ', " e", 5, 5),
        new('-', "-f", 6), new('+', "+F", null, 6), new(' ', " g", 7, 7)
    ];

    private sealed record Case(string? Focus, EnterDoes Enter, Func<Task<Opened>> Open);

    private sealed record Opened(Window Window, DialogHost Dialog, Func<bool> Confirmed, IAsyncDisposable? Model, AppHarness? Harness = null) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Window.Close();
            if (Model is not null) await Model.DisposeAsync();
        }
    }

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["CreateBranchOverlay"] = new("NewBranchNameEditor", EnterDoes.Confirms, () => InWindow("CreateBranchOverlay", async (_, model, _) =>
            await model.Branches.OpenCreateBranchCommand.ExecuteAsync(null), typed: ("NewBranchNameEditor", "feature/ui"))),
        ["RenameBranchOverlay"] = new("RenameBranchEditor", EnterDoes.Confirms, () => InWindow("RenameBranchOverlay", async (_, model, _) =>
        {
            await model.Branches.List.Completion;
            model.Branches.RequestRenameCommand.Execute(model.Branches.Rows.Local.Single(row => row.Name == "topic"));
        }, typed: ("RenameBranchEditor", "renamed"))),
        ["CreateTagOverlay"] = new("NewTagNameEditor", EnterDoes.Confirms, () => InWindow("CreateTagOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 0);
            model.Tags.OpenCreateTagCommand.Execute(null);
        }, typed: ("NewTagNameEditor", "v1.0"))),
        ["RevertOverlay"] = new("CancelRevertButton", EnterDoes.Cancels, () => InWindow("RevertOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 1);
            model.Revert.RequestRevertCommand.Execute(null);
        })),
        ["CherryPickOverlay"] = new("CancelCherryPickButton", EnterDoes.Cancels, () => InWindow("CherryPickOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 1);
            await model.CherryPick.RequestCherryPickCommand.ExecuteAsync(null);
        })),
        ["ResetOverlay"] = new("CancelResetButton", EnterDoes.Cancels, () => InWindow("ResetOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 1);
            await model.Reset.RequestResetCommand.ExecuteAsync(null);
        })),
        ["RewriteOverlay"] = new("RewriteRows", EnterDoes.Nothing, () => InWindow("RewriteOverlay", async (_, model, _) =>
        {
            await model.Sync.SyncCompletion;
            await ChosenAsync(model, 1);
            await model.Rewrite.RequestRewriteCommand.ExecuteAsync(null);
        })),
        ["RemotesOverlay"] = new(null, EnterDoes.Nothing, () => InWindow("RemotesOverlay", (_, model, _) =>
        {
            model.RemoteSettings.OpenCommand.Execute(null);
            return Task.CompletedTask;
        }, arrange: h => h.Reader.Snapshot = h.Synced())),
        ["RepositoryDetailsOverlay"] = new(null, EnterDoes.Nothing, () => InWindow("RepositoryDetailsOverlay", async (_, model, _) =>
        {
            await model.RepositoryDetails.OpenCommand.ExecuteAsync(null);
            await model.RepositoryDetails.Details.Completion;
        })),
        ["PublishOverlay"] = new("PublishNameEditor", EnterDoes.Confirms, () => InWindow("PublishOverlay", async (_, model, _) =>
        {
            await model.Sync.SyncCompletion;
            await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        }, arrange: h => h.Catalog.Accounts.Add(FakeAccountService.GitHub(user: "contoso")))),
        ["DeleteBranchOverlay"] = new("CancelDeleteBranchButton", EnterDoes.Cancels, () => InWindow("DeleteBranchOverlay", async (_, model, _) =>
        {
            await model.Branches.List.Completion;
            model.Branches.Delete.Request(model.Branches.Rows.Local.Single(row => row.Name == "topic"));
        })),
        ["DeleteTagOverlay"] = new("CancelDeleteTagButton", EnterDoes.Cancels, () => InWindow("DeleteTagOverlay", async (_, model, _) =>
        {
            await model.Tags.List.Completion;
            model.Tags.RequestDeleteCommand.Execute(model.Tags.Visible[0]);
        }, arrange: h =>
        {
            h.Branches.Tags.Add(new("v1.9", Older, "Initial"));
            h.Reader.Snapshot = h.Synced() with { TagRefs = "one" };
        })),
        ["GoToCommitOverlay"] = new("GoToBranchNameEditor", EnterDoes.Confirms, () => InWindow("GoToCommitOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 1);
            await model.GoTo.GoToSelectedCommitCommand.ExecuteAsync(null);
        })),
        ["KeepCommitsOverlay"] = new("KeepBranchNameEditor", EnterDoes.Confirms, () => InWindow("KeepCommitsOverlay", async (_, model, _) =>
        {
            await model.Branches.OpenBranchPickerCommand.ExecuteAsync(null);
            await model.Branches.SwitchToBranchCommand.ExecuteAsync(model.Branches.Rows.Local.Single(row => row.Name == "topic"));
        }, arrange: h => h.Reader.Snapshot = Snapshots.Create(branch: null, detachedCommits: 2))),
        ["MergeBranchesOverlay"] = new("MergeFromSelector", EnterDoes.Nothing, () => InWindow("MergeBranchesOverlay", async (_, model, _) =>
        {
            await model.MergeDialog.OpenCommand.ExecuteAsync(null);
            await model.Branches.List.Completion;
        })),
        ["StashOverlay"] = new("StashMessageEditor", EnterDoes.Confirms, () => InWindow("StashOverlay", (_, model, _) =>
        {
            model.Stash.OpenStashCommand.Execute(null);
            return Task.CompletedTask;
        }, arrange: Changed)),
        ["DropStashOverlay"] = new("CancelDropStashButton", EnterDoes.Cancels, () => InWindow("DropStashOverlay", async (_, model, _) =>
        {
            await model.Stash.List.Completion;
            model.Stash.RequestDropCommand.Execute(model.Stash.Rows[0]);
        }, arrange: h =>
        {
            h.Stashes.Add("On main: half-done login form");
            h.Reader.Snapshot = Snapshots.Create(stashHead: h.Stashes.Head);
        })),
        ["DiscardOverlay"] = new("CancelDiscardButton", EnterDoes.Cancels, () => InWindow("DiscardOverlay", (_, model, window) =>
        {
            window.Descendant<ListBox>("UnstagedList")!.SelectedItems!.Add(model.FileStatus.UnstagedFiles[0]);
            model.Discard.DiscardSelectedCommand.Execute(null);
            return Task.CompletedTask;
        }, arrange: Changed)),
        ["DiscardHunkOverlay"] = new("CancelDiscardHunkButton", EnterDoes.Cancels, () => InWindow("DiscardHunkOverlay", async (_, model, _) =>
        {
            model.FileStatus.SelectedUnstaged = (StatusRow)model.FileStatus.UnstagedFiles[0];
            await model.WorkingDiff.Diff.Completion;
            model.WorkingDiff.DiscardHunkCommand.Execute(new HunkChoice(TwoChanges, [3, 4]));
        }, arrange: h =>
        {
            Changed(h);
            h.Diff.Diff = (_, _, _) => new([], TwoChanges, DiffContentKind.Text, "");
        })),
        ["RestoreFileOverlay"] = new("CancelRestoreFileButton", EnterDoes.Cancels, () => InWindow("RestoreFileOverlay", async (_, model, _) =>
        {
            await ChosenAsync(model, 0);
            await model.Files.RequestRestoreCommand.ExecuteAsync(model.History.Details.Panel.Files[0]);
        }, arrange: h =>
        {
            Changed(h);
            h.History.Details = id => new(Snapshots.Commit(id, "Change"), "Body",
                [new(Snapshots.Path("a.txt"), "M", "100644", "100644", Snapshots.Id('1'), Snapshots.Id('2'))]);
        })),
        ["CloneDialog"] = new("CloneTargetEditor", EnterDoes.Nothing, Clone),
        ["GitSetupOverlay"] = new(null, EnterDoes.Nothing, GitSetup),
        ["GitLabEntryOverlay"] = new("GitLabTokenEditor", EnterDoes.Nothing, GitLabEntry),
        ["DeviceLoginOverlay"] = new(null, EnterDoes.Nothing, DeviceLogin),
    };

    public static TheoryData<string> Dialogs => [.. Cases.Keys];

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public async Task WhenADialogOpens_ShouldFocusItsControlKeepTabAndShiftTabInsideAndCloseOnEscape(string name)
    {
        await using var opened = await Cases[name].Open();
        var (window, dialog) = (opened.Window, opened.Dialog);

        Assert.Equal(Cases[name].Focus ?? dialog.Name, (Focused(window) as Control)?.Name);

        var cycle = new List<IInputElement>();
        for (var step = 0; step < 60; step++)
        {
            Press(window, Key.Tab, PhysicalKey.Tab);
            var focused = Focused(window);
            Assert.True(Inside(dialog, focused), $"Tab took the keys from {name} to {(focused as Control)?.Name ?? focused?.GetType().Name}");
            if (cycle.Count != 0 && focused == cycle[0]) break;
            cycle.Add(focused!);
        }
        Assert.True(cycle.Count > 1 && Focused(window) == cycle[0], $"Tab did not come round in {name}");
        // A list is one stop, and Shift+Tab goes back into it, to the control in its row that had the keys there.
        Press(window, Key.Tab, PhysicalKey.Tab, RawInputModifiers.Shift);
        Assert.True(Focused(window) is Visual back && (back == cycle[^1] || ((Visual)cycle[^1]).IsVisualAncestorOf(back)), $"Shift+Tab did not go back in {name}");

        Press(window, Key.Escape, PhysicalKey.Escape);
        Assert.False(dialog.IsOpen, $"Escape left {name} open");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Dialogs))]
    public async Task WhenEnterIsPressedAsADialogOpens_ShouldDoItsDefaultOrNothing(string name)
    {
        await using var opened = await Cases[name].Open();
        var (window, dialog) = (opened.Window, opened.Dialog);

        Press(window, Key.Enter, PhysicalKey.Enter);

        switch (Cases[name].Enter)
        {
            case EnterDoes.Confirms:
                await AppHarness.Until(opened.Confirmed, "Enter to confirm " + name);
                break;
            case EnterDoes.Cancels:
                await AppHarness.Until(() => !dialog.IsOpen, "Enter to cancel " + name);
                Assert.False(opened.Confirmed());
                break;
            default:
                await Task.Delay(50); AppHarness.Layout(window);
                Assert.True(dialog.IsOpen, $"Enter closed {name}");
                Assert.False(opened.Confirmed());
                Assert.True(Inside(dialog, Focused(window)), $"Enter took the keys out of {name}");
                break;
        }
    }

    // A write that runs while a dialog is open (a push, a fetch after adding a remote, a write the dialog under it started)
    // keeps each of the dialog's own actions back until it ends, and Cancel still closes the dialog.
    public static TheoryData<string, string, string> WriteGuarded => new()
    {
        { "CreateBranchOverlay", "ConfirmCreateBranchButton", "CancelCreateBranchButton" },
        { "RenameBranchOverlay", "ConfirmRenameBranchButton", "CancelRenameBranchButton" },
        { "CreateTagOverlay", "ConfirmCreateTagButton", "CancelCreateTagButton" },
        { "RevertOverlay", "ConfirmRevertButton", "CancelRevertButton" },
        { "CherryPickOverlay", "ConfirmCherryPickButton", "CancelCherryPickButton" },
        { "ResetOverlay", "ConfirmResetButton", "CancelResetButton" },
        { "PublishOverlay", "ConfirmPublishButton", "CancelPublishButton" },
        { "DeleteBranchOverlay", "ConfirmDeleteBranchButton", "CancelDeleteBranchButton" },
        { "DeleteTagOverlay", "ConfirmDeleteTagButton", "CancelDeleteTagButton" },
        { "GoToCommitOverlay", "CreateBranchAtCommitButton", "CancelGoToCommitButton" },
        { "GoToCommitOverlay", "DetachAtCommitButton", "CancelGoToCommitButton" },
        { "KeepCommitsOverlay", "KeepCommitsButton", "CancelKeepCommitsButton" },
        { "KeepCommitsOverlay", "DiscardCommitsButton", "CancelKeepCommitsButton" },
        { "MergeBranchesOverlay", "ConfirmMergeButton", "CancelMergeButton" },
        { "MergeBranchesOverlay", "ConfirmRebaseButton", "CancelMergeButton" },
        { "RemotesOverlay", "AddRemoteButton", "CloseRemotesButton" },
        { "RemotesOverlay", "SaveRemoteButton", "CloseRemotesButton" },
        { "RemotesOverlay", "RemoveRemoteButton", "CloseRemotesButton" },
        { "StashOverlay", "CreateStashButton", "CancelStashButton" },
        { "DropStashOverlay", "ConfirmDropStashButton", "CancelDropStashButton" },
        { "DiscardOverlay", "ConfirmDiscardButton", "CancelDiscardButton" },
        { "DiscardHunkOverlay", "ConfirmDiscardHunkButton", "CancelDiscardHunkButton" },
        { "RestoreFileOverlay", "ConfirmRestoreFileButton", "CancelRestoreFileButton" },
    };

    // What the user chooses first where the action is off as the dialog opens: the branch to merge or rebase onto, the remote
    // to add, a change to the remote, the remote to remove.
    private static readonly Dictionary<string, Func<ShellViewModel, Task>> ChosenFor = new()
    {
        ["ConfirmMergeButton"] = async model =>
        {
            var merge = model.MergeDialog.Merge;
            merge.Draft.From = merge.Draft.FromOptions.Single(row => row.Name == "topic");
            await merge.Preview.Completion;
        },
        ["ConfirmRebaseButton"] = async model =>
        {
            var rebase = model.MergeDialog.Rebase;
            await model.MergeDialog.ShowRebaseCommand.ExecuteAsync(null);
            rebase.Draft.Onto = rebase.Draft.OntoOptions.Single(row => row.Name == "topic");
            await rebase.Preview.Completion;
        },
        ["AddRemoteButton"] = model =>
        {
            (model.RemoteSettings.NewName, model.RemoteSettings.NewUrl) = ("upstream", "https://example.com/upstream.git");
            return Task.CompletedTask;
        },
        ["SaveRemoteButton"] = async model =>
        {
            await model.RemoteSettings.BeginEditCommand.ExecuteAsync(model.RemoteSettings.Remotes[0]);
            model.RemoteSettings.EditName = "upstream";
        },
        ["RemoveRemoteButton"] = model =>
        {
            model.RemoteSettings.BeginRemoveCommand.Execute(model.RemoteSettings.Remotes[0]);
            return Task.CompletedTask;
        },
    };

    [AvaloniaTheory]
    [MemberData(nameof(WriteGuarded))]
    public async Task WhenTheRepositoryWritesWhileADialogIsOpen_ShouldDisableConfirmAndKeepCancel(string name, string confirm, string cancel)
    {
        await using var opened = await Cases[name].Open();
        var (window, dialog, h) = (opened.Window, opened.Dialog, opened.Harness!);
        if (ChosenFor.GetValueOrDefault(confirm) is { } choose) { await choose((ShellViewModel)opened.Model!); AppHarness.Layout(window); }
        var (yes, no) = (window.Descendant<Button>(confirm)!, window.Descendant<Button>(cancel)!);
        Assert.True(yes.IsEffectivelyEnabled, confirm + " is off before the write");
        h.Writer.Gate.IsClosed = true;
        var writing = ((ShellViewModel)opened.Model!).Repository.Writer.RunAsync(expected => new IgnoreRequest(expected, Snapshots.Path("build.log"), "build.log"));
        await h.Writer.Gate.Started; AppHarness.Layout(window);
        try
        {
            Assert.Equal((false, true), (yes.IsEffectivelyEnabled, no.IsEffectivelyEnabled));
            no.Command!.Execute(null); AppHarness.Layout(window);
            Assert.False(dialog.IsOpen, cancel + " left the dialog open");
        }
        finally { h.Writer.Gate.Open(); await writing; }
    }

    // A dialog that asked about the repository as it was, when a terminal switches the branch under it: it closes, or every
    // line it shows is what it would say if it were asked now.
    [AvaloniaTheory]
    [InlineData("ResetOverlay")]
    [InlineData("CherryPickOverlay")]
    [InlineData("RevertOverlay")]
    [InlineData("RewriteOverlay")]
    [InlineData("MergeBranchesOverlay")]
    [InlineData("GoToCommitOverlay")]
    [InlineData("DeleteBranchOverlay")]
    public async Task WhenTheBranchIsSwitchedOutsideTragUnderAnOpenDialog_ShouldCloseItOrShowWhatItSaysNow(string name)
    {
        await using var opened = await Cases[name].Open();
        var (window, dialog, h) = (opened.Window, opened.Dialog, opened.Harness!);

        h.Reader.Snapshot = h.Reader.Snapshot with { Branch = "topic", Head = Snapshots.Id('e') };
        await ((ShellViewModel)opened.Model!).RefreshCommand.ExecuteAsync(null); AppHarness.Layout(window);

        if (!dialog.IsOpen) return;
        var shown = Lines(dialog);
        var context = dialog.DataContext;
        dialog.DataContext = null; dialog.DataContext = context; AppHarness.Layout(window);
        Assert.Equal(Lines(dialog), shown);
    }

    // The title, the subtitle and every line the dialog shows.
    private static string[] Lines(DialogHost dialog) =>
        [dialog.Title, dialog.Subtitle, .. dialog.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text ?? "")];

    // The repository every main-window dialog opens over: two commits, the older one reachable by reset and rewrite, a
    // second branch, and every write accepted.
    private static AppHarness Base()
    {
        var h = new AppHarness();
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        h.Branches.Local.Add("topic");
        h.Branches.Distances[Older] = 1;
        h.History.Pages = (_, page) => new([Snapshots.Commit(Snapshots.HeadId, "Update image", Older), Snapshots.Commit(Older, "Older commit")], false, page);
        h.Branches.Rewrites[Older] = new(Snapshots.Id('c'),
            [new(Older, "Older commit", "Older commit", RewriteAction.Pick, "Older commit"),
             new(Snapshots.HeadId, "Update image", "Update image", RewriteAction.Pick, "Update image")], 0);
        return h;
    }

    private static void Changed(AppHarness h) => h.Reader.Snapshot = Snapshots.Create(files: [Snapshots.Modified("a.txt")]);

    // The main window over the repository, the dialog opened by its own command, and what the user typed into it.
    private static async Task<Opened> InWindow(string overlay, Func<AppHarness, ShellViewModel, Window, Task> open, Action<AppHarness>? arrange = null,
        (string Editor, string Text)? typed = null)
    {
        var h = Base();
        arrange?.Invoke(h);
        var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        await open(h, model, window); AppHarness.Layout(window);
        if (typed is { } text) { window.Descendant<TextBox>(text.Editor)!.Text = text.Text; AppHarness.Layout(window); }
        var dialog = window.Descendant<DialogHost>(overlay)!;
        Assert.True(dialog.IsOpen, overlay + " did not open");
        return new(window, dialog, () => h.Writer.Requests.Count != 0, model, h);
    }

    private static async Task ChosenAsync(ShellViewModel model, int index)
    {
        model.Navigator.ShowHistoryCommand.Execute(null); await model.History.HistoryCompletion;
        model.History.SelectedCommit = model.History.Page.Commits[index]; await model.History.Details.Completion;
    }

    private static async Task<Opened> Clone()
    {
        var h = Base();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow)];
        var model = h.Shell();
        var window = h.Window(model);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion; AppHarness.Layout(window);
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);
        model.Catalog.BeginCloneCommand.Execute(group.Rows[0]); AppHarness.Layout(window);
        return new(window, window.Descendant<DialogHost>("CloneDialog")!, () => h.Catalog.Clones.Count != 0, model);
    }

    private static async Task<Opened> GitSetup()
    {
        var h = Base();
        h.Git.Found = new(GitProblem.Missing);
        var model = h.Shell();
        var window = h.Window(model);
        _ = model.InitializeAsync(); AppHarness.Layout(window);
        return new(window, window.Descendant<DialogHost>("GitSetupOverlay")!, () => false, model);
    }

    private static async Task<Opened> GitLabEntry()
    {
        var signedIn = false;
        var service = new FakeAccountService { SignInGitLab = (_, _, _, _) => { signedIn = true; return Task.FromResult(FakeAccountService.GitLab("lab", "work")); } };
        var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Click(window, window.Descendant<Button>("AddGitLabButton")!);
        window.Descendant<TextBox>("GitLabTokenEditor")!.Text = "glpat-synthetic"; AppHarness.Layout(window);
        return new(window, window.Descendant<DialogHost>("GitLabEntryOverlay")!, () => signedIn, null);
    }

    private static async Task<Opened> DeviceLogin()
    {
        var service = new FakeAccountService
        {
            SignInGitHub = async (_, progress, ct) =>
            {
                progress.Report(new(DeviceLoginState.Waiting, "ABCD-1234", new("https://github.com/login/device")));
                await Task.Delay(Timeout.Infinite, ct);
                return FakeAccountService.GitHub();
            }
        };
        var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Click(window, window.Descendant<Button>("AddGitHubButton")!);
        await AppHarness.Until(() => window.Model.Login.HasCode, "the code"); AppHarness.Layout(window);
        return new(window, window.Descendant<DialogHost>("DeviceLoginOverlay")!, () => false, null);
    }

    private static IInputElement? Focused(Window window) => window.FocusManager!.GetFocusedElement();

    private static bool Inside(DialogHost dialog, IInputElement? focused) => focused is Visual visual && (visual == dialog || dialog.IsVisualAncestorOf(visual));

    private static void Press(Window window, Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        AppHarness.Layout(window);
    }
}
