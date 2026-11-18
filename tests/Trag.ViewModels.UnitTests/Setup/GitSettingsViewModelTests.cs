using Trag.Application.Setup;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Setup;
using Trag.TestSupport;
using Trag.ViewModels.Setup;

namespace Trag.ViewModels.UnitTests.Setup;

// Settings -> Git shows what the settings model says as it changes, and its command gives the choice up there.
public sealed class GitSettingsViewModelTests
{
    [Fact]
    public async Task WhenTheChoiceIsGivenUp_ShouldShowWhatTheSettingsModelSays()
    {
        var chosen = new GitInstallation(@"D:\PortableGit\cmd\git.exe", new(2, 50, 0), GitOrigin.Chosen);
        var git = new FakeGitInstallations { Found = new(GitProblem.Missing), Chosen = { [chosen.Path] = new(GitProblem.None, chosen) } };
        var setup = new GitSetup(git, new FakeGitChoiceStore { Path = chosen.Path });
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsViewModel(new GitSettingsModel(setup));
        var announced = new List<string?>();
        model.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        await model.ForgetChoiceCommand.ExecuteAsync(null);

        Assert.Equal((GitSetupText.NoOtherGit, true, chosen.Path), (model.Message, model.IsChosen, model.Facts[0].Value));
        Assert.Contains(nameof(GitSettingsViewModel.Message), announced);
    }
}
