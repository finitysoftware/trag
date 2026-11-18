using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Application.Setup;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Views;
using Trag.Domain.Status;
using Trag.TestSupport;
using Trag.ViewModels.Setup;

namespace Trag.Desktop.UnitTests.Dialogs.Settings;

// Settings has a Git section: the Git Trag runs and what it brings, in place of the accounts, with Choose git.exe, and
// the way back to the Git Trag finds once one was chosen. The window hands Settings the shell's Git; without it there is
// no section.
public sealed class GitPageTests
{
    [AvaloniaFact]
    public async Task WhenGitIsChosenInSettings_ShouldShowTheGitTragRunsInPlaceOfTheAccounts()
    {
        var setup = new GitSetup(new FakeGitInstallations { Features = new(true, "manager", CommitSigning.None) }, new FakeGitChoiceStore());
        await setup.StartAsync(CancellationToken.None);
        var window = SettingsHarness.Window(new FakeAccountService(), git: new GitSettingsViewModel(new GitSettingsModel(setup)));
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        var page = window.FindControl<GitPage>("Git")!;
        Assert.False(page.IsEffectivelyVisible);

        SettingsHarness.Choose(window, "GitSection");

        Assert.True(page.IsEffectivelyVisible);
        Assert.False(window.Descendant<ScrollViewer>("AccountsScroll")!.IsEffectivelyVisible);
        var texts = page.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains(FakeGitInstallations.PathGit.Path, texts);
        Assert.Contains("manager", texts);
        Assert.True(page.Descendant<Button>("ChooseGitButton")!.IsEffectivelyVisible);
        Assert.False(page.Descendant<Button>("ForgetGitChoiceButton")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenSettingsCloses_ShouldLetTheShellsGitGo()
    {
        var chosen = new Domain.Setup.GitInstallation(@"D:\PortableGit\cmd\git.exe", new(2, 50, 0), Domain.Setup.GitOrigin.Chosen);
        var setup = new GitSetup(new FakeGitInstallations { Chosen = { [chosen.Path] = new(Domain.Setup.GitProblem.None, chosen) } }, new FakeGitChoiceStore());
        await setup.StartAsync(CancellationToken.None);
        var model = new GitSettingsViewModel(new GitSettingsModel(setup));
        var window = SettingsHarness.Window(new FakeAccountService(), git: model);
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        var facts = model.Facts;

        window.Close();
        await setup.ChooseAsync(chosen.Path, CancellationToken.None);

        Assert.Same(facts, model.Facts);
    }

    [AvaloniaFact]
    public async Task WhenSettingsHasNoGit_ShouldOfferNoGitSection()
    {
        var window = SettingsHarness.Window(new FakeAccountService());
        window.Show(); await window.Initialization; AppHarness.Layout(window);

        Assert.False(window.Descendant<RadioButton>("GitSection")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenSettingsOpensFromTheWindow_ShouldShowTheShellsGit()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = new FakeAccountService(), Git = model.Git.Setup } };
        owner.Show(); AppHarness.Layout(owner);

        var showing = owner.SettingsHost.ShowSettingsAsync();
        var settings = owner.SettingsHost.ActiveSettings!; await settings.Initialization; AppHarness.Layout(settings);

        Assert.True(settings.Descendant<RadioButton>("GitSection")!.IsVisible);
        settings.Close(); await showing; owner.Close();
    }
}
