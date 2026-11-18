using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace Trag.Desktop.UnitTests.Dialogs;

// The frame every in-window dialog shares, as the window it is in sizes it.
public sealed class DialogHostTests
{
    // A dialog taller than its window keeps its header and its buttons inside it; the content between them gets what is
    // left, as the dialogs that scroll their content take it.
    [AvaloniaFact]
    public void WhenTheContentIsTallerThanTheWindow_ShouldKeepTheHeaderAndTheFooterInside()
    {
        var footer = new Button { Content = "Confirm" };
        var host = new Trag.Desktop.Dialogs.DialogHost { IsOpen = true, Title = "Tall", Content = new ScrollViewer { Content = new Border { Height = 1000 } }, Footer = footer };
        var window = new Window { Width = 600, Height = 400, Content = host };
        window.Show(); AppHarness.Layout(window);

        var close = window.Descendant<Button>("CloseButton")!;

        Assert.True(close.TranslatePoint(new Point(0, 0), window)!.Value.Y >= 0);
        Assert.True(footer.TranslatePoint(new Point(0, footer.Bounds.Height), window)!.Value.Y <= window.Bounds.Height);
        window.Close();
    }

    // A dialog that names no control to focus holds the keys itself; Tab from there goes to its first control and Shift+Tab
    // to its last, not past it to the window's controls behind the scrim.
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.None, "CloseButton")]
    [InlineData(RawInputModifiers.Shift, "Last")]
    public void WhenTheDialogItselfHasTheKeys_ShouldTabIntoIt(RawInputModifiers modifiers, string expected)
    {
        var host = new Trag.Desktop.Dialogs.DialogHost { IsOpen = true, Content = new Button { Content = "Inner" }, Footer = new Button { Name = "Last", Content = "Last" } };
        var window = new Window { Content = new StackPanel { Children = { new Button { Name = "Before" }, host, new Button { Name = "After" } } } };
        window.Show(); AppHarness.Layout(window);
        Assert.Same(host, window.FocusManager!.GetFocusedElement());

        window.KeyPress(Key.Tab, modifiers, PhysicalKey.Tab, null); AppHarness.Layout(window);

        Assert.Equal(expected, (window.FocusManager!.GetFocusedElement() as Control)?.Name);
        window.Close();
    }
}
