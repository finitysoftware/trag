using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Domain.Accounts;
using Trag.Domain.Errors;
using Trag.Domain.Presentation.Text;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Dialogs.Settings;

// Adding a GitLab account opens its entry in a dialog over the whole settings window, as the GitHub sign-in does, not
// under the cards: the server, the masked token with the keys in it and Connect in the accent; the ✕ closes it. While the token is checked a
// bar runs in the dialog, and what the check ends with is said there, so a refused token stays in view with its reason.
public sealed class GitLabEntryDialogTests
{
    [AvaloniaFact]
    public async Task WhenAGitLabAccountIsAdded_ShouldOpenTheEntryInADialogOverSettingsThatTheCloseButtonCloses()
    {
        var window = SettingsHarness.Window(new FakeAccountService()); window.Show(); await window.Initialization; AppHarness.Layout(window);

        SettingsHarness.Click(window, window.Descendant<Button>("AddGitLabButton")!);

        var dialog = window.Descendant<DialogHost>("GitLabEntryOverlay")!;
        Assert.True(dialog.IsEffectivelyVisible);
        Assert.Equal(window.Bounds.Size, dialog.Bounds.Size);
        Assert.Equal("https://gitlab.com", dialog.Descendant<TextBox>("GitLabAddressEditor")!.Text);
        Assert.Equal('●', dialog.Descendant<TextBox>("GitLabTokenEditor")!.PasswordChar);
        Assert.True(dialog.Descendant<TextBox>("GitLabTokenEditor")!.IsFocused);
        Assert.Contains("accent", dialog.Descendant<Button>("ConnectGitLabButton")!.Classes);
        Assert.DoesNotContain(window.Descendant<ScrollViewer>("AccountsScroll")!.GetVisualDescendants().OfType<TextBox>(), editor => editor.IsEffectivelyVisible);
        Assert.Equal(AccountText.GitLabTokenHelp, dialog.Descendant<TextBlock>("GitLabEntryStatus")!.Text);

        SettingsHarness.Click(window, dialog.Descendant<Button>("CloseButton")!);

        Assert.False(dialog.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenTheTokenIsChecked_ShouldRunABarInTheDialogAndKeepItOpenWithTheReasonItWasRefused()
    {
        var answer = new TaskCompletionSource<AccountInfo>();
        var service = new FakeAccountService { SignInGitLab = (_, _, _, _) => answer.Task };
        var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Click(window, window.Descendant<Button>("AddGitLabButton")!);
        var dialog = window.Descendant<DialogHost>("GitLabEntryOverlay")!;
        var bar = dialog.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.False(bar.IsEffectivelyVisible);
        dialog.Descendant<TextBox>("GitLabTokenEditor")!.Text = "glpat-synthetic";

        SettingsHarness.Click(window, dialog.Descendant<Button>("ConnectGitLabButton")!);
        Assert.True(bar.IsEffectivelyVisible);
        answer.SetException(new AccountException("GitLab refused this token."));
        await AppHarness.Until(() => window.Model.IsIdle, "the check"); AppHarness.Layout(window);

        Assert.True(dialog.IsEffectivelyVisible); Assert.False(bar.IsEffectivelyVisible);
        Assert.Contains("GitLab refused this token.", dialog.Descendant<TextBlock>("GitLabEntryStatus")!.Text);
        window.Close();
    }
}
