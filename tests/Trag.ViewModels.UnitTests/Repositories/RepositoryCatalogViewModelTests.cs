using Trag.Domain.Accounts;
using Trag.Domain.Catalog;
using Trag.Domain.Presentation;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Repositories;

namespace Trag.ViewModels.UnitTests.Repositories;

public sealed class RepositoryCatalogViewModelTests
{
    [Fact]
    public async Task WhenARepositoryIsAdded_ShouldPublishOneUpdateWithTheCompleteRow()
    {
        var h = Harness(); var host = new RecordingHost { Folder = Folder("alpha") };
        await using var model = h.Shell(); model.Host = host;
        var updates = 0;
        model.Catalog.Local.RowsChanged += () =>
        {
            updates++;
            Assert.True(Assert.Single(model.Catalog.Local.LocalRows).IsValid);
        };
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(1, updates);
    }

    // The add leaves a valid repository selected, so only the clone keeps the open command from running.
    [Fact]
    public async Task WhenOnlyTheCatalogMessageChanges_ShouldNotReaskTheOpenCommandButACloneShould()
    {
        var h = CloneHarness(); var host = new RecordingHost { Folder = Folder("alpha") }; h.Catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell(); model.Host = host;
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        var clone = await BeginCloneAsync(model);
        var invalidations = 0;
        model.Catalog.OpenSelectedRepositoryCommand.CanExecuteChanged += (_, _) => invalidations++;
        model.Catalog.State.Message = "Information only";
        Assert.Equal(0, invalidations);
        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null); await h.Catalog.CloneGate.Started;
        Assert.NotEqual(0, invalidations);
        Assert.False(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        h.Catalog.CloneGate.Open(); await pending;
    }

