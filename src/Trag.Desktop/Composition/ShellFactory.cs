using System.ComponentModel;
using Trag.Application.Diff;
using Trag.Application.Live;
using Trag.Application.Ports.Catalog;
using Trag.Application.Ports.Git;
using Trag.Application.Setup;
using Trag.Application.Ports.Storage;
using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;
using Trag.Application.Publish;
using Trag.Application.Sync;
using Trag.Application.Workspace;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Accounts;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.Git.Setup;
using Trag.Infrastructure.Git.Sync;
using Trag.Infrastructure.Git.Write;
using Trag.Infrastructure.Platform.Storage;
using Trag.Infrastructure.Platform.Watching;
using Trag.ViewModels.Branches;
using Trag.ViewModels.CherryPick;
using Trag.ViewModels.Files;
using Trag.ViewModels.Reset;
using Trag.ViewModels.Commit;
using Trag.ViewModels.Details;
using Trag.ViewModels.Diff;
using Trag.ViewModels.Discard;
using Trag.ViewModels.History;
using Trag.ViewModels.Merge;
using Trag.ViewModels.Publish;
using Trag.ViewModels.Rebase;
using Trag.ViewModels.Setup;
using Trag.ViewModels.Repositories;
using Trag.ViewModels.Revert;
using Trag.ViewModels.Shell;
using Trag.ViewModels.Stash;
using Trag.ViewModels.Status;
using Trag.ViewModels.Sync;
using Trag.ViewModels.Tags;

namespace Trag.Desktop.Composition;

// The shell over the real Git backend: one process runner shared by every reader and writer, starting the Git the locator found, the sync facade with the
// accounts and this exe as askpass, and the workspace that composes them.
public static class ShellFactory
{
    // The catalog shares the Git program; a launch without a settings file restores nothing and remembers the open
    // repository only while it runs, without a layout file it keeps the default layout, without a Git choice file the
    // locator's choice. A defect a write caught goes to defects.
    public static ShellViewModel Create(GitExecutable git, IRepositoryCatalog catalog, AccountService? accounts,
        ILastRepositoryStore? settings, ILayoutStore? layout, IGitChoiceStore? gitChoice, IViewDispatcher dispatcher, Action<Exception> defects)
    {
        var runner = new GitProcessRunner(executable: git);
        var reader = new CliRepositoryReader(runner);
        var sync = new GitSyncFacade(runner, reader, accounts: accounts, askPassExecutable: Environment.ProcessPath);
        var workspace = new RepositoryWorkspace(reader,
            watcher: new RepositoryWatcher(),
            diffReader: new CliFileDiffReader(runner),
            writer: new GitWriteRouter(runner, sync, reader),
            historyReader: new CliHistoryReader(runner),
            synchronizer: sync,
            branchReader: new CliBranchReader(runner),
            stashReader: new CliStashReader(runner),
            defects: defects);
        return Compose(catalog, settings ?? new MemoryLastRepositoryStore(), new GitLocator(git), workspace,
            dispatcher, layout: layout, gitChoice: gitChoice, defects: defects);
    }

