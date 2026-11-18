using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;
using Trag.Desktop.Views.Sync;
using Trag.Domain.Accounts;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Publish;
using Trag.ViewModels.Shell;

namespace Trag.Desktop.UnitTests.Views;

public sealed class PublishUiTests
{
    private static readonly string HeadId = new('c', 40);

    [AvaloniaFact]
    public async Task WhenTheRepositoryHasNoRemote_ShouldReplacePushWithPublishWhoseDialogShowsTheOwnerOnlyWithOrganizations()
    {
        var h = Harness(); var catalog = h.Catalog;
        catalog.Owners[catalog.Accounts[0].Id] = [new("finitysoftware", "finitysoftware")];
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        Assert.True(window.Descendant<Button>("PublishButton")!.IsEffectivelyVisible); Assert.False(window.Descendant<Button>("PushButton")!.IsEffectivelyVisible);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        Assert.True(panel.Descendant<Border>("NoRemoteCard")!.IsEffectivelyVisible); Assert.False(panel.Descendant<Button>("SyncRemoteSelector")!.IsEffectivelyVisible);
        var overlay = window.Descendant<DialogHost>("PublishOverlay")!; Assert.False(overlay.IsEffectivelyVisible);
        panel.Descendant<Button>("PublishLink")!.Command!.Execute(null); await model.Publish.OpenPublishCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal("fixture", window.Descendant<TextBox>("PublishNameEditor")!.Text);
        Assert.Contains("active", window.Descendant<Button>("PublishGitHubButton")!.Classes); Assert.False(window.Descendant<Button>("PublishGitLabButton")!.IsEffectivelyEnabled);
        Assert.Contains("active", window.Descendant<Button>("PublishPrivateButton")!.Classes);
        var owner = window.Descendant<ComboBox>("PublishOwnerSelector")!;
        Assert.True(owner.IsEffectivelyVisible); Assert.Equal(2, owner.ItemCount);
        Assert.Equal("Push main after publishing", window.Descendant<CheckBox>("PublishPushCheck")!.Content);
        Assert.True(window.Descendant<Button>("ConfirmPublishButton")!.IsEffectivelyEnabled);
        window.Descendant<Button>("PublishUrlModeButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.True(window.Descendant<TextBox>("PublishUrlEditor")!.IsEffectivelyVisible); Assert.False(window.Descendant<TextBox>("PublishNameEditor")!.IsEffectivelyVisible);
        Assert.False(window.Descendant<Button>("ConfirmPublishButton")!.IsEffectivelyEnabled);
        window.Descendant<TextBox>("PublishUrlEditor")!.Text = "https://github.com/contoso/fixture.git"; AppHarness.Layout(window);
        Assert.True(window.Descendant<ComboBox>("PublishUrlAccountSelector")!.IsEffectivelyVisible); Assert.True(window.Descendant<Button>("ConfirmPublishButton")!.IsEffectivelyEnabled);
        window.Descendant<Button>("CancelPublishButton")!.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(overlay.IsEffectivelyVisible);
        catalog.Owners.Clear();
        window.Descendant<Button>("PublishButton")!.Command!.Execute(null); await model.Publish.OpenPublishCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.True(overlay.IsEffectivelyVisible); Assert.False(owner.IsEffectivelyVisible); Assert.True(window.Descendant<ComboBox>("PublishAccountSelector")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheBranchIsNotPublished_ShouldShowTheCardInsteadOfTheSelectorsAndOfferItsOwnNameFirst()
    {
        await using var model = Harness(remote: true).Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        Assert.True(window.Descendant<Button>("PublishButton")!.IsEffectivelyVisible);
        Assert.True(panel.Descendant<Border>("PublishBranchCard")!.IsEffectivelyVisible); Assert.False(panel.Descendant<Border>("NoRemoteCard")!.IsEffectivelyVisible);
        Assert.False(panel.Descendant<Button>("SyncRemoteSelector")!.IsEffectivelyVisible);
        panel.Descendant<Button>("PublishBranchLink")!.Command!.Execute(null); await model.Publish.OpenPublishCommand.ExecutionTask!; AppHarness.Layout(window);
        Assert.True(window.Descendant<DialogHost>("PublishOverlay")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<ComboBox>("PublishRemoteSelector")!.IsEffectivelyVisible); Assert.False(window.Descendant<TextBox>("PublishNameEditor")!.IsEffectivelyVisible);
        var targets = window.Descendant<ComboBox>("PublishTargetSelector")!;
        Assert.Equal(2, targets.ItemCount); Assert.True(((PublishTargetOption)targets.SelectedItem!).IsNew); Assert.Equal("develop", model.Publish.Draft.Targets.Items[1].Name);
        Assert.True(window.Descendant<Button>("ConfirmPublishButton")!.IsEffectivelyEnabled);
        window.Close();
    }

    // Publishing a branch where remotes exist can create a new repository instead: the link leads to the provider's form,
    // with the new remote named after the repository because origin is taken, and the publish creates it.
    [AvaloniaFact]
    public async Task WhenABranchIsPublishedWhereRemotesExist_ShouldOfferALinkThatCreatesANewRepositoryInstead()
    {
        var h = Harness(remote: true);
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        var link = window.Descendant<Button>("PublishCreateRepositoryLink")!;
        Assert.True(link.IsEffectivelyVisible); Assert.False(window.Descendant<TextBox>("PublishNameEditor")!.IsEffectivelyVisible);
        link.Command!.Execute(null); AppHarness.Layout(window);
        Assert.False(link.IsEffectivelyVisible); Assert.False(window.Descendant<ComboBox>("PublishRemoteSelector")!.IsEffectivelyVisible);
        Assert.True(window.Descendant<TextBox>("PublishNameEditor")!.IsEffectivelyVisible);
        var remoteName = window.Descendant<TextBox>("PublishNewRemoteNameEditor")!;
        Assert.True(remoteName.IsEffectivelyVisible); Assert.Equal("fixture", remoteName.Text);
        window.Descendant<Button>("ConfirmPublishButton")!.Command!.Execute(null); await model.Publish.PublishCommand.ExecutionTask!;
        var publish = Assert.IsType<PublishRequest>(h.Writer.LastRequest).Publish;
        Assert.Equal((PublishKind.CreateRepository, "fixture", "fixture"), (publish.Kind, publish.RemoteName, publish.RepositoryName));
        window.Close();
    }

    // A push that found the remote's repository missing under the user's own account shows the card whose link creates it
    // and pushes, though the accounts answer after the push ended; once that went through, the card goes.
    [AvaloniaFact]
    public async Task WhenAPushFindsTheRepositoryMissing_ShouldShowTheCardWhoseLinkCreatesItAndPushes()
    {
        var h = Harness(remote: true, tracking: true);
        h.Writer.Result = GitWriteResult.Rejected("The host has no repository at this remote's address.") with { MissingRepository = true };
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        var card = panel.Descendant<Border>("MissingRepositoryCard")!;
        Assert.False(card.IsEffectivelyVisible);
        h.Catalog.AccountsGate.IsClosed = true;
        await model.Sync.PushCommand.ExecuteAsync(null);
        h.Catalog.AccountsGate.Open(); await model.Sync.MissingRepositoryCompletion; AppHarness.Layout(window);
        Assert.True(card.IsEffectivelyVisible); Assert.True(panel.Descendant<Button>("CreateMissingRepositoryLink")!.IsEffectivelyEnabled);
        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "fixture was not found on GitHub");
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Published main.");
        panel.Descendant<Button>("CreateMissingRepositoryLink")!.Command!.Execute(null);
        await model.Sync.CreateMissingRepositoryCommand.ExecutionTask!; await model.Sync.MissingRepositoryCompletion; AppHarness.Layout(window);
        Assert.Equal("fixture", Assert.IsType<PublishRequest>(h.Writer.LastRequest).Publish.RepositoryName);
        Assert.False(card.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThereIsASingleRemote_ShouldHideTheRemoteCaptionAndTheSelector()
    {
        await using var model = Harness(remote: true, tracking: true).Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion; AppHarness.Layout(window);
        var panel = Assert.Single(window.GetVisualDescendants().OfType<SyncPanel>());
        Assert.False(model.Publish.Context.ShowPublish); Assert.True(model.Publish.Context.ShowSyncSelectors); Assert.False(model.Publish.Context.HasMultipleRemotes);
        Assert.False(panel.Descendant<TextBlock>("RemoteHeader")!.IsEffectivelyVisible); Assert.False(panel.Descendant<Button>("SyncRemoteSelector")!.IsEffectivelyVisible);
        Assert.True(panel.Descendant<Button>("SyncBranchSelector")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheOwnerAndUrlAccountListsAreReplaced_ShouldKeepTheirSelectionBoundBothWays()
    {
        var h = WithGitLabAndATeam(); var catalog = h.Catalog;
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        AppHarness.Layout(window);

        var owner = window.Descendant<ComboBox>("PublishOwnerSelector")!;
        owner.SelectedIndex = 1;
        Assert.Equal("team", model.Publish.Accounts.Owners.Value.Selected!.Id);
        window.Descendant<Button>("PublishGitLabButton")!.Command!.Execute(null);
        await model.Publish.Accounts.Owners.Completion;
        AppHarness.Layout(window);
        Assert.False(owner.IsEffectivelyVisible);
        Assert.Equal("work", model.Publish.Accounts.Owners.Value.Selected!.Id);
        window.Descendant<Button>("PublishGitHubButton")!.Command!.Execute(null);
        await model.Publish.Accounts.Owners.Completion;
        AppHarness.Layout(window);
        Assert.True(owner.IsEffectivelyVisible);
        Assert.Same(model.Publish.Accounts.Owners.Value.Selected, owner.SelectedItem);
        Assert.True(((PublishOwner)owner.SelectedItem!).IsUser);

        window.Descendant<Button>("PublishUrlModeButton")!.Command!.Execute(null);
        var url = window.Descendant<TextBox>("PublishUrlEditor")!;
        var accounts = window.Descendant<ComboBox>("PublishUrlAccountSelector")!;
        url.Text = "https://github.com/contoso/trag";
        AppHarness.Layout(window);
        Assert.Equal(2, accounts.ItemCount);
        Assert.Equal(catalog.Accounts[0].Id, ((AccountChoice)accounts.SelectedItem!).Id);
        accounts.SelectedIndex = 0;
        url.Text = "https://github.com/contoso/other";
        AppHarness.Layout(window);
        Assert.Equal(AccountChoice.External, ((AccountChoice)accounts.SelectedItem!).Id);
        Assert.Null(model.Publish.Accounts.UrlAccounts.AccountId);
        url.Text = "git@github.com:contoso/trag.git";
        AppHarness.Layout(window);
        Assert.False(accounts.IsEffectivelyVisible);
        Assert.Null(accounts.SelectedItem);
        window.Close();
    }

    // Enter in the address box does what Publish would: an address that is not valid yet sends nothing and keeps the dialog.
    [AvaloniaFact]
    public async Task WhenEnterIsPressedInTheUrlBoxWithAnInvalidAddress_ShouldPublishNothing()
    {
        var h = Harness();
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model }; window.Show(); AppHarness.Layout(window);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null); AppHarness.Layout(window);
        window.Descendant<Button>("PublishUrlModeButton")!.Command!.Execute(null); AppHarness.Layout(window);
        var url = window.Descendant<TextBox>("PublishUrlEditor")!;
        url.Text = "not an address"; url.Focus(); AppHarness.Layout(window);

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); AppHarness.Layout(window);

        Assert.DoesNotContain(h.Writer.Requests, request => request is PublishRequest);
        Assert.True(window.Descendant<DialogHost>("PublishOverlay")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenAPublishIsRunning_ShouldDisableTheFormCommandsAndEnableThemAgainWhenItIsRejected()
    {
        var h = WithGitLabAndATeam(); var writer = h.Writer;
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        AppHarness.Layout(window);
        var owner = window.Descendant<ComboBox>("PublishOwnerSelector")!;
        await ChooseOwnersAndUrlAccounts(model, window);

        window.Descendant<Button>("PublishProviderModeButton")!.Command!.Execute(null);
        writer.Gate.IsClosed = true;
        var publishing = model.Publish.PublishCommand.ExecuteAsync(null);
        await writer.Gate.Started;
        AppHarness.Layout(window);
        foreach (var name in new[] { "PublishGitHubButton", "PublishGitLabButton", "PublishUrlModeButton", "PublishPrivateButton", "PublishPublicButton" })
            Assert.False(window.Descendant<Button>(name)!.IsEffectivelyEnabled);
        Assert.False(owner.IsEffectivelyEnabled);
        Assert.False(model.Publish.ChoosePublishGitLabCommand.CanExecute(null));
        model.Publish.ChoosePublishGitLabCommand.Execute(null);
        Assert.True(model.Publish.Accounts.IsGitHub);
        writer.Gate.Open();
        await publishing;
        AppHarness.Layout(window);
        Assert.True(model.Publish.Draft.IsOpen);
        Assert.True(owner.IsEffectivelyEnabled);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheDraftOrTheRemotesAreReplaced_ShouldKeepTheTargetBindingsAndConfirmCurrent()
    {
        var h = Harness(remote: true); var reader = h.Reader;
        var upstream = new RepositoryRemote("upstream", "https://example.invalid/repo", "https://example.invalid/repo");
        reader.Snapshot = reader.Snapshot with { Remotes = [.. reader.Snapshot.Remotes, upstream] };
        await using var model = h.Shell();
        var window = new MainWindow { DataContext = model };
        window.Show();
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        AppHarness.Layout(window);
        var targets = window.Descendant<ComboBox>("PublishTargetSelector")!;
        var remotes = window.Descendant<ComboBox>("PublishRemoteSelector")!;
        var confirm = window.Descendant<Button>("ConfirmPublishButton")!;
        targets.SelectedIndex = 1;
        AppHarness.Layout(window);
        Assert.Equal("develop", model.Publish.Draft.Targets.Selected!.Name);
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == model.Publish.Draft.Targets.Warning);

        model.Publish.CancelPublishCommand.Execute(null);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        AppHarness.Layout(window);
        Assert.Equal("develop", ((PublishTargetOption)targets.SelectedItem!).Name);
        Assert.True(confirm.IsEffectivelyEnabled);

        targets.SelectedIndex = 1;
        var changedAddress = new RepositoryRemote("origin", "https://example.invalid/new", "https://example.invalid/new");
        model.Repository.Apply(reader.Snapshot with { Remotes = [changedAddress, upstream] });
        AppHarness.Layout(window);
        Assert.Equal(changedAddress, remotes.SelectedItem);
        Assert.Equal("develop", ((PublishTargetOption)targets.SelectedItem!).Name);

        model.Repository.Apply(reader.Snapshot with { Remotes = [upstream] });
        AppHarness.Layout(window);
        Assert.Equal(upstream, remotes.SelectedItem);
        Assert.Equal("upstream", ((PublishTargetOption)targets.SelectedItem!).Remote);
        Assert.True(confirm.IsEffectivelyEnabled);
        model.Repository.Apply(reader.Snapshot with { Remotes = [] });
        AppHarness.Layout(window);
        Assert.Null(remotes.SelectedItem);
        Assert.Null(targets.SelectedItem);
        Assert.Equal(0, targets.ItemCount);
        Assert.False(confirm.IsEffectivelyEnabled);
        window.Close();
    }

    // One GitHub account with no organizations unless a test adds them, a remote branch to publish onto, every write
    // succeeding; remote adds origin to the repository, tracking makes main track it.
    private static AppHarness Harness(bool remote = false, bool tracking = false)
    {
        var h = new AppHarness();
        h.Reader.Snapshot = Snapshots.Create(head: HeadId, trackingRemote: tracking ? "origin" : null, upstream: tracking ? "origin/main" : null,
            remotes: remote ? [new("origin", "https://github.com/contoso/fixture.git", "https://github.com/contoso/fixture.git")] : []);
        h.Writer.Result = new(GitWriteOutcome.Succeeded, "Done.");
        h.Branches.Remote.Add("origin/develop");
        h.Catalog.Accounts.Add(FakeAccountService.GitHub(user: "contoso"));
        return h;
    }

    // A GitLab account beside the GitHub one, a team for the GitHub user, and every write rejected.
    private static AppHarness WithGitLabAndATeam()
    {
        var h = Harness();
        h.Catalog.Accounts.Add(FakeAccountService.GitLab("lab", "work"));
        h.Catalog.Owners[h.Catalog.Accounts[0].Id] = [new("team", "Team")];
        h.Writer.Result = new(GitWriteOutcome.Rejected, "Try again.");
        return h;
    }

    // The list test's steps: the team chosen, GitLab and back, and three addresses typed in URL mode, so the publish
    // starts from the form that test leaves.
    private static async Task ChooseOwnersAndUrlAccounts(ShellViewModel model, MainWindow window)
    {
        window.Descendant<ComboBox>("PublishOwnerSelector")!.SelectedIndex = 1;
        window.Descendant<Button>("PublishGitLabButton")!.Command!.Execute(null);
        await model.Publish.Accounts.Owners.Completion;
        AppHarness.Layout(window);
        window.Descendant<Button>("PublishGitHubButton")!.Command!.Execute(null);
        await model.Publish.Accounts.Owners.Completion;
        AppHarness.Layout(window);

        window.Descendant<Button>("PublishUrlModeButton")!.Command!.Execute(null);
        var url = window.Descendant<TextBox>("PublishUrlEditor")!;
        var accounts = window.Descendant<ComboBox>("PublishUrlAccountSelector")!;
        url.Text = "https://github.com/contoso/trag";
        AppHarness.Layout(window);
        accounts.SelectedIndex = 0;
        url.Text = "https://github.com/contoso/other";
        AppHarness.Layout(window);
        url.Text = "git@github.com:contoso/trag.git";
        AppHarness.Layout(window);
    }
}