    [Fact]
    public async Task WhenTheAccountFilterSwitches_ShouldRebuildTheVisibleRowsOnceAndKeepTheSearch()
    {
        var h = Harness(); var catalog = h.Catalog;
        for (var i = 0; i < 2; i++)
        {
            var account = new AccountInfo("account-" + i, AccountProvider.GitHub, "7", "user" + i,
                new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
            catalog.Accounts.Add(account);
            catalog.Remote[account.Id] = [new("user" + i + "/match", "https://github.com/user/match.git", "https://github.com/user/match", "main", false, null)];
        }
        await using var model = h.Shell();
        await model.Catalog.Load();
        foreach (var group in model.Catalog.AccountGroups) await group.LoadCommand.ExecuteAsync(null);
        var first = model.Catalog.AccountGroups[0];
        var second = model.Catalog.AccountGroups[1];
        model.Catalog.Filter.CatalogSearch = "match";
        var updates = 0;
        first.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AccountGroup.VisibleRows)) updates++; };
        model.Catalog.Filter.ShowAccountCommand.Execute(first);
        Assert.Equal(1, updates);
        Assert.Single(first.VisibleRows);
        model.Catalog.Filter.ShowAccountCommand.Execute(second);
        Assert.Empty(first.VisibleRows);
        Assert.Single(second.VisibleRows);
        Assert.Same(second, model.Catalog.Filter.FilterGroup);
        Assert.Equal("match", model.Catalog.Filter.CatalogSearch);
    }

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "trag-catalog-tests");
    private static string Folder(string name) => Path.Combine(Root, name);

    [Fact]
    public async Task WhenStartingWithoutALastRepository_ShouldShowThePickerOpenOnlyAValidEntryAndComeBackOnOpenAndClose()
    {
        var h = Harness(Entry("alpha"), Entry("gone")); var catalog = h.Catalog;
        catalog.Probes[Folder("gone")] = new(RepositoryLinkStatus.Missing);
        await using var model = h.Shell();
        await model.InitializeAsync(); await model.Catalog.CatalogCompletion;
        Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section); Assert.False(model.Repository.HasRepository);
        var alpha = model.Catalog.Local.LocalRows.Single(row => row.Name == "alpha"); var gone = model.Catalog.Local.LocalRows.Single(row => row.Name == "gone");
        Assert.True(alpha.IsValid); Assert.Equal("main", alpha.BranchText); Assert.Equal("Missing", gone.StatusText); Assert.True(gone.IsBroken);
        Assert.False(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        model.Catalog.State.SelectedLocal = gone; Assert.False(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null)); Assert.Same(gone, model.Catalog.State.SelectedLocal);
        model.Catalog.State.SelectedLocal = alpha; Assert.True(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        await model.Catalog.OpenSelectedRepositoryCommand.ExecuteAsync(null);
        Assert.True(model.Repository.HasRepository); Assert.NotEqual(WorkspaceSection.Repositories, model.Navigator.Section); Assert.Equal(Folder("alpha"), model.Repository.Snapshot!.Root);
        Assert.NotNull(catalog.Saved!.Single(entry => entry.Path == Folder("alpha")).LastOpenedAt);
        Assert.Equal("https://github.com/acme/alpha.git", catalog.Saved!.Single(entry => entry.Path == Folder("alpha")).RemoteUrl);
        model.OpenCommand.Execute(null); await model.Catalog.CatalogCompletion;
        Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section); Assert.True(model.Catalog.BackCommand.CanExecute(null));
        Assert.Same(alpha, model.Catalog.Local.LocalRows[0]);
        model.Catalog.BackCommand.Execute(null); Assert.Equal(WorkspaceSection.FileStatus, model.Navigator.Section); Assert.True(model.Repository.HasRepository);
        await model.CloseRepositoryCommand.ExecuteAsync(null); await model.Catalog.CatalogCompletion;
        Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section); Assert.False(model.Repository.HasRepository);
    }

    // A listed repository that does not open leaves the list shown, saying why.
    [Fact]
    public async Task WhenAListedRepositoryFailsToOpen_ShouldStayOnTheListAndSayWhy()
    {
        var h = Harness(Entry("alpha"));
        await using var model = h.Shell();
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        h.Reader.Read = (_, _) => Task.FromException<RepositorySnapshot>(new IOException("The folder is gone."));

        await model.Catalog.OpenEntryCommand.ExecuteAsync(Assert.Single(model.Catalog.Local.LocalRows));

        Assert.Equal(WorkspaceSection.Repositories, model.Navigator.Section);
        Assert.NotEqual("", model.StatusBar.ErrorMessage);
        Assert.Equal(model.StatusBar.ErrorMessage, model.Catalog.State.Message);
    }

    // Back needs an open repository first.
    [Fact]
    public async Task WhenAMergeIsInProgress_ShouldGoBackToTheMergeView()
    {
        var h = Harness(Entry("alpha"));
        h.Reader.Read = (_, _) => Task.FromResult(Snapshots.Create(files: [Snapshots.Conflict("a.txt")], operation: RepositoryOperation.Merge,
            merge: new("incoming", Snapshots.HeadId, "Merge branch 'topic'", "fp", "")));
        await using var model = h.Shell();
        model.Navigator.ShowRepositories();
        Assert.False(model.Catalog.BackCommand.CanExecute(null));
        await model.Session.OpenRepositoryAsync(Folder("alpha"));
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section);
        model.Navigator.ShowRepositories();
        Assert.True(model.Catalog.BackCommand.CanExecute(null));
        model.Catalog.BackCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Merge, model.Navigator.Section); Assert.False(model.Catalog.BackCommand.CanExecute(null));
    }

    // A rebase in progress is the repository's own view too.
    [Fact]
    public async Task WhenARebaseIsInProgress_ShouldGoBackToTheRebaseView()
    {
        var h = Harness(Entry("alpha"));
        h.Reader.Read = (_, _) => Task.FromResult(Snapshots.Rebasing(files: [Snapshots.Conflict("a.txt")]));
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync(Folder("alpha"));
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
        model.Navigator.ShowRepositories();
        model.Catalog.BackCommand.Execute(null);
        Assert.Equal(WorkspaceSection.Rebase, model.Navigator.Section);
    }

    // A repository Git refuses for its ownership is not a plain folder: initializing it is not offered, and the list says
    // why it cannot be added; one already listed shows the same reason and cannot be opened.
    [Fact]
    public async Task WhenGitDoesNotTrustAFoldersOwnership_ShouldSaySoInsteadOfOfferingToInitializeIt()
    {
        var h = Harness(Entry("listed")); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("owned") };
        catalog.Probes[Folder("owned")] = new(RepositoryLinkStatus.Untrusted);
        catalog.Probes[Folder("listed")] = new(RepositoryLinkStatus.Untrusted);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;

        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        Assert.False(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal("Git does not trust this folder's ownership. Review it in your Git tools, then add it again.", model.Catalog.State.Message);
        var listed = Assert.Single(model.Catalog.Local.LocalRows);
        Assert.Equal(("Not trusted", "Git does not trust this folder's ownership · review it in your Git tools", true),
            (listed.StatusText, listed.LinkText, listed.IsBroken));
    }

    // A repository in a format only a newer Git reads is not a plain folder either: initializing it is not offered, and the
    // list says to choose a newer Git; one already listed shows the same reason and cannot be opened.
    [Fact]
    public async Task WhenTheRepositoryNeedsANewerGit_ShouldSaySoInsteadOfOfferingToInitializeIt()
    {
        var h = Harness(Entry("listed")); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("reftable") };
        catalog.Probes[Folder("reftable")] = new(RepositoryLinkStatus.NeedsNewerGit);
        catalog.Probes[Folder("listed")] = new(RepositoryLinkStatus.NeedsNewerGit);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;

        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        Assert.False(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal("This repository needs a newer Git than the one Trag uses. Choose a newer git.exe in Settings → Git, then add it again.",
            model.Catalog.State.Message);
        var listed = Assert.Single(model.Catalog.Local.LocalRows);
        Assert.Equal(("Needs newer Git", "This repository needs a newer Git · choose one in Settings → Git", true),
            (listed.StatusText, listed.LinkText, listed.IsBroken));
    }

    // A bare repository is not a plain folder either: initializing it would nest a new repository inside it, so the list
    // says it has no working tree instead; one already listed shows the same reason as a broken link.
    [Fact]
    public async Task WhenABareRepositoryIsAdded_ShouldSayItHasNoWorkingTreeInsteadOfOfferingToInitializeIt()
    {
        var h = Harness(Entry("listed")); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("bare") };
        catalog.Probes[Folder("bare")] = new(RepositoryLinkStatus.Bare);
        catalog.Probes[Folder("listed")] = new(RepositoryLinkStatus.Bare);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;

        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        Assert.False(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal("This folder is a bare repository, which has no working tree. Add a working copy of it instead.", model.Catalog.State.Message);
        var listed = Assert.Single(model.Catalog.Local.LocalRows);
        Assert.Equal(("Bare repository", "This folder is a bare repository with no working tree · add a working copy instead", true),
            (listed.StatusText, listed.LinkText, listed.IsBroken));
    }

    // A working copy's .git folder is not a plain folder either: initializing it would nest a new repository inside it, so
    // the list says what it holds instead; one already listed shows the same reason as a broken link.
    [Fact]
    public async Task WhenAWorkingCopysGitFolderIsAdded_ShouldSayItHoldsGitDataInsteadOfOfferingToInitializeIt()
    {
        var h = Harness(Entry("listed")); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("work/.git") };
        catalog.Probes[Folder("work/.git")] = new(RepositoryLinkStatus.GitDirectory);
        catalog.Probes[Folder("listed")] = new(RepositoryLinkStatus.GitDirectory);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;

        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        Assert.False(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal("This folder holds a repository's Git data, not its files. Add the working copy instead.", model.Catalog.State.Message);
        var listed = Assert.Single(model.Catalog.Local.LocalRows);
        Assert.Equal(("Git data folder", "This folder holds a repository's Git data, not its files · add the working copy instead", true),
            (listed.StatusText, listed.LinkText, listed.IsBroken));
    }

    [Fact]
    public async Task WhenFoldersAreAdded_ShouldAddRepositoriesOnceAndInitializeAPlainFolderOnlyAfterConfirmation()
    {
        var h = Harness(); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("plain") };
        catalog.Probes[Folder("plain")] = new(RepositoryLinkStatus.NotRepository);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        Assert.Empty(model.Catalog.Local.LocalRows);
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.True(model.Catalog.Local.Initialization.IsOpen); Assert.Empty(catalog.Initialized); Assert.Empty(model.Catalog.Local.LocalRows);
        model.Catalog.Local.Initialization.CancelCommand.Execute(null); Assert.False(model.Catalog.Local.Initialization.IsOpen);
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        await model.Catalog.Local.Initialization.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal([Folder("plain")], catalog.Initialized);
        var row = Assert.Single(model.Catalog.Local.LocalRows); Assert.True(row.IsValid); Assert.Same(row, model.Catalog.State.SelectedLocal); Assert.False(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal(Folder("plain"), Assert.Single(catalog.Saved!).Path);
        host.Folder = Folder("alpha");
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Catalog.Local.LocalRows.Count); Assert.Equal("alpha", model.Catalog.State.SelectedLocal!.Name); Assert.Contains("added", model.Catalog.State.Message);
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Catalog.Local.LocalRows.Count); Assert.Contains("already", model.Catalog.State.Message);
    }

    // An initialization that fails keeps its question open with why, for another try.
    [Fact]
    public async Task WhenInitializingAFolderFails_ShouldKeepTheQuestionOpenWithWhy()
    {
        var h = Harness(); var host = new RecordingHost { Folder = Folder("plain") };
        h.Catalog.Probes[Folder("plain")] = new(RepositoryLinkStatus.NotRepository);
        h.Catalog.InitializeError = new IOException("denied");
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        await model.Catalog.Local.Initialization.ConfirmCommand.ExecuteAsync(null);

        Assert.True(model.Catalog.Local.Initialization.IsOpen);
        Assert.Equal(CatalogText.InitializeFailed, model.Catalog.Local.Initialization.Message);
    }

    // A repository added while the question about a plain folder is open answers it: the question goes.
    [Fact]
    public async Task WhenARepositoryIsAddedWhileTheInitializeQuestionIsOpen_ShouldCloseTheQuestion()
    {
        var h = Harness(); var host = new RecordingHost { Folder = Folder("plain") };
        h.Catalog.Probes[Folder("plain")] = new(RepositoryLinkStatus.NotRepository);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.True(model.Catalog.Local.Initialization.IsOpen);

        host.Folder = Folder("alpha");
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.False(model.Catalog.Local.Initialization.IsOpen);
    }

    // A check of the folder that was cancelled adds nothing and says nothing over what the picker said before.
    [Fact]
    public async Task WhenTheFolderCheckIsCancelled_ShouldAddNothingAndKeepTheMessage()
    {
        var h = Harness(); var host = new RecordingHost { Folder = Folder("alpha") };
        h.Catalog.ReadProbe = (_, _) => Task.FromException<RepositoryProbe>(new OperationCanceledException());
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        model.Catalog.State.Message = "Before";
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        Assert.Empty(model.Catalog.Local.LocalRows); Assert.Equal("Before", model.Catalog.State.Message);
    }

    // A full list says why the folder is not added and how to make room.
    [Fact]
    public async Task WhenTheListIsFull_ShouldSayWhyTheFolderIsNotAdded()
    {
        var h = Harness(); var host = new RecordingHost { Folder = Folder("alpha") };
        h.Catalog.Entries.AddRange(Enumerable.Range(0, RepositoryEntry.MaximumListed).Select(i => new RepositoryEntry(Folder("listed-" + i), "listed-" + i, DateTimeOffset.UtcNow)));
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;

        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(CatalogText.ListFull, model.Catalog.State.Message);
        Assert.Equal(RepositoryEntry.MaximumListed, model.Catalog.Local.LocalRows.Count);
    }

    // The list read again while a folder is being initialized (the Repositories section chosen once more) ends before
    // the initialization does; the picker stays busy until both are done.
    [Fact]
    public async Task WhenTheListIsReadAgainDuringAnInitialization_ShouldStayBusyUntilTheInitializationEnds()
    {
        var h = Harness(); var catalog = h.Catalog; var host = new RecordingHost { Folder = Folder("plain") };
        catalog.Probes[Folder("plain")] = new(RepositoryLinkStatus.NotRepository);
        await using var model = h.Shell(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        await model.Catalog.Local.AddRepositoryCommand.ExecuteAsync(null);
        catalog.InitializeGate.IsClosed = true;
        var initializing = model.Catalog.Local.Initialization.ConfirmCommand.ExecuteAsync(null);
        await catalog.InitializeGate.Started;
        Assert.True(model.Catalog.State.IsBusy);
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        Assert.True(model.Catalog.State.IsBusy);
        catalog.InitializeGate.Open(); await initializing;
        Assert.False(model.Catalog.State.IsBusy);
        Assert.Equal([Folder("plain")], catalog.Initialized);
    }

    [Fact]
    public async Task WhenARepositoryIsRemoved_ShouldAskFirstAndRemoveOnlyTheListEntry()
    {
        var h = Harness(Entry("alpha"), Entry("beta")); var catalog = h.Catalog;
        await using var model = h.Shell();
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var saves = catalog.Saves;
        Assert.False(model.Catalog.Local.RequestRemoveRepositoryCommand.CanExecute(null));
        var beta = model.Catalog.Local.LocalRows.Single(row => row.Name == "beta");
        model.Catalog.State.SelectedLocal = beta;
        model.Catalog.Local.RequestRemoveRepositoryCommand.Execute(null);
        Assert.True(model.Catalog.Local.Removal.IsOpen); Assert.Contains("stay on disk", model.Catalog.Local.Removal.Text);
        model.Catalog.Local.Removal.CancelCommand.Execute(null);
        Assert.False(model.Catalog.Local.Removal.IsOpen); Assert.Equal(2, model.Catalog.Local.LocalRows.Count); Assert.Equal(saves, catalog.Saves);
        model.Catalog.Local.RequestRemoveRepositoryCommand.Execute(beta);
        await model.Catalog.Local.Removal.ConfirmCommand.ExecuteAsync(null);
        Assert.Single(model.Catalog.Local.LocalRows); Assert.Null(model.Catalog.State.SelectedLocal);
        Assert.Contains("not changed", model.Catalog.State.Message);
    }

    // A folder that is gone or a page off the account's host is refused, and a detached host hears nothing.
    [Fact]
    public async Task WhenTheCatalogAsksForAWindowService_ShouldReachTheAttachedHostAndTurnUnsafeRequestsIntoMessages()
    {
        var h = Harness(Entry("alpha"), new RepositoryEntry(Path.GetTempPath(), "temp", DateTimeOffset.UtcNow.AddDays(-2))); var catalog = h.Catalog;
        var account = new AccountInfo("account-1", AccountProvider.GitHub, "7", "contoso", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
        catalog.Accounts.Add(account);
        catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null),
            new("contoso/odd", "https://github.com/contoso/odd.git", "http://elsewhere.invalid/odd", "main", true, null)];
        await using var model = h.Shell();
        var host = new RecordingHost(); model.Host = host;
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        model.Catalog.RequestSettingsCommand.Execute(null);
        Assert.Equal(1, host.Settings);
        model.Catalog.RevealCommand.Execute(model.Catalog.Local.LocalRows.Single(row => row.Name == "temp"));
        Assert.True(Directory.Exists(Assert.Single(host.Revealed)));
        model.Catalog.RevealCommand.Execute(model.Catalog.Local.LocalRows.Single(row => row.Name == "alpha"));
        Assert.Single(host.Revealed); Assert.Equal("This folder is no longer available.", model.Catalog.State.Message);
        var group = Assert.Single(model.Catalog.AccountGroups); await group.LoadCommand.ExecuteAsync(null);
        model.Catalog.OpenWebCommand.Execute(group.Rows.Single(row => row.FullName == "contoso/sandbox"));
        Assert.Equal([new Uri("https://github.com/contoso/sandbox")], host.Opened);
        model.Catalog.OpenWebCommand.Execute(group.Rows.Single(row => row.FullName == "contoso/odd"));
        Assert.Single(host.Opened); Assert.Equal("This repository address cannot be opened safely.", model.Catalog.State.Message);
        model.Host = null;
        model.Catalog.RequestSettingsCommand.Execute(null);
        Assert.Equal(1, host.Settings);
    }

    // Nothing is open, then a repository is: the terminal opens in its folder.
    [Fact]
    public async Task WhenATerminalIsAskedFor_ShouldOpenItInTheOpenRepositorysFolder()
    {
        var h = Harness();
        await using var model = h.Shell();
        var host = new RecordingHost(); model.Host = host;
        model.OpenTerminalCommand.Execute(null);
        Assert.Empty(host.Terminals);

        await model.Session.OpenRepositoryAsync("fixture");
        model.OpenTerminalCommand.Execute(null);

        Assert.Equal(["fixture"], host.Terminals);
    }

    [Fact]
    public async Task WhenAnAccountsRepositoriesAreLoadedSelectedAndCloned_ShouldMatchLocalClonesAndOpenTheNewClone()
    {
        var h = Harness(Entry("alpha")); var catalog = h.Catalog;
        catalog.Probes[Folder("alpha")] = new(RepositoryLinkStatus.Valid, "main", "git@github.com:Acme/Alpha.git", Folder("alpha"));
        var account = new AccountInfo("account-1", AccountProvider.GitHub, "7", "contoso", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
        catalog.Accounts.Add(account);
        catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow.AddDays(-21)),
            new("acme/alpha", "https://github.com/acme/alpha.git", "https://github.com/acme/alpha", "main", false, DateTimeOffset.UtcNow.AddDays(-3))];
        await using var model = h.Shell();
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var group = Assert.Single(model.Catalog.AccountGroups);
        Assert.Equal("GitHub · contoso (0)", group.HeaderText); Assert.Equal("1", model.Catalog.AllCountText);
        await group.LoadCommand.ExecuteAsync(null);
        Assert.Equal(2, group.Rows.Count); Assert.Equal("3", model.Catalog.AllCountText);
        var alphaRemote = group.Rows.Single(row => row.FullName == "acme/alpha"); var sandbox = group.Rows.Single(row => row.FullName == "contoso/sandbox");
        Assert.Equal("git@github.com:Acme/Alpha.git", model.Catalog.Local.LocalRows[0].Entry.RemoteUrl);
        Assert.True(alphaRemote.IsCloned); Assert.False(sandbox.IsCloned); Assert.Equal("Updated 3 weeks ago", sandbox.SubtitleText);
        group.SelectedRow = alphaRemote;
        Assert.Same(alphaRemote, model.Catalog.State.SelectedRemote); Assert.Null(model.Catalog.State.SelectedLocal); Assert.True(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        model.Catalog.State.SelectedLocal = model.Catalog.Local.LocalRows[0];
        Assert.Null(group.SelectedRow); Assert.Null(model.Catalog.State.SelectedRemote); Assert.NotNull(model.Catalog.State.SelectedLocal);
        group.SelectedRow = sandbox;
        Assert.Null(model.Catalog.State.SelectedLocal); Assert.False(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        model.Catalog.BeginCloneCommand.Execute(sandbox);
        Assert.True(model.Catalog.Clone.IsCloneDialogOpen); Assert.Equal(Path.Combine(Root, "contoso", "sandbox"), model.Catalog.Clone.CloneTarget); Assert.True(model.Catalog.Clone.CloneOpenAfter);
        catalog.CloneOutcome = new(false, "Git authentication failed.", "fatal: detail");
        await model.Catalog.Clone.ConfirmCloneCommand.ExecuteAsync(null);
        Assert.True(model.Catalog.Clone.IsCloneDialogOpen); Assert.Equal("Git authentication failed.", model.Catalog.Clone.CloneStatus); Assert.Equal("fatal: detail", model.Catalog.Clone.CloneDetails); Assert.Single(model.Catalog.Local.LocalRows);
        catalog.CloneOutcome = new(true, "Repository cloned.");
        await model.Catalog.Clone.ConfirmCloneCommand.ExecuteAsync(null);
        var request = catalog.Clones[^1];
        Assert.Equal("https://github.com/contoso/sandbox.git", request.CloneUrl); Assert.Equal(account.Id, request.AccountId); Assert.Equal(Path.Combine(Root, "contoso", "sandbox"), request.TargetFolder);
        Assert.False(model.Catalog.Clone.IsCloneDialogOpen); Assert.True(model.Repository.HasRepository); Assert.Equal(request.TargetFolder, model.Repository.Snapshot!.Root);
        Assert.Contains(catalog.Saved!, entry => entry.Path == request.TargetFolder);
        Assert.True(sandbox.IsCloned);
    }

    // The local row and the hosted row alike.
    [Fact]
    public async Task WhenRowsAreShown_ShouldShowADashForAMissingBranchAndAFolderOrABookForWhetherAHostedRowIsCloned()
    {
        var h = Harness(Entry("alpha")); var catalog = h.Catalog;
        catalog.Probes[Folder("alpha")] = new(RepositoryLinkStatus.Valid, null, "https://github.com/acme/alpha.git", Folder("alpha"));
        var account = new AccountInfo("account-1", AccountProvider.GitHub, "7", "contoso", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null);
        catalog.Accounts.Add(account);
        catalog.Remote[account.Id] = [new("acme/alpha", "https://github.com/acme/alpha.git", "https://github.com/acme/alpha", null, false, DateTimeOffset.UtcNow),
            new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, DateTimeOffset.UtcNow)];
        await using var model = h.Shell();
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);

        Assert.Equal("—", Assert.Single(model.Catalog.Local.LocalRows).BranchText);
        var alpha = group.Rows.Single(row => row.FullName == "acme/alpha"); var sandbox = group.Rows.Single(row => row.FullName == "contoso/sandbox");
        Assert.Equal((true, "—", "folder"), (alpha.IsCloned, alpha.DefaultBranchText, alpha.IconKind));
        Assert.Equal((false, "main", "book"), (sandbox.IsCloned, sandbox.DefaultBranchText, sandbox.IconKind));
    }

    [Fact]
    public async Task WhenSearchingOrFiltering_ShouldNarrowTheLocalAndTheHostedLists()
    {
        var h = SearchHarness();
        await using var model = h.Shell();
        var group = await ShowAndLoadAsync(model);
        model.Catalog.Filter.CatalogSearch = "ALP";
        Assert.Single(model.Catalog.Filter.VisibleLocalRows); Assert.Single(group.VisibleRows);
        model.Catalog.Filter.CatalogSearch = "";
        model.Catalog.Filter.ShowLocalRepositoriesCommand.Execute(null);
        Assert.True(model.Catalog.Filter.IsLocalFilter); Assert.False(group.IsVisible); Assert.Equal(2, model.Catalog.Filter.VisibleLocalRows.Count);
        model.Catalog.Filter.ShowAccountCommand.Execute(group);
        Assert.True(group.IsFiltered); Assert.True(group.IsVisible); Assert.False(model.Catalog.Filter.ShowLocalGroup); Assert.Empty(model.Catalog.Filter.VisibleLocalRows);
        model.Catalog.Filter.ShowAllRepositoriesCommand.Execute(null);
        Assert.True(model.Catalog.Filter.IsAllFilter); Assert.False(group.IsFiltered); Assert.Equal(2, model.Catalog.Filter.VisibleLocalRows.Count);
    }

    [Fact]
    public async Task WhenTheCatalogRefreshes_ShouldCheckTheLinksAgainAndSaveWhatChanged()
    {
        var h = SearchHarness(); var catalog = h.Catalog;
        await using var model = h.Shell();
        NarrowAndWiden(model, await ShowAndLoadAsync(model));
        catalog.Probes[Folder("beta")] = new(RepositoryLinkStatus.Valid, "feature", null, Folder("beta"));
        model.Catalog.RefreshCatalogCommand.Execute(null); await model.Catalog.CatalogCompletion;
        Assert.Equal("feature", model.Catalog.Local.LocalRows.Single(row => row.Name == "beta").BranchText);
        Assert.Equal("feature", catalog.Saved!.Single(entry => entry.Name == "beta").Branch);
    }

    // Two saved repositories and one GitLab account whose one repository the search finds with the first of them.
    private static ShellHarness SearchHarness()
    {
        var h = Harness(Entry("alpha"), Entry("beta"));
        var account = new AccountInfo("account-1", AccountProvider.GitLab, "1", "work", new("https://gitlab.com/api/v4/"), new("https://gitlab.com/"), true, DateTimeOffset.UtcNow, null);
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("team/alpha-service", "https://gitlab.com/team/alpha-service.git", "https://gitlab.com/team/alpha-service", "main", true, null)];
        return h;
    }

    private static async Task<AccountGroup> ShowAndLoadAsync(ShellViewModel model)
    {
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);
        return group;
    }

    // The search and the filters the narrowing test sets, ending on all repositories again.
    private static void NarrowAndWiden(ShellViewModel model, AccountGroup group)
    {
        model.Catalog.Filter.CatalogSearch = "ALP";
        model.Catalog.Filter.CatalogSearch = "";
        model.Catalog.Filter.ShowLocalRepositoriesCommand.Execute(null);
        model.Catalog.Filter.ShowAccountCommand.Execute(group);
        model.Catalog.Filter.ShowAllRepositoriesCommand.Execute(null);
    }

    // Submodules are cloned too unless the user leaves them out.
    [Fact]
    public async Task WhenACloneIsConfirmed_ShouldCloneTheSubmodulesTooUnlessTheyWereLeftOut()
    {
        var h = CloneHarness(); var catalog = h.Catalog;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);
        catalog.CloneOutcome = new(false, "Git authentication failed.");

        await clone.ConfirmCloneCommand.ExecuteAsync(null);
        clone.CloneSubmodules = false;
        await clone.ConfirmCloneCommand.ExecuteAsync(null);

        Assert.Equal([true, false], catalog.Clones.Select(request => request.Submodules));
    }

    [Theory]
    [InlineData("dialog")]
    [InlineData("global")]
    public async Task WhenARunningCloneIsCanceled_ShouldStopItAndKeepTheDialogReadyForARetry(string source)
    {
        var h = CloneHarness(); var catalog = h.Catalog;
        catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);

        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null);
        await catalog.CloneGate.Started;
        Assert.Same(pending, clone.ConfirmCloneCommand.ExecutionTask);
        Assert.True(clone.ConfirmCloneCommand.IsRunning);
        Assert.True(clone.State.IsCloning);
        Assert.False(clone.CanEditClone);
        Assert.False(clone.ConfirmCloneCommand.CanExecute(null));
        Assert.True(model.CancelCommand.CanExecute(null));

        switch (source)
        {
            case "dialog": clone.CancelCloneCommand.Execute(null); break;
            case "global": model.CancelCommand.Execute(null); break;
        }

        Assert.True(catalog.CloneGate.Token.IsCancellationRequested);
        catalog.CloneGate.Cancel();
        await pending;

        Assert.False(clone.State.IsCloning);
        Assert.False(clone.ConfirmCloneCommand.IsRunning);
        Assert.True(clone.IsCloneDialogOpen);
        Assert.True(clone.CanEditClone);
        Assert.True(clone.ConfirmCloneCommand.CanExecute(null));
        Assert.Equal("Clone canceled.", clone.CloneStatus);
        Assert.Empty(model.Catalog.Local.LocalRows);
        Assert.False(model.Repository.HasRepository);
        clone.CancelCloneCommand.Execute(null);
        Assert.False(clone.IsCloneDialogOpen);
        Assert.Null(clone.CloneSource);
    }

    [Fact]
    public async Task WhenTheShellCancelsWhileTheRepositoryListReads_ShouldStopTheReadAndLeaveNothingToCancel()
    {
        var h = Harness(Entry("alpha")); var catalog = h.Catalog;
        catalog.AccountsGate.IsClosed = true;
        await using var model = h.Shell();
        model.Navigator.ShowRepositories();
        await catalog.AccountsGate.Started;
        Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(catalog.AccountsGate.Token.IsCancellationRequested); Assert.False(model.CancelCommand.CanExecute(null));
        catalog.AccountsGate.Open();
        await model.Catalog.CatalogCompletion;
    }

    [Fact]
    public async Task WhenTheShellCancelsWhileAnAccountsRepositoriesLoad_ShouldStopTheLoad()
    {
        var h = Harness(); var catalog = h.Catalog;
        catalog.Accounts.Add(new AccountInfo("account-1", AccountProvider.GitHub, "7", "contoso", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null));
        var remote = new AsyncGate { IsClosed = true };
        catalog.ReadRemote = async (_, ct) => { await remote.WaitAsync(ct); return new([], ReachedLimit: false); };
        await using var model = h.Shell();
        model.Navigator.ShowRepositories(); await model.Catalog.CatalogCompletion;
        var load = Assert.Single(model.Catalog.AccountGroups).LoadCommand.ExecuteAsync(null);
        await remote.Started;
        Assert.True(model.CancelCommand.CanExecute(null));
        model.CancelCommand.Execute(null);
        Assert.True(remote.Token.IsCancellationRequested);
        remote.Open();
        await load;
    }

    [Fact]
    public async Task WhenACloneOpensTheNewRepository_ShouldReleaseTheCatalogButKeepTheCommandRunningUntilTheOpenEnds()
    {
        var h = CloneHarness(); var catalog = h.Catalog; var reader = h.Reader;
        reader.Gate.IsClosed = true;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);

        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null);
        await reader.Gate.Started;
        Assert.False(clone.State.IsCloning);
        Assert.False(clone.IsCloneDialogOpen);
        Assert.True(clone.ConfirmCloneCommand.IsRunning);
        Assert.False(pending.IsCompleted);
        Assert.Single(model.Catalog.Local.LocalRows);
        reader.Gate.Open();
        await pending;

        Assert.True(model.Repository.HasRepository);
        Assert.Equal(Assert.Single(catalog.Clones).TargetFolder, model.Repository.Snapshot!.Root);
        Assert.False(clone.ConfirmCloneCommand.IsRunning);
    }

    [Fact]
    public async Task WhenACloneSucceedsDespiteALateCancelWithOpenAfterOff_ShouldRecordItWithoutOpeningIt()
    {
        var h = CloneHarness(); var catalog = h.Catalog;
        catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);
        clone.CloneOpenAfter = false;

        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null);
        await catalog.CloneGate.Started;
        clone.CancelCloneCommand.Execute(null);
        catalog.CloneGate.Open(); // Git completed successfully despite the cancellation request.
        await pending;

        Assert.Single(model.Catalog.Local.LocalRows);
        Assert.Single(catalog.Saved!);
        Assert.False(clone.IsCloneDialogOpen);
        Assert.False(model.Repository.HasRepository);
        Assert.Equal("Repository cloned.", model.StatusBar.Status);
    }

    // With no repository listed, the clone goes under Repos in the home folder the catalog names.
    [Fact]
    public async Task WhenNoRepositoryIsListed_ShouldProposeTheCloneUnderReposInTheCatalogsHomeFolder()
    {
        var h = CloneHarness(); h.Catalog.HomeFolder = Path.Combine(Path.GetTempPath(), "trag-clone-home");
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);
        Assert.Equal(Path.Combine(h.Catalog.HomeFolder, "Repos", "contoso", "sandbox"), clone.CloneTarget);
    }

    // A folder chosen in the window's dialog takes the clone under it, by the name its address gives.
    [Fact]
    public async Task WhenAFolderIsChosenForTheClone_ShouldProposeTheCloneUnderIt()
    {
        var h = CloneHarness(); var host = new RecordingHost { Folder = Path.Combine(Path.GetTempPath(), "trag-clone-chosen") };
        await using var model = h.Shell(); model.Host = host;
        var clone = await BeginCloneAsync(model);

        await clone.PickCloneFolderCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(host.Folder, "sandbox"), clone.CloneTarget);
    }

    [Fact]
    public async Task WhenACloneThrows_ShouldKeepTheSourceAndTheTargetForReviewAndARetry()
    {
        var h = CloneHarness(); var catalog = h.Catalog;
        catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);
        var source = clone.CloneSource;
        var target = clone.CloneTarget;

        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null);
        await catalog.CloneGate.Started;
        catalog.CloneGate.Fail(new IOException("Fixture failure"));
        await pending;

        Assert.Contains("Review the target folder", clone.CloneStatus);
        Assert.Same(source, clone.CloneSource);
        Assert.Equal(target, clone.CloneTarget);
        Assert.True(clone.IsCloneDialogOpen);
        Assert.False(clone.State.IsCloning);
        Assert.True(clone.ConfirmCloneCommand.CanExecute(null));
        Assert.Empty(model.Catalog.Local.LocalRows);
    }

    // The row announces every text its status and its entry feed, and the commands that read the selection are asked
    // again, all before the read ends.
    [Fact]
    public async Task WhenOneLinkAnswersWhileAnotherIsStillChecked_ShouldShowItInItsRowAndReaskTheCommandsAtOnce()
    {
        var h = Harness(Entry("alpha"), Entry("beta")); var catalog = h.Catalog;
        var alpha = new TaskCompletionSource<RepositoryProbe>();
        var beta = new TaskCompletionSource<RepositoryProbe>();
        catalog.ReadProbe = (path, _) => path == Folder("alpha") ? alpha.Task : beta.Task;
        await using var model = h.Shell();
        var loading = model.Catalog.Load();
        var row = model.Catalog.Local.LocalRows.Single(candidate => candidate.Name == "alpha");
        model.Catalog.State.SelectedLocal = row;
        Assert.False(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));
        var changes = new List<string?>();
        row.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var asked = 0;
        model.Catalog.OpenSelectedRepositoryCommand.CanExecuteChanged += (_, _) => asked++;

        alpha.SetResult(new(RepositoryLinkStatus.Valid, "trunk", "https://github.com/acme/alpha.git", Folder("alpha")));
        Assert.False(loading.IsCompleted);
        Assert.Superset(new HashSet<string?>
        {
            nameof(LocalRepositoryRow.Status), nameof(LocalRepositoryRow.IsValid), nameof(LocalRepositoryRow.IsBroken),
            nameof(LocalRepositoryRow.StatusText), nameof(LocalRepositoryRow.LinkText), nameof(LocalRepositoryRow.Entry),
            nameof(LocalRepositoryRow.BranchText), nameof(LocalRepositoryRow.RemoteText), nameof(LocalRepositoryRow.IsChecking)
        }, changes.ToHashSet());
        Assert.Equal("trunk", row.BranchText);
        Assert.True(asked > 0);
        Assert.True(model.Catalog.OpenSelectedRepositoryCommand.CanExecute(null));

        beta.SetResult(new(RepositoryLinkStatus.Valid, "main", null, Folder("beta")));
        await loading;
    }

    // Escape and the Cancel button then reach the clone at once.
    [Fact]
    public async Task WhenACloneStarts_ShouldReaskTheShellsCancel()
    {
        var h = CloneHarness(); var catalog = h.Catalog;
        catalog.CloneGate.IsClosed = true;
        await using var model = h.Shell();
        var clone = await BeginCloneAsync(model);
        var asked = 0;
        model.CancelCommand.CanExecuteChanged += (_, _) => asked++;

        var pending = clone.ConfirmCloneCommand.ExecuteAsync(null);
        await catalog.CloneGate.Started;
        Assert.True(asked > 0);
        catalog.CloneGate.Open();
        await pending;
    }

    // One account with one repository to clone.
    private static ShellHarness CloneHarness()
    {
        var h = Harness();
        var account = FakeAccountService.GitHub(user: "contoso");
        h.Catalog.Accounts.Add(account);
        h.Catalog.Remote[account.Id] = [new("contoso/sandbox", "https://github.com/contoso/sandbox.git", "https://github.com/contoso/sandbox", "main", true, null)];
        return h;
    }

    private static async Task<CloneViewModel> BeginCloneAsync(ShellViewModel model)
    {
        await model.Catalog.Load();
        var group = Assert.Single(model.Catalog.AccountGroups);
        await group.LoadCommand.ExecuteAsync(null);
        model.Catalog.BeginCloneCommand.Execute(Assert.Single(group.Rows));
        return model.Catalog.Clone;
    }

    private static RepositoryEntry Entry(string name) => new(Folder(name), name, DateTimeOffset.UtcNow.AddDays(-1));

    // The given entries in the list, and every fixture folder answering as a repository whose origin names the folder,
    // so the catalog can tell the entries apart.
    private static ShellHarness Harness(params RepositoryEntry[] entries)
    {
        var h = new ShellHarness();
        h.Catalog.Entries.AddRange(entries);
        h.Reader.Read = (path, _) =>
        {
            var name = System.IO.Path.GetFileName(path);
            var origin = "https://github.com/" + (name == "sandbox" ? "contoso" : "acme") + "/" + name + ".git";
            return Task.FromResult(new RepositorySnapshot(path, "2.43.0", "main", "abc", null, null, [new("origin", origin, origin)], RepositoryOperation.None, [], []));
        };
        return h;
    }
}
