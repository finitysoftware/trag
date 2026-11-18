using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Trag.Desktop.Dialogs;
using Trag.Domain.Setup;

namespace Trag.Desktop.UnitTests.Dialogs;

// Without a Git to run, the window shows the Git dialog over itself, as it shows the repository details: why, Install Git
// in the accent, and Choose git.exe.
public sealed class GitSetupDialogTests
{
    [AvaloniaFact]
    public async Task WhenThereIsNoGitAtTheStart_ShouldShowTheGitDialogOverTheWindow()
    {
        var h = new AppHarness();
        h.Git.Found = new(GitProblem.Missing);
        await using var model = h.Shell();
        var window = h.Window(model);

        _ = model.InitializeAsync(); AppHarness.Layout(window);

        var dialog = window.Descendant<DialogHost>("GitSetupOverlay")!;
        Assert.True(dialog.IsEffectivelyVisible);
        Assert.Equal(window.Bounds.Size.WithHeight(window.Bounds.Height - window.Descendant<Border>("TitleBar")!.Bounds.Height), dialog.Bounds.Size);
        Assert.Equal(model.Git.Text, window.Descendant<TextBlock>("GitSetupText")!.Text);
        var install = window.Descendant<Button>("InstallGitButton")!;
        Assert.Contains("accent", install.Classes);
        Assert.Same(model.Git.InstallGitCommand, install.Command);
        Assert.Same(model.Git.ChooseGitCommand, window.Descendant<Button>("ChooseGitButton")!.Command);
        window.Close();
    }
}
