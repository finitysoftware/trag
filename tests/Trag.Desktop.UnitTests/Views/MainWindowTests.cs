using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Views;

namespace Trag.Desktop.UnitTests.Views;

// Trag opens over the whole work area; its own size is the one it takes when it is restored.
public sealed class MainWindowTests
{
    [AvaloniaFact]
    public void WhenTheAppStarts_ShouldOpenItsWindowMaximized()
    {
        var window = new MainWindow();

        Assert.Equal(WindowState.Maximized, window.WindowState);
    }

    // Closing while the layout is still being saved waits for the save, however often close is asked, and then the window
    // closes once.
    [AvaloniaFact]
    public async Task WhenCloseIsAskedTwiceDuringALayoutSave_ShouldCloseOnceTheSaveEnds()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var closes = 0; window.Closed += (_, _) => closes++;
        h.Preferences.SaveGate.IsClosed = true;
        model.Layout.DiffMode = 1;
        await h.Preferences.SaveGate.Started;

        window.Close(); window.Close(); AppHarness.Layout(window);
        var openWhileSaving = window.IsVisible;
        h.Preferences.SaveGate.Open(); await model.Layout.SaveCompletion; AppHarness.Layout(window);

        Assert.True(openWhileSaving);
        Assert.Equal((1, false), (closes, window.IsVisible));
    }

    // Windows asks the element under the pointer whether it is the title bar, so a dialog's scrim over it would take the
    // window's drag and double-click; every dialog covers the window below the title bar only.
    [AvaloniaFact]
    public async Task WhenADialogIsOpen_ShouldLeaveTheTitleBarToTheWindow()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        var titleBar = window.Descendant<Border>("TitleBar")!;
        var point = titleBar.TranslatePoint(new Point(titleBar.Bounds.Width / 2, titleBar.Bounds.Height / 2), window)!.Value;
        var belowTitleBar = new Rect(0, titleBar.Bounds.Height, window.Bounds.Width, window.Bounds.Height - titleBar.Bounds.Height);
        var dialogs = window.GetVisualDescendants().OfType<DialogHost>().ToList();

        foreach (var dialog in dialogs)
        {
            dialog.IsOpen = true; AppHarness.Layout(window);
            // A hit test reads the composed scene, which the next render tick brings up to date with the open dialog.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); AppHarness.Layout(window);
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(hit is not null && (hit == titleBar || titleBar.IsVisualAncestorOf(hit)), dialog.Name + " covers the title bar");
            Assert.Equal((dialog.Name, belowTitleBar), (dialog.Name, dialog.Bounds));
            dialog.IsOpen = false; AppHarness.Layout(window);
        }
        window.Close();
    }
}
