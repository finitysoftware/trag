using Trag.Application.Setup;
using Trag.Domain.Setup;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Setup;

// The Git Trag runs: looked for at the start, the user's choice first, and the start goes on once there is a Git to run.
// A chosen git.exe is remembered only when it runs as a new enough Git; one that fails while a Git runs leaves the running
// one shown. A choice that cannot be read is as none, and one that cannot be written still runs. Looks and choices take
// turns, so each sees the one before. Without anything to look with, as in a host that keeps no Git, the start goes on at
// once.
public sealed class GitSetupTests
{
    private static readonly GitInstallation Chosen = new(@"D:\PortableGit\cmd\git.exe", new(2, 50, 0), GitOrigin.Chosen);

    [Fact]
    public async Task WhenTheStartFindsTheChosenGit_ShouldGoOnWithIt()
    {
        var git = new FakeGitInstallations { Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var setup = new GitSetup(git, new FakeGitChoiceStore { Path = Chosen.Path });

        Assert.True(await setup.StartAsync(CancellationToken.None));

        Assert.Equal(new GitFinding(GitProblem.None, Chosen), setup.Finding);
        Assert.Equal([Chosen.Path], git.Looks);
    }

    [Fact]
    public async Task WhenThereIsNoGitAtTheStart_ShouldWaitUntilAChosenOneRunsAndRememberIt()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing), Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var store = new FakeGitChoiceStore();
        var setup = new GitSetup(git, store);
        var start = setup.StartAsync(CancellationToken.None);
        Assert.Equal((GitProblem.Missing, false), (setup.Finding.Problem, start.IsCompleted));

        Assert.Equal(new GitFinding(GitProblem.NotGit), await setup.ChooseAsync(@"D:\notes.txt", CancellationToken.None));
        Assert.Equal((GitProblem.NotGit, false, 0), (setup.Finding.Problem, start.IsCompleted, store.Saves));
        await setup.ChooseAsync(Chosen.Path, CancellationToken.None);

