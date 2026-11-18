using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace Trag.Desktop.Behaviors;

// A button's flyout as the view model sees it: Command runs every time the flyout opens (the branch and stash pickers
// load their lists on opening), and IsOpen follows the flyout both ways, so a view model closes it by setting false.
public static class FlyoutOpen
{
    public static readonly AttachedProperty<ICommand?> CommandProperty = AvaloniaProperty.RegisterAttached<Button, ICommand?>("Command", typeof(FlyoutOpen));
    public static readonly AttachedProperty<bool> IsOpenProperty = AvaloniaProperty.RegisterAttached<Button, bool>("IsOpen", typeof(FlyoutOpen), defaultBindingMode: BindingMode.TwoWay);
    private static readonly AttachedProperty<bool> WatchedProperty = AvaloniaProperty.RegisterAttached<Button, bool>("Watched", typeof(FlyoutOpen));

    public static ICommand? GetCommand(Button button) => button.GetValue(CommandProperty);
    public static void SetCommand(Button button, ICommand? value) => button.SetValue(CommandProperty, value);
    public static bool GetIsOpen(Button button) => button.GetValue(IsOpenProperty);
    public static void SetIsOpen(Button button, bool value) => button.SetValue(IsOpenProperty, value);

    // A button is watched once its flyout arrives: a bound IsOpen that stays false raises no change to start from.
    static FlyoutOpen()
    {
        Button.FlyoutProperty.Changed.AddClassHandler<Button>((button, _) => Watch(button));
        IsOpenProperty.Changed.AddClassHandler<Button>((button, e) =>
        {
            if (e.NewValue is false) button.Flyout?.Hide();
        });
    }

    // The subscription follows the button into and out of the tree.
    private static void Watch(Button button)
    {
        if (button.GetValue(WatchedProperty)) return;
        button.SetValue(WatchedProperty, true);
        FlyoutBase? watched = null;
        void Opened(object? sender, EventArgs e) { button.SetCurrentValue(IsOpenProperty, true); GetCommand(button)?.Execute(null); }
        void Closed(object? sender, EventArgs e) => button.SetCurrentValue(IsOpenProperty, false);
        button.AttachedToVisualTree += (_, _) =>
        {
            watched = button.Flyout;
            if (watched is null) return;
            watched.Opened += Opened;
            watched.Closed += Closed;
        };
        button.DetachedFromVisualTree += (_, _) =>
        {
            if (watched is not null) { watched.Opened -= Opened; watched.Closed -= Closed; }
            watched = null;
        };
    }
}
