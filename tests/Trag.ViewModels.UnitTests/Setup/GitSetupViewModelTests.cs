using Trag.Domain.Presentation.Text;
using Trag.Domain.Setup;
using Trag.TestSupport;

namespace Trag.ViewModels.UnitTests.Setup;

// The window's Git dialog: it opens while there is no Git to run and says why, sends the user to Git's download page,
// takes the git.exe the user chooses, and looks again whenever the user comes back to the window while there is no Git,
// closed or not. Closed, it leaves the repository list in view. The shell's start applies the saved layout and theme
// first, then waits for a Git before it opens the last repository.
public sealed class GitSetupViewModelTests
{
    private static readonly GitInstallation Chosen = new(@"D:\PortableGit\cmd\git.exe", new(2, 50, 0), GitOrigin.Chosen);

    [Fact]
    public async Task WhenThereIsNoGitAtTheStart_ShouldAskForOneAndOpenTheLastRepositoryOnceAChosenOneRuns()
    {
        var h = new ShellHarness();
        h.Git.Found = new(GitProblem.Missing); h.Git.Chosen[Chosen.Path] = new(GitProblem.None, Chosen);
        h.Store.Path = "saved";
        await using var model = h.Shell();
        var host = new RecordingHost { GitExecutable = Chosen.Path };
        model.Host = host;

        var start = model.InitializeAsync();
        Assert.Equal((true, GitSetupText.Problem(new(GitProblem.Missing)), false), (model.Git.IsOpen, model.Git.Text, model.Repository.HasRepository));
        model.Git.InstallGitCommand.Execute(null);
        await model.Git.ChooseGitCommand.ExecuteAsync(null);
        await start;

        Assert.Equal([GitSetupText.DownloadPage], host.Opened);
        Assert.Equal((false, Chosen.Path, true), (model.Git.IsOpen, h.GitChoice.Path, model.Repository.HasRepository));
    }

    // While the start waits for a Git the window already wears the saved layout and theme, so nothing the user changes
    // meanwhile is saved over them.
    [Fact]
    public async Task WhenThereIsNoGitAtTheStart_ShouldApplyTheSavedLayoutWhileItWaits()
    {
        var h = new ShellHarness();
        h.Git.Found = new(GitProblem.Missing);
        h.Preferences.Preferences = new(Theme: 2, HistoryRatio: 0.3);
        await using var model = h.Shell();

        var start = model.InitializeAsync();

        Assert.False(start.IsCompleted);
        Assert.Equal((2, 0.3), (model.Layout.ThemeIndex, model.Layout.HistoryPanelRatio));
    }

    [Fact]
    public async Task WhenTheUserComesBackAfterInstallingGit_ShouldLookAgainAndClose()
    {
        var h = new ShellHarness();
        h.Git.Found = new(GitProblem.Missing);
        await using var model = h.Shell();
        var start = model.InitializeAsync();
        await model.Git.ActivatedAsync();
        Assert.True(model.Git.IsOpen);

        h.Git.Found = new(GitProblem.None, FakeGitInstallations.PathGit);
        await model.Git.ActivatedAsync();
        await start;
        await model.Git.ActivatedAsync();

        Assert.False(model.Git.IsOpen);
        Assert.Equal([null, null, null], h.Git.Looks);
    }

    [Fact]
    public async Task WhenTheDialogIsClosed_ShouldShowTheRepositoriesAndStillLookAgainOnComingBackWithoutOpeningIt()
    {
        var h = new ShellHarness();
        h.Git.Found = new(GitProblem.Missing);
        await using var model = h.Shell();
        var start = model.InitializeAsync();

        model.Git.CloseCommand.Execute(null);
        await model.Git.ActivatedAsync();
        Assert.Equal((false, 2, Domain.Presentation.WorkspaceSection.Repositories), (model.Git.IsOpen, h.Git.Looks.Count, model.Navigator.Section));
        h.Git.Found = new(GitProblem.None, FakeGitInstallations.PathGit);
        await model.Git.ActivatedAsync();

        await start;
        Assert.False(model.Git.IsOpen);
    }

    // The dialog lives as long as the window, while every repository shown brings features of its own: closing it after
    // repositories were switched shows the repository list once, so the list is read once.
    [Fact]
    public async Task WhenTheDialogIsClosedAfterRepositoriesWereSwitched_ShouldReadTheRepositoryListOnce()
    {
        var h = new ShellHarness();
        await using var model = h.Shell();
        await model.Session.OpenRepositoryAsync("first");
        await model.Session.OpenRepositoryAsync("second");
        var reads = h.Catalog.AccountsReads;

        model.Git.CloseCommand.Execute(null);
        await model.Catalog.CatalogCompletion;

        Assert.Equal(reads + 1, h.Catalog.AccountsReads);
    }
}