        Assert.True(await start);
        Assert.Equal((Chosen.Path, new GitFinding(GitProblem.None, Chosen)), (store.Path, setup.Finding));
    }

    [Fact]
    public async Task WhenGitIsLookedForAgainOnceItIsInstalled_ShouldGoOnWithTheStart()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing) };
        var setup = new GitSetup(git, null);
        var start = setup.StartAsync(CancellationToken.None);

        git.Found = new(GitProblem.None, FakeGitInstallations.PathGit);
        await setup.CheckAsync(CancellationToken.None);

        Assert.True(await start);
    }

    [Fact]
    public async Task WhenAChoiceFailsWhileAGitRuns_ShouldKeepShowingTheRunningOne()
    {
        var store = new FakeGitChoiceStore();
        var setup = new GitSetup(new FakeGitInstallations(), store);
        await setup.StartAsync(CancellationToken.None);

        Assert.Equal(new GitFinding(GitProblem.NotGit), await setup.ChooseAsync(@"D:\notes.txt", CancellationToken.None));

        Assert.Equal((new GitFinding(GitProblem.None, FakeGitInstallations.PathGit), 0), (setup.Finding, store.Saves));
    }

    [Fact]
    public async Task WhenTheApplicationGoesBeforeAGitIsFound_ShouldEndTheStartWithoutGoingOn()
    {
        var setup = new GitSetup(new FakeGitInstallations { Found = new(GitProblem.Missing) }, null);
        using var lifetime = new CancellationTokenSource();
        var start = setup.StartAsync(lifetime.Token);

        await lifetime.CancelAsync();

        Assert.False(await start);
    }

    [Fact]
    public async Task WhenAChosenGitIsGivenUp_ShouldForgetItAndFindGitAsAtTheStartThenDescribeThatOne()
    {
        var git = new FakeGitInstallations { Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) }, Features = new(true, "manager", CommitSigning.None) };
        var store = new FakeGitChoiceStore { Path = Chosen.Path };
        var setup = new GitSetup(git, store);
        await setup.StartAsync(CancellationToken.None);

        await setup.ForgetChoiceAsync(CancellationToken.None);

        Assert.Equal((null, new GitFinding(GitProblem.None, FakeGitInstallations.PathGit)), (store.Path, setup.Finding));
        Assert.Equal(new GitFeatures(true, "manager", CommitSigning.None), await setup.DescribeAsync(CancellationToken.None));
        Assert.Equal([FakeGitInstallations.PathGit], git.Described);
    }

    [Fact]
    public async Task WhenThereIsNoGitToRun_ShouldDescribeNone()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing) };
        var setup = new GitSetup(git, null);
        _ = setup.StartAsync(CancellationToken.None);

        Assert.Null(await setup.DescribeAsync(CancellationToken.None));
        Assert.Empty(git.Described);
    }

    // A Git too old to run is named in the problem, but it is not the Git Trag runs, so nothing asks it what it brings.
    [Fact]
    public async Task WhenTheGitFoundIsTooOld_ShouldDescribeNone()
    {
        var old = new GitInstallation(@"C:\old\git.exe", new(2, 20, 0), GitOrigin.Path);
        var git = new FakeGitInstallations { Found = new(GitProblem.TooOld, old) };
        var setup = new GitSetup(git, null);
        _ = setup.StartAsync(CancellationToken.None);

        Assert.Null(await setup.DescribeAsync(CancellationToken.None));
        Assert.Empty(git.Described);
    }

    [Fact]
    public async Task WhenTheChoiceCannotBeRead_ShouldLookForGitAsWithoutOne()
    {
        var git = new FakeGitInstallations();
        var setup = new GitSetup(git, new FakeGitChoiceStore { LoadFailure = new IOException("broken git.json") });

        Assert.True(await setup.StartAsync(CancellationToken.None));

        Assert.Equal(new GitFinding(GitProblem.None, FakeGitInstallations.PathGit), setup.Finding);
        Assert.Equal([null], git.Looks);
    }

    [Fact]
    public async Task WhenTheChoiceCannotBeWritten_ShouldStillRunTheChosenGit()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing), Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var setup = new GitSetup(git, new FakeGitChoiceStore { SaveFailure = new IOException("disk full") });
        var start = setup.StartAsync(CancellationToken.None);

        Assert.Equal(new GitFinding(GitProblem.None, Chosen), await setup.ChooseAsync(Chosen.Path, CancellationToken.None));

        Assert.True(await start);
        Assert.Equal(new GitFinding(GitProblem.None, Chosen), setup.Finding);
    }

    // The window is activated as it opens, before the start looks for Git and while it looks: only a start that found
    // no Git to run is followed by another look.
    [Fact]
    public async Task WhenTheWindowIsActivatedDuringStartup_ShouldLookForGitOnce()
    {
        var git = new FakeGitInstallations();
        var setup = new GitSetup(git, null);
        git.PathGate.IsClosed = true;

        var opened = setup.ActivatedAsync();
        var start = setup.StartAsync(CancellationToken.None);
        await git.PathGate.Started;
        var looking = setup.ActivatedAsync();
        git.PathGate.Open();
        await Task.WhenAll(opened, start, looking);

        Assert.Equal([null], git.Looks);
    }

    // The window's activation as the file dialog closes looks along the PATH while the user's choice is checked; the look
    // that began first must not be the one shown last.
    [Fact]
    public async Task WhenALookAlongThePathOverlapsAChoice_ShouldEndWithTheChosenGitShown()
    {
        var old = new GitInstallation(@"C:\Old\cmd\git.exe", new(2, 30, 0), GitOrigin.Path);
        var git = new FakeGitInstallations { Found = new(GitProblem.TooOld, old), Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var setup = new GitSetup(git, new FakeGitChoiceStore());
        var start = setup.StartAsync(CancellationToken.None);
        git.PathGate.IsClosed = true;
        var looking = setup.CheckAsync(CancellationToken.None);
        await git.PathGate.Arrival(2);

        var choosing = setup.ChooseAsync(Chosen.Path, CancellationToken.None);
        git.PathGate.Open();
        await Task.WhenAll(looking, choosing, start);

        Assert.Equal(new GitFinding(GitProblem.None, Chosen), setup.Finding);
    }

    // A chosen git.exe that no longer runs, as a portable Git moved away, gives way to the Git along the PATH and is
    // forgotten; with none along the PATH it is kept, for a drive that comes back, and what is wrong with it is said.
    [Fact]
    public async Task WhenTheChosenGitNoLongerRuns_ShouldGoOnWithTheGitAlongThePathAndForgetTheChoice()
    {
        var git = new FakeGitInstallations();
        var store = new FakeGitChoiceStore { Path = Chosen.Path };
        var setup = new GitSetup(git, store);

        Assert.True(await setup.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token));

        Assert.Equal((new GitFinding(GitProblem.None, FakeGitInstallations.PathGit), null), (setup.Finding, store.Path));
        Assert.Equal([Chosen.Path, null], git.Looks);
    }

    [Fact]
    public void WhenTheChosenGitNoLongerRunsAndThePathHasNone_ShouldKeepTheChoiceAndSayWhatIsWrongWithIt()
    {
        var store = new FakeGitChoiceStore { Path = Chosen.Path };
        var setup = new GitSetup(new FakeGitInstallations { Found = new(GitProblem.Missing) }, store);

        _ = setup.StartAsync(CancellationToken.None);

        Assert.Equal((new GitFinding(GitProblem.NotGit), Chosen.Path), (setup.Finding, store.Path));
    }

    // Letting Trag find Git again forgets the choice only when Trag finds another Git; otherwise the chosen one runs on.
    [Fact]
    public async Task WhenTheChoiceIsGivenUpButThereIsNoOtherGit_ShouldKeepRunningTheChosenOne()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing), Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var store = new FakeGitChoiceStore { Path = Chosen.Path };
        var setup = new GitSetup(git, store);
        await setup.StartAsync(CancellationToken.None);

        Assert.Equal(new GitFinding(GitProblem.Missing), await setup.ForgetChoiceAsync(CancellationToken.None));

        Assert.Equal((new GitFinding(GitProblem.None, Chosen), Chosen.Path), (setup.Finding, store.Path));
    }
}