    public static ShellViewModel Compose(IRepositoryCatalog catalog, ILastRepositoryStore settings, IGitInstallations git, IRepositoryWorkspace? workspace = null,
        IViewDispatcher? dispatcher = null,
        IDiffPresentationBuilder? diffBuilder = null, ILayoutStore? layout = null, TimeProvider? time = null,
        IGitChoiceStore? gitChoice = null, Action<Exception>? defects = null)
    {
        dispatcher ??= new ImmediateViewDispatcher();
        var presentation = diffBuilder ?? new BackgroundDiffBuilder();
        var workbench = new Workbench(new(workspace, new StatusBarState(), dispatcher) { Time = time ?? TimeProvider.System, Defects = defects ?? (_ => { }) }, settings, catalog, layout);
        var navigator = new Navigator();
        ShellViewModel? shell = null;
        WorkspaceFeatures? shown = null;
        // The window's services reach every feature the same way: through the shell, while a window shows it.
        Func<IShellHost?> host = () => shell?.Host;

        // The features of one repository: the view models over its models, linked to each other, and to the application
        // through links that Detach undoes.
        WorkspaceFeatures ComposeWorkspace(Repository repository)
        {
            // The files the user selected, which the file status keeps, the diff pane beside it shows, and the commit pane,
            // the discard question and the conflicts view act on.
            var selection = new StatusSelection();
            // The diff panes read through Application readers of their own: the file status's and the commit pane's.
            var workingDiff = new WorkingDiffViewModel(repository.Status, selection, new DiffReader(presentation, repository.Cancels, DiffMessages.Working), navigator);
            var fileStatus = new FileStatusViewModel(repository.Status, selection);
            var history = new HistoryViewModel(repository.History, new DiffReader(presentation, repository.Cancels, DiffMessages.Commit), navigator);
            var blame = new BlameViewModel(repository.Blame);
            var files = new FileActionsViewModel(repository.Files, repository.Versions);
            var branches = new BranchesViewModel(repository.Branches, repository.History);
            var tags = new TagsViewModel(repository.Tags, repository.History);
            var revert = new RevertViewModel(repository.Revert, repository.History);
            var pick = new PickViewModel(repository.Pick);
            var cherryPick = new CherryPickViewModel(repository.CherryPick, repository.History);
            var reset = new ResetViewModel(repository.Reset, repository.History);
            var rewrite = new RewriteViewModel(repository.Rewrite, repository.History);
            var remoteSettings = new RemoteSettingsViewModel(repository.RemoteSettings);
            var repositoryDetails = new RepositoryDetailsViewModel(repository.Details) { SearchHistory = history.Search, Host = host };
            var goTo = new GoToCommitViewModel(repository.Branches, repository.History, navigator);
            var publish = new PublishViewModel(new PublishAccountsModel(workbench.Accounts, repository), repository.Remotes, repository.Branches);
            var resolution = new ResolutionViewModel(repository.Conflicts, selection, navigator);
            var conflict = new ConflictViewModel(repository.Conflicts);
            var merge = new MergeViewModel(repository.Merge);
            var rebase = new RebaseViewModel(repository.Rebase);
            var mergeBranches = new MergeBranchesViewModel(repository.Merge, repository.Branches, navigator);
            var rebaseBranch = new RebaseBranchViewModel(repository.Rebase, repository.Branches, navigator);
            var mergeDialog = new MergeDialogViewModel(mergeBranches, rebaseBranch, repository);
            var commit = new CommitViewModel(repository.Status, repository.Amend, repository.CoAuthors, selection);
            var sync = new SyncViewModel(repository.Remotes, new MissingRepositoryModel(repository.Remotes, workbench.Accounts));
            var forcePush = new ForcePushViewModel(repository.Remotes, navigator);
            var stash = new StashViewModel(repository.Stashes, repository.History, navigator);
            var discard = new DiscardViewModel(repository.Status, selection);
            branches.Host = files.Host = history.Host = host;
            // A submodule opens as a repository of its own.
            files.OpenRepository = path => workbench.Session.OpenRepositoryAsync(path);
            // A picker row hands its branch to the merge dialog.
            branches.Merge = mergeBranches.OpenForAsync;
            // A tag row and the blame's chosen commit find the commit in the history's search.
            tags.FindCommit = history.FindCommitAsync;
            blame.FindCommit = history.FindCommitAsync;
            // The dialogs kept back by uncommitted changes open the stash dialog over themselves, and the discard question
            // in its place.
            discard.OpenStash = goTo.OpenStash = mergeBranches.OpenStash = rebaseBranch.OpenStash = stash.OpenStashCommand;
            // The navigator belongs to the application; the features follow it through these links.
            PropertyChangedEventHandler followSection = (_, e) =>
            {
                if (e.PropertyName != nameof(Navigator.Section)) return;
                workingDiff.FollowSection(); history.FollowSection(); resolution.FollowSection(); blame.FollowSection();
            };
            navigator.PropertyChanged += followSection;
            navigator.Reselected += history.Reselect;
            return new(repository, fileStatus, workingDiff, commit, discard, stash, branches, tags, revert, cherryPick, reset, rewrite, remoteSettings, repositoryDetails, pick, goTo, resolution, conflict, merge, rebase,
                mergeBranches, rebaseBranch, mergeDialog, history, blame, files, sync, forcePush, publish, Detach: () =>
                {
                    navigator.PropertyChanged -= followSection;
                    navigator.Reselected -= history.Reselect;
                });
        }

        // Every repository the session starts, the first empty one included, gets features of its own: the previous one's
        // are detached, the navigator follows the new one, and the shell shows it; the workbench's automatic fetch
        // follows it after.
        void Present(Repository repository)
        {
            shown?.Detach();
            shown = ComposeWorkspace(repository);
            navigator.Follow(repository);
            shell?.Show(shown);
        }

        workbench.Attach(Present);
        var session = workbench.Session;
        var repositories = new RepositoryCatalogViewModel(workbench.Catalog, session, navigator) { Host = host };
        var gitSetup = new GitSetupViewModel(new GitSetup(git, gitChoice)) { Host = host };
        // What the application hands on: a closed repository to the picker, and so does the Git dialog the user closed.
        session.Closed += navigator.ShowRepositories;
        gitSetup.Dismissed += navigator.ShowRepositories;
        // Edited accounts: a shown repository list reads again.
        workbench.AccountsEdited += repositories.FollowAccounts;
        return shell = new ShellViewModel(new(workbench, navigator, repositories, new AutoFetchViewModel(workbench.AutoFetch), gitSetup), shown!);
    }
}
