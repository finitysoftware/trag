using Trag.Application.Setup;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Setup;
using Trag.Domain.Status;
using Trag.TestSupport;

namespace Trag.Application.UnitTests.Setup;

// Settings -> Git without a screen, over the shell's Git: what runs and, once read, what it brings. A chosen git.exe that
// does not run says why and leaves the running one; one that runs is shown as chosen until the user lets Trag find Git
// again.
public sealed class GitSettingsModelTests
{
    private static readonly GitFeatures Brings = new(true, "manager", CommitSigning.Ssh);
    private static readonly GitInstallation Chosen = new(@"D:\PortableGit\cmd\git.exe", new(2, 50, 0), GitOrigin.Chosen);

    [Fact]
    public async Task WhenSettingsOpens_ShouldShowTheRunningGitAndWhatItBrings()
    {
        var setup = new GitSetup(new FakeGitInstallations { Features = Brings }, new FakeGitChoiceStore());
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsModel(setup);

        await model.LoadAsync();

        Assert.Equal(GitSetupText.Facts(FakeGitInstallations.PathGit, Brings), model.Facts);
        Assert.Equal(("", false), (model.Message, model.IsChosen));
    }

    [Fact]
    public void WhenThereIsNoGitToRun_ShouldSayWhyInPlaceOfIt()
    {
        var setup = new GitSetup(new FakeGitInstallations { Found = new(GitProblem.Missing) }, null);
        _ = setup.StartAsync(CancellationToken.None);

        var model = new GitSettingsModel(setup);

        Assert.Equal((GitSetupText.Problem(new(GitProblem.Missing)), 0), (model.Message, model.Facts.Count));
    }

    [Fact]
    public async Task WhenTheChoiceIsGivenUpButThereIsNoOtherGit_ShouldSaySoAndKeepShowingTheChosenOne()
    {
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing), Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } };
        var setup = new GitSetup(git, new FakeGitChoiceStore { Path = Chosen.Path });
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsModel(setup);

        await model.ForgetChoiceAsync();

        Assert.Equal((GitSetupText.NoOtherGit, true, Chosen.Path), (model.Message, model.IsChosen, model.Facts[0].Value));
    }

    // Settings goes and the shell's Git stays: once let go, the model no longer follows it.
    [Fact]
    public async Task WhenSettingsLetsTheGitGo_ShouldNoLongerFollowIt()
    {
        var setup = new GitSetup(new FakeGitInstallations { Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) } }, new FakeGitChoiceStore());
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsModel(setup);
        var facts = model.Facts;

        model.Detach();
        await setup.ChooseAsync(Chosen.Path, CancellationToken.None);

        Assert.Same(facts, model.Facts);
    }

    [Fact]
    public async Task WhenAGitExeIsChosen_ShouldSayWhyOneDoesNotRunAndShowOneThatRunsAsChosenUntilItIsGivenUp()
    {
        var store = new FakeGitChoiceStore();
        var setup = new GitSetup(new FakeGitInstallations { Chosen = { [Chosen.Path] = new(GitProblem.None, Chosen) }, Features = Brings }, store);
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsModel(setup);

        await model.ChooseAsync(@"D:\notes.txt");
        Assert.Equal((GitSetupText.Problem(new(GitProblem.NotGit)), FakeGitInstallations.PathGit.Path), (model.Message, model.Facts[0].Value));
        await model.ChooseAsync(Chosen.Path);
        Assert.Equal(("", true), (model.Message, model.IsChosen));
        Assert.Equal(GitSetupText.Facts(Chosen, Brings), model.Facts);
        await model.ForgetChoiceAsync();

        Assert.Equal((FakeGitInstallations.PathGit.Path, false, null), (model.Facts[0].Value, model.IsChosen, store.Path));
    }
}
