using Trag.Domain.Presentation.Text;
using Trag.Domain.Setup;
using Trag.Domain.Status;

namespace Trag.Domain.UnitTests.Presentation.Text;

// What the Git dialog says: that no Git was found, which Git is too old and where it is, or that the chosen file is no
// Git; each with the oldest Git Trag works with where installing one is the way out.
public sealed class GitSetupTextTests
{
    [Fact]
    public void WhenThereIsNoGitToRun_ShouldSayWhyAndWhatToDo()
    {
        var old = new GitInstallation(@"C:\Old\cmd\git.exe", new(2, 30, 1), GitOrigin.Path);

        Assert.Equal("No Git was found on this computer. Install Git 2.43 or newer, or choose the git.exe you already have.",
            GitSetupText.Problem(new(GitProblem.Missing)));
        Assert.Equal(@"Git 2.30.1 at C:\Old\cmd\git.exe is older than Trag works with. Install Git 2.43 or newer, or choose the git.exe of a newer one.",
            GitSetupText.Problem(new(GitProblem.TooOld, old)));
        Assert.Equal("The chosen file does not run as Git. Choose the git.exe of a Git installation, or install Git.",
            GitSetupText.Problem(new(GitProblem.NotGit)));
    }

    // A chosen Git given up: nothing to say once Trag found another to run, else that it keeps the chosen one.
    [Fact]
    public void WhenAChoiceIsGivenUp_ShouldSayItIsKeptOnlyWhenNoOtherGitRuns()
    {
        var found = new GitInstallation(@"C:\Git\cmd\git.exe", new(2, 50, 0), GitOrigin.Path);
        Assert.Equal(("", GitSetupText.NoOtherGit), (GitSetupText.Forgotten(new(GitProblem.None, found)), GitSetupText.Forgotten(new(GitProblem.Missing))));
    }

    // A Git runs only without a problem; a Git too old is named but does not run.
    [Fact]
    public void WhenGitWasLookedFor_ShouldSayWhetherAGitRunsAndWhichOne()
    {
        var git = new GitInstallation(@"C:\Git\cmd\git.exe", new(2, 50, 0), GitOrigin.Path);
        Assert.Equal((true, git), (new GitFinding(GitProblem.None, git).Runs, new GitFinding(GitProblem.None, git).Running));
        Assert.Equal((false, null), (new GitFinding(GitProblem.TooOld, git).Runs, new GitFinding(GitProblem.TooOld, git).Running));
    }

    // Settings names the Git Trag runs, its version and where it was found, then, once read, what it brings.
    [Fact]
    public void WhenTheRunningGitIsShown_ShouldNameItAndThenWhatItBrings()
    {
        var git = new GitInstallation(@"C:\Program Files\Git\cmd\git.exe", new(2, 55, 0), GitOrigin.Path);

        Assert.Equal([new("Program", git.Path), new("Version", "2.55.0"), new("Found", "In a folder of the PATH")], GitSetupText.Facts(git, null));
        Assert.Equal([new("Program", git.Path), new("Version", "2.55.0"), new("Found", "Chosen by you"), new("Git LFS", "Installed"),
                new("Credential helper", "manager"), new("Signs commits", "With an SSH key")],
            GitSetupText.Facts(git with { Origin = GitOrigin.Chosen }, new(true, "manager", CommitSigning.Ssh)));
        Assert.Equal([new("Found", "In a standard install folder"), new("Git LFS", "Not installed"), new("Credential helper", "None"), new("Signs commits", "No")],
            GitSetupText.Facts(git with { Origin = GitOrigin.InstallFolder }, new(false, null, CommitSigning.None)).Skip(2));
    }

    // A credential helper is named, never shown whole: a script, or what follows a helper's name, can carry a token.
    [Theory]
    [InlineData("manager", "manager")]
    [InlineData("store --file ~/.git-credentials", "store")]
    [InlineData(@"C:/Program\ Files/Git/mingw64/bin/git-credential-manager.exe", "manager")]
    [InlineData("\"C:/Program Files/Git/mingw64/bin/git-credential-manager.exe\" --no-ui", "manager")]
    [InlineData("!f() { echo password=ghp_secret; }; f", "A custom script")]
    public void WhenACredentialHelperIsShown_ShouldNameItWithoutWhatFollowsItsName(string helper, string shown) =>
        Assert.Contains(new DetailFact("Credential helper", shown),
            GitSetupText.Facts(new(@"C:\Git\cmd\git.exe", new(2, 55, 0), GitOrigin.Path), new(false, helper, CommitSigning.None)));
}
