using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Dialogs.Settings;
using Trag.Domain.Accounts;
using Trag.TestSupport;

namespace Trag.Desktop.UnitTests.Dialogs.Settings;

// While GitHub waits for its code, Settings shows the code in a dialog over the whole window, wherever the accounts are
// scrolled, and nothing else that runs: the code, the way to GitHub's page, Copy in the accent and a bar that runs. Copy
// puts the code on the clipboard, and the ✕ cancels the sign-in and closes the dialog.
public sealed class DeviceLoginDialogTests
{
    [AvaloniaFact]
    public async Task WhenGitHubWaitsForTheCode_ShouldShowItInADialogOverSettingsThatCopiesItAndCancels()
    {
        var (window, dialog) = await SigningIn();

        Assert.True(dialog.IsEffectivelyVisible);
        Assert.Equal(window.Bounds.Size, dialog.Bounds.Size);
        Assert.Equal("ABCD-1234", dialog.Descendant<SelectableTextBlock>("DeviceCode")!.Text);
        Assert.Same(window.Model.OpenDevicePageCommand, dialog.Descendant<Button>("OpenDevicePageButton")!.Command);
        Assert.True(dialog.GetVisualDescendants().OfType<ProgressBar>().Single().IsEffectivelyVisible);
        Assert.DoesNotContain(window.Descendant<ScrollViewer>("AccountsScroll")!.GetVisualDescendants().OfType<SelectableTextBlock>(), text => text.Text == "ABCD-1234");
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ProgressBar>(), bar => bar.IsEffectivelyVisible && bar.FindAncestorOfType<DialogHost>() is null);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Cancel") && button.IsEffectivelyVisible);
        var copy = dialog.Descendant<Button>("CopyDeviceCodeButton")!;
        Assert.Contains("accent", copy.Classes);

        SettingsHarness.Click(window, copy);
        Assert.Equal("ABCD-1234", await window.Clipboard!.TryGetTextAsync());
        SettingsHarness.Click(window, dialog.Descendant<Button>("CloseButton")!);
        await AppHarness.Until(() => window.Model.IsIdle, "the cancel"); AppHarness.Layout(window);

        Assert.False(dialog.IsEffectivelyVisible);
        Assert.Contains("canceled", window.Model.Status);
        window.Close();
    }

    // The caption sits in the middle over the code, the buttons at the right edge over the bar's end, and twice the
    // dialog's usual room lies between the caption, the code and the buttons.
    [AvaloniaFact]
    public async Task WhenTheCodeShows_ShouldCentreTheCaptionAndPutTheButtonsAtTheRightWithDoubleRoomBetween()
    {
        var (window, dialog) = await SigningIn();
        var card = dialog.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Card");
        Rect Placed(Control control) => new(control.TranslatePoint(default, card)!.Value, control.Bounds.Size);
        var (caption, code, open, copy, bar) = (Placed(dialog.Descendant<TextBlock>("DeviceCodeCaption")!), Placed(dialog.Descendant<SelectableTextBlock>("DeviceCode")!),
            Placed(dialog.Descendant<Button>("OpenDevicePageButton")!), Placed(dialog.Descendant<Button>("CopyDeviceCodeButton")!), Placed(dialog.GetVisualDescendants().OfType<ProgressBar>().Single()));

        Assert.True(Math.Abs(caption.Center.X - code.Center.X) <= 1, $"the caption's middle is at {caption.Center.X}, the code's at {code.Center.X}");
        Assert.True(Math.Abs(copy.Right - bar.Right) <= 0.5, $"the buttons end at {copy.Right}, the bar at {bar.Right}");
        Assert.True(code.Top - caption.Bottom >= 24 && open.Top - code.Bottom >= 24, $"{code.Top - caption.Bottom} under the caption, {open.Top - code.Bottom} under the code");
        window.Close();
    }

    private static async Task<(SettingsWindow Window, DialogHost Dialog)> SigningIn()
    {
        var service = new FakeAccountService
        {
            SignInGitHub = async (_, progress, ct) =>
            {
                progress.Report(new(DeviceLoginState.Waiting, "ABCD-1234", new("https://github.com/login/device")));
                await Task.Delay(Timeout.Infinite, ct);
                return FakeAccountService.GitHub();
            }
        };
        var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Click(window, window.Descendant<Button>("AddGitHubButton")!);
        await AppHarness.Until(() => window.Model.Login.HasCode, "the code"); AppHarness.Layout(window);
        return (window, window.Descendant<DialogHost>("DeviceLoginOverlay")!);
    }
}
