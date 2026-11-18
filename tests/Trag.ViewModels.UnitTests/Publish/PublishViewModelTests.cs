using Trag.Domain.Accounts;
using Trag.Domain.Branches;
using Trag.Domain.Errors;
using Trag.Domain.Publish;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Domain.Writes;
using Trag.TestSupport;
using Trag.ViewModels.Shell;

namespace Trag.ViewModels.UnitTests.Publish;

public sealed class PublishViewModelTests
{
    private static readonly AccountInfo GitHub = new("account-1", AccountProvider.GitHub, "7", "contoso", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
    private static readonly AccountInfo GitLab = new("account-2", AccountProvider.GitLab, "1", "work", new("https://gitlab.com/api/v4/"), new("https://gitlab.com/"), true, DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task WhenTheRepositoryHasNoRemote_ShouldOfferPublishInsteadOfPushAndCreateATrackedOrigin()
    {
        var backend = new Backend(); var catalog = new FakeRepositoryCatalog { Accounts = { GitHub, GitLab } };
        catalog.Owners[GitHub.Id] = [new("acme", "acme")];
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.True(model.Publish.Context.ShowPublish); Assert.False(model.Publish.Context.HasRemotes); Assert.False(model.Sync.PushCommand.CanExecute(null)); Assert.True(model.Publish.OpenPublishCommand.CanExecute(null));
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.True(model.Publish.Draft.IsOpen); Assert.Equal(PublishKind.CreateRepository, model.Publish.Draft.Mode); Assert.True(model.Publish.Accounts.IsGitHub); Assert.True(model.Publish.Accounts.HasGitLabAccounts);
        Assert.Equal("contoso", model.Publish.Accounts.SelectedAccount!.UserName); Assert.Single(model.Publish.Accounts.Visible);
        Assert.True(model.Publish.Accounts.Owners.Value.HasChoice); Assert.True(model.Publish.Accounts.Owners.Value.Items[0].IsUser); Assert.Same(model.Publish.Accounts.Owners.Value.Items[0], model.Publish.Accounts.Owners.Value.Selected);
        Assert.Equal("fixture", model.Publish.Draft.Name); Assert.True(model.Publish.Draft.IsPrivate); Assert.True(model.Publish.Draft.PushAfter); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.Name = "bad name"; Assert.True(model.Publish.Draft.HasNameError); Assert.False(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.Name = "trag"; Assert.False(model.Publish.Draft.HasNameError); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.ChoosePublishGitLabCommand.Execute(null); await model.Publish.Accounts.Owners.Completion;
        Assert.Equal("work", model.Publish.Accounts.SelectedAccount!.UserName); Assert.False(model.Publish.Accounts.Owners.Value.HasChoice);
        model.Publish.ChoosePublishGitHubCommand.Execute(null); await model.Publish.Accounts.Owners.Completion;
        model.Publish.Accounts.Owners.Value.Selected = model.Publish.Accounts.Owners.Value.Items[1]; model.Publish.SetPublishPublicCommand.Execute(null);
        Assert.Contains("create the repository, add origin, and set trunk", model.Publish.Draft.Summary);
        await model.Publish.PublishCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        var request = backend.Requests.Single();
        var publish = Assert.IsType<PublishRequest>(request).Publish;
        Assert.Equal(PublishKind.CreateRepository, publish.Kind); Assert.Equal("origin", publish.RemoteName); Assert.Equal("trunk", publish.Branch); Assert.True(publish.Push);
        Assert.Equal(GitHub.Id, publish.AccountId); Assert.Equal("acme", publish.OwnerId); Assert.Equal("trag", publish.RepositoryName); Assert.False(publish.IsPrivate); Assert.Null(publish.RemoteUrl);
        Assert.False(model.Publish.Draft.IsOpen); Assert.True(model.Publish.Context.HasRemotes); Assert.False(model.Publish.Context.ShowPublish);
        Assert.Equal("origin", model.Sync.Selection.Remote!.Name); Assert.NotNull(model.Sync.Selection.SelectedBranch); Assert.True(model.Sync.PushCommand.CanExecute(null));
        Assert.Contains("Published", model.StatusBar.Status);
    }

    [Fact]
    public async Task WhenTheUserPublishesToAnAddress_ShouldMatchAccountsToItAndKeepTheDialogOpenAfterAFailure()
    {
        var backend = new Backend(); var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Publish.UsePublishUrlCommand.Execute(null);
        Assert.True(model.Publish.Draft.IsUrlMode); Assert.False(model.Publish.PublishCommand.CanExecute(null)); Assert.False(model.Publish.Accounts.UrlAccounts.HasChoices);
        model.Publish.Draft.RemoteUrl = "https://github.com/contoso/trag.git";
        Assert.True(model.Publish.Accounts.UrlAccounts.HasChoices); Assert.Equal(GitHub.Id, model.Publish.Accounts.UrlAccounts.Selected!.Id); Assert.Equal(AccountChoice.External, model.Publish.Accounts.UrlAccounts.Items[0].Id);
        Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.RemoteUrl = "git@github.com:contoso/trag.git"; Assert.False(model.Publish.Accounts.UrlAccounts.HasChoices); Assert.Null(model.Publish.Accounts.UrlAccounts.Selected);
        model.Publish.Draft.RemoteName = "bad name"; Assert.True(model.Publish.Draft.HasRemoteError); Assert.False(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.RemoteName = "upstream"; Assert.False(model.Publish.Draft.HasRemoteError);
        Assert.Contains("add upstream with this address", model.Publish.Draft.Summary);
        backend.Result = new(GitWriteOutcome.Rejected, "Git could not add the remote.", "fatal: detail");
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.True(model.Publish.Draft.IsOpen); Assert.Equal("Git could not add the remote.", model.Publish.Draft.Notice); Assert.Equal("fatal: detail", model.Publish.PublishDetails); Assert.False(model.Publish.Context.HasRemotes);
        backend.Result = new(GitWriteOutcome.Succeeded, "Published trunk to upstream.");
        await model.Publish.PublishCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        var publish = Assert.IsType<PublishRequest>(backend.Requests[^1]).Publish;
        Assert.Equal(PublishKind.AddRemote, publish.Kind); Assert.Equal("upstream", publish.RemoteName); Assert.Equal("git@github.com:contoso/trag.git", publish.RemoteUrl); Assert.Null(publish.AccountId); Assert.Null(publish.RepositoryName);
        Assert.False(model.Publish.Draft.IsOpen); Assert.Equal("upstream", model.Sync.Selection.Remote!.Name); Assert.False(model.Publish.Context.ShowPublish);
    }

    [Fact]
    public async Task WhenTheAccountHasNoOrganizationsOrCannotListThem_ShouldHideTheOwnerAndKeepTheAccountUsable()
    {
        var backend = new Backend(); var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.False(model.Publish.Accounts.Owners.Value.HasChoice); Assert.True(model.Publish.Accounts.Owners.Value.Selected!.IsUser); Assert.Equal("", model.Publish.Accounts.Message); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.CancelPublishCommand.Execute(null); Assert.False(model.Publish.Draft.IsOpen);
        catalog.OwnersError = new AccountException("Organizations could not be listed with this sign-in. Sign in again to grant read:org, or publish under your own account.");
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.Contains("read:org", model.Publish.Accounts.Message); Assert.False(model.Publish.Accounts.Owners.Value.HasChoice); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.Null(Assert.IsType<PublishRequest>(backend.Requests.Single()).Publish.OwnerId);
    }

    // The dialog read the accounts and published before they were removed; it must read them again rather than keep them.
    [Fact]
    public async Task WhenTheAccountsAreRemovedAndTheRepositoryIsOpenedAgain_ShouldPublishOnlyToAnAddress()
    {
        var backend = new Backend(); var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await PublishWithoutOrganizationsAsync(model, catalog);
        Assert.Null(Assert.IsType<PublishRequest>(backend.Requests.Single()).Publish.OwnerId);
        catalog.Accounts.Clear();
        await model.CloseRepositoryCommand.ExecuteAsync(null); Assert.False(model.Publish.Draft.IsOpen); Assert.False(model.Publish.Context.ShowPublish);
        backend.Remotes = []; backend.Tracking = false;
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.False(model.Publish.Accounts.HasAccounts); Assert.False(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.UsePublishUrlCommand.Execute(null); model.Publish.Draft.RemoteUrl = "https://example.invalid/repo.git";
        Assert.True(model.Publish.PublishCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheBranchHasNoUpstream_ShouldHideTheSelectorsAndPublishItUnderItsOwnNameByDefault()
    {
        var backend = new Backend { Remotes = [new("origin", "https://fetch.example.invalid/repo", "https://fetch.example.invalid/repo")], Tracking = false };
        await using var model = ShellHarness.Compose(backend.Workspace);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.True(model.Publish.Context.HasRemotes); Assert.False(model.Publish.Context.HasMultipleRemotes);
        Assert.True(model.Publish.Context.ShowPublish); Assert.True(model.Publish.Context.ShowBranchCard); Assert.False(model.Publish.Context.ShowSyncSelectors);
        Assert.Equal("trunk is not published yet", model.Publish.Context.BranchCardText); Assert.False(model.Sync.PushCommand.CanExecute(null));
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.Equal(PublishKind.ExistingRemote, model.Publish.Draft.Mode); Assert.True(model.Publish.Draft.IsBranchMode); Assert.False(model.Publish.Draft.ShowModeSwitch); Assert.Equal("Publish branch", model.Publish.Draft.Title);
        Assert.Equal("origin", model.Publish.Draft.ExistingRemote!.Name);
        Assert.Equal(new[] { "trunk", "main" }, model.Publish.Draft.Targets.Items.Select(target => target.Name).ToArray());
        Assert.True(model.Publish.Draft.Targets.Selected!.IsNew); Assert.False(model.Publish.Draft.Targets.HasWarning); Assert.Contains("origin/trunk", model.Publish.Draft.Summary); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.Targets.Selected = model.Publish.Draft.Targets.Items[1];
        Assert.Contains("origin/main already exists", model.Publish.Draft.Targets.Warning); Assert.Contains("origin/main", model.Publish.Draft.Summary);
        model.Publish.Draft.Targets.Selected = model.Publish.Draft.Targets.Items[0];
        await model.Publish.PublishCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        var publish = Assert.IsType<PublishRequest>(backend.Requests.Single()).Publish;
        Assert.Equal(PublishKind.ExistingRemote, publish.Kind); Assert.Equal("origin", publish.RemoteName); Assert.Equal("trunk", publish.RemoteBranch); Assert.True(publish.Push); Assert.Null(publish.RemoteUrl);
        Assert.False(model.Publish.Draft.IsOpen); Assert.False(model.Publish.Context.ShowPublish); Assert.True(model.Publish.Context.ShowSyncSelectors); Assert.True(model.Sync.PushCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenTheSidebarPushesTheBranchUnderItsOwnName_ShouldCreateItThereAndWarnAboutOtherTargets()
    {
        // An upstream under another name keeps the selectors; the branch's own name is still offered as a new target.
        var backend = new Backend { Remotes = [new("origin", "https://fetch.example.invalid/repo", "https://fetch.example.invalid/repo")], Tracking = true, UpstreamName = "main" };
        await using var model = ShellHarness.Compose(backend.Workspace);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.False(model.Publish.Context.ShowPublish); Assert.True(model.Publish.Context.ShowSyncSelectors);
        Assert.Equal("main", model.Sync.Selection.SelectedBranch!.Branch.Display); Assert.Contains("into origin/main", model.Sync.Panel.Target.Warning);
        model.Sync.Selection.SelectedBranch = model.Sync.Selection.Branches.Single(branch => branch.Branch.IsNew); await model.Sync.SyncCompletion;
        Assert.Equal("trunk · new branch", model.Sync.Selection.SelectedBranch!.Label); Assert.False(model.Sync.Panel.Target.HasWarning); Assert.Contains("not on origin yet", model.Sync.Panel.Counts.Text);
        await model.Sync.PushCommand.ExecuteAsync(null); await model.Sync.SyncCompletion;
        var request = backend.Requests.Single();
        var existing = Assert.IsType<PublishRequest>(request).Publish; Assert.Equal(PublishKind.ExistingRemote, existing.Kind); Assert.Equal("trunk", existing.RemoteBranch);
        Assert.False(model.Sync.Selection.SelectedBranch!.Branch.IsNew); Assert.Equal("trunk", model.Sync.Selection.SelectedBranch.Branch.Display);
        model.Sync.Selection.SelectedBranch = model.Sync.Selection.Branches.Single(branch => branch.Branch.Display == "main"); await model.Sync.SyncCompletion;
        Assert.True(model.Sync.Panel.Target.HasWarning);
        await model.Sync.PushCommand.ExecuteAsync(null);
        Assert.Equal(SyncAction.Push, Assert.IsType<SyncRequest>(backend.Requests[^1]).Action);
    }

    [Fact]
    public async Task WhenTheRepositoryHasNoCommitYet_ShouldAddARemoteOnlyWithoutPushing()
    {
        var backend = new Backend { Head = null }; var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        Assert.True(model.Publish.Context.ShowPublish);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.False(model.Publish.Context.CanPush); Assert.False(model.Publish.Draft.PushAfter); Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.PushAfter = true; Assert.False(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.PushAfter = false;
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.False(Assert.IsType<PublishRequest>(backend.Requests.Single()).Publish.Push); Assert.False(model.Publish.Draft.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheDialogIsClosedAndReopenedDuringARead_ShouldDiscardTheEarlierAccountOrOwnerRead(bool readingOwners)
    {
        var backend = new Backend();
        var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        var oldRead = new AsyncGate { IsClosed = true };
        if (readingOwners)
            catalog.ReadOwners = async (_, ct) => { await oldRead.WaitAsync(ct); return [new("old", "Old team")]; };
        else
            catalog.ReadAccounts = async ct => { await oldRead.WaitAsync(ct); return [GitHub]; };

        var firstOpen = model.Publish.OpenPublishCommand.ExecuteAsync(null);
        await oldRead.Started;
        model.Publish.CancelPublishCommand.Execute(null);
        Assert.False(model.Publish.Draft.IsOpen);
        Assert.True(oldRead.Token.IsCancellationRequested);
        Assert.False(model.Publish.Accounts.IsBusy);

        catalog.ReadAccounts = null;
        catalog.ReadOwners = null;
        catalog.Accounts.Clear();
        catalog.Accounts.Add(GitLab);
        catalog.Owners[GitLab.Id] = [new("current", "Current team")];
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        oldRead.Open();
        await firstOpen;

        Assert.True(model.Publish.Draft.IsOpen);
        Assert.Same(GitLab, model.Publish.Accounts.SelectedAccount);
        Assert.Equal(new[] { "work", "current" }, model.Publish.Accounts.Owners.Value.Items.Select(owner => owner.Id));
        Assert.True(model.Publish.PublishCommand.CanExecute(null));
    }

    [Fact]
    public async Task WhenAnAddressPublishSucceedsWhileTheAccountsAreRead_ShouldCancelTheReadAsTheDialogCloses()
    {
        var backend = new Backend();
        var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        catalog.AccountsGate.IsClosed = true;
        var opening = model.Publish.OpenPublishCommand.ExecuteAsync(null);
        await catalog.AccountsGate.Arrival(catalog.AccountsReads);
        model.Publish.UsePublishUrlCommand.Execute(null);
        model.Publish.Draft.RemoteUrl = "git@github.com:contoso/trag.git";
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.False(model.Publish.Draft.IsOpen);
        Assert.True(catalog.AccountsGate.Token.IsCancellationRequested);
        catalog.AccountsGate.Open();
        await opening;
        Assert.Empty(model.Publish.Accounts.All.Value);
        Assert.False(model.Publish.Accounts.IsBusy);
    }

    [Fact]
    public async Task WhenTheDraftOrTheOwnerIsEdited_ShouldRefreshOnlyPublishingAndNotTheRepositoryPresentation()
    {
        var backend = new Backend();
        var catalog = new FakeRepositoryCatalog { Accounts = { GitHub } };
        catalog.Owners[GitHub.Id] = [new("team", "Team")];
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: catalog);
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        var changes = new List<string?>();
        model.Publish.Draft.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var parentChanges = new List<string?>();
        model.Publish.PropertyChanged += (_, e) => parentChanges.Add(e.PropertyName);
        var publishChanges = 0;
        var openChanges = 0;
        model.Publish.PublishCommand.CanExecuteChanged += (_, _) => publishChanges++;
        model.Publish.OpenPublishCommand.CanExecuteChanged += (_, _) => openChanges++;

        model.Publish.Accounts.Owners.Value.Selected = null;
        Assert.False(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Accounts.Owners.Value.Selected = model.Publish.Accounts.Owners.Value.Items[1];
        Assert.True(model.Publish.PublishCommand.CanExecute(null));
        model.Publish.Draft.RemoteUrl = "https://github.com/contoso/trag";
        Assert.Contains(nameof(model.Publish.Draft.RemoteError), changes);
        Assert.DoesNotContain(nameof(model.Publish.Context.ShowPublish), changes);
        Assert.DoesNotContain(nameof(model.Publish.Draft.Summary), changes);
        Assert.Empty(parentChanges);
        Assert.Equal(0, openChanges);
        Assert.True(publishChanges >= 3);
    }

    [Fact]
    public async Task WhenTheDialogIsCancelledAndReopened_ShouldStartFromAFreshDraft()
    {
        var backend = new Backend();
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        var draft = model.Publish.Draft;
        draft.Name = "unfinished";
        draft.IsPrivate = false;
        draft.PushAfter = false;
        draft.Mode = PublishKind.AddRemote;
        draft.RemoteUrl = "https://github.com/contoso/old";
        model.Publish.CancelPublishCommand.Execute(null);
        Assert.False(draft.IsOpen);
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        Assert.True(draft.IsOpen);
        Assert.Equal("fixture", draft.Name);
        Assert.True(draft.IsPrivate);
        Assert.True(draft.PushAfter);
        Assert.Equal(PublishKind.CreateRepository, draft.Mode);
        Assert.Empty(draft.RemoteUrl);
    }

    [Theory]
    [InlineData(PublishKind.CreateRepository)]
    [InlineData(PublishKind.AddRemote)]
    [InlineData(PublishKind.ExistingRemote)]
    public async Task WhenThePushFailsAfterTheRemoteAndUpstreamWereConfigured_ShouldCloseTheDialog(PublishKind kind)
    {
        var remote = new RepositoryRemote("origin", "https://github.com/contoso/fixture", "https://github.com/contoso/fixture");
        var backend = new Backend { Remotes = kind == PublishKind.ExistingRemote ? [remote] : [] };
        backend.Workspace.Handle = _ =>
        {
            backend.Remotes = [remote];
            backend.Tracking = true;
            backend.RemoteHasTrunk = true;
            return new(GitWriteOutcome.Rejected, "Upstream configured; push failed.", "Authentication failed.");
        };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Publish.Draft.Mode = kind;
        model.Publish.Draft.RemoteUrl = remote.PushAddress;
        await model.Publish.PublishCommand.ExecuteAsync(null);
        await model.Sync.SyncCompletion;
        Assert.False(model.Publish.Draft.IsOpen);
        Assert.False(model.Publish.Context.ShowPublish);
        Assert.True(model.Sync.PushCommand.CanExecute(null));
        Assert.Equal("Upstream configured; push failed.", model.StatusBar.Status);
        Assert.Equal("Authentication failed.", model.StatusBar.SyncDetails);
    }

    [Fact]
    public async Task WhenTheInputOrSnapshotChangesAsThePublishIsAnnounced_ShouldPublishWhatWasShownBefore()
    {
        var backend = new Backend { Result = new(GitWriteOutcome.Rejected, "Try again.") };
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Publish.Draft.Name = "captured";
        var expected = model.Repository.Snapshot!;
        model.Publish.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(model.Publish.IsPublishing) || !model.Publish.IsPublishing)
                return;
            model.Publish.Draft.Name = "later";
            model.Repository.Apply(expected with { Branch = "later-branch" });
        };
        await model.Publish.PublishCommand.ExecuteAsync(null);
        var request = Assert.IsType<PublishRequest>(Assert.Single(backend.Requests));
        Assert.Same(expected, request.Expected);
        Assert.Equal("trunk", request.Publish.Branch);
        Assert.Equal("captured", request.Publish.RepositoryName);
    }

    // Until Git reports, the dialog's notice says the publish runs; what Git reports then is its notice as well as the
    // status line.
    [Fact]
    public async Task WhenThePublishRuns_ShouldShowItsProgressAsTheDialogsNotice()
    {
        var backend = new Backend();
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        var publish = backend.Workspace.Handle!;
        var (before, shown) = ("", "");
        backend.Workspace.Handle = request =>
        {
            before = model.Publish.Draft.Notice;
            request.Progress!.Report("Creating contoso/fixture…");
            shown = model.Publish.Draft.Notice;
            return publish(request);
        };
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.Equal(("Publishing…", "Creating contoso/fixture…"), (before, shown));
    }

    // The dialog stays to show how the publish ended.
    [Fact]
    public async Task WhenTheUserCancelsWhilePublishing_ShouldCancelTheWriteAndKeepTheDialog()
    {
        var backend = new Backend();
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        backend.Workspace.Gate.IsClosed = true;
        var pending = model.Publish.PublishCommand.ExecuteAsync(null);
        await backend.Workspace.Gate.Started;
        model.Publish.CancelPublishCommand.Execute(null);
        Assert.True(backend.Workspace.Gate.Token.IsCancellationRequested); Assert.True(model.Publish.Draft.IsOpen);
        backend.Workspace.Gate.Open();
        await pending;
    }

    [Fact]
    public async Task WhenHeadLeftItsBranchWhileTheDialogWasOpen_ShouldPublishNothing()
    {
        var backend = new Backend();
        await using var model = ShellHarness.Compose(backend.Workspace, catalog: new FakeRepositoryCatalog { Accounts = { GitHub } });
        await model.Session.OpenRepositoryAsync("fixture");
        await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Repository.Apply(model.Repository.Snapshot! with { Branch = null });
        await model.Publish.PublishCommand.ExecuteAsync(null);
        Assert.Empty(backend.Requests);
        Assert.True(model.Publish.Draft.IsOpen);
        Assert.Equal("", model.Publish.Draft.Notice);
    }

    // The user opens the dialog for an account without organizations, cancels, opens it again while the organizations
    // cannot be listed, and publishes under the personal account.
    private static async Task PublishWithoutOrganizationsAsync(ShellViewModel model, FakeRepositoryCatalog catalog)
    {
        await model.Session.OpenRepositoryAsync("fixture"); await model.Sync.SyncCompletion;
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        model.Publish.CancelPublishCommand.Execute(null);
        catalog.OwnersError = new AccountException("Organizations could not be listed with this sign-in. Sign in again to grant read:org, or publish under your own account.");
        await model.Publish.OpenPublishCommand.ExecuteAsync(null);
        await model.Publish.PublishCommand.ExecuteAsync(null);
    }

    // The fixture repository as the publish flow sees it: which remotes it has, whether trunk is published, and the
    // workspace that answers from exactly that state. A publish changes the state the way Git would.
    private sealed class Backend
    {
        public IReadOnlyList<RepositoryRemote> Remotes = [];
        public bool Tracking;
        public string UpstreamName = "trunk";
        public bool RemoteHasTrunk;
        public string? Head = Snapshots.HeadId;
        public FakePublishWorkspace Workspace { get; }
        public List<GitWriteRequest> Requests => Workspace.Requests;
        public GitWriteResult Result { get => Workspace.Result; set => Workspace.Result = value; }

        public Backend()
        {
            Workspace = new()
            {
                Result = new(GitWriteOutcome.Succeeded, "Published trunk to origin. origin/trunk is now its upstream."),
                Read = () => new RepositorySnapshot("fixture", "2.43.0", "trunk", Head, Tracking ? Remotes[0].Name + "/" + UpstreamName : null, Tracking ? Remotes[0].Name : null,
                    Remotes, RepositoryOperation.None, [], []) { UpstreamBranchRef = Tracking ? "refs/heads/" + UpstreamName : null, IndexFingerprint = "index" },
                ReadBranches = _ => new BranchSet(
                    [new("trunk", "refs/heads/trunk", Head ?? "", false)],
                    Remotes.Count == 0 ? [] : [new(Remotes[0].Name + "/main", "refs/remotes/" + Remotes[0].Name + "/main", Head ?? "", true, Remotes[0].Name) { NameOnRemote = "main" }], "trunk"),
                // The remote always has main; trunk appears there only after it was published.
                ReadSync = (expected, remote, branch) =>
                {
                    var trunk = new SyncBranch("refs/heads/trunk", "refs/remotes/" + remote + "/trunk") { IsNew = !RemoteHasTrunk };
                    var main = new SyncBranch("refs/heads/main", "refs/remotes/" + remote + "/main");
                    var selected = branch ?? (expected.TrackingRemote == remote ? expected.UpstreamBranchRef : null) ?? (trunk.IsNew ? trunk.SourceRef : null);
                    var chosen = new[] { trunk, main }.FirstOrDefault(item => item.SourceRef == selected);
                    int? count = chosen is null || chosen.IsNew ? null : 0;
                    return new(new(remote, "https://fetch.example.invalid/" + remote, "https://fetch.example.invalid/" + remote, "config"), [trunk, main], chosen, count, count, "Local comparison");
                }
            };
            Workspace.Handle = request =>
            {
                if (request is PublishRequest { Publish: { } publish } && Result.Outcome == GitWriteOutcome.Succeeded)
                {
                    if (publish.Kind != PublishKind.ExistingRemote)
                    {
                        var url = publish.RemoteUrl ?? "https://github.com/contoso/" + publish.RepositoryName + ".git";
                        Remotes = [.. Remotes, new(publish.RemoteName, url, url)];
                    }
                    var index = Remotes.ToList().FindIndex(remote => remote.Name == publish.RemoteName);
                    Remotes = [Remotes[index], .. Remotes.Where((_, i) => i != index)];
                    Tracking = true; UpstreamName = publish.RemoteBranch ?? "trunk";
                    if (UpstreamName == "trunk") RemoteHasTrunk = true;
                }
                return Result;
            };
        }
    }
}
