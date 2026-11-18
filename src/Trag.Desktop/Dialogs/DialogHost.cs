using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace Trag.Desktop.Dialogs;

// The frame every in-window dialog shares: a scrim over the window, a centred card, a header with an icon (on a round
// plate or plain), title and subtitle, the ✕ button, Escape bound to CloseCommand, then the dialog's content and its footer.
// Each dialog derives from it in XAML and only supplies the header values, the content and the footer.
public class DialogHost : ContentControl
{
    public static readonly StyledProperty<bool> IsOpenProperty = AvaloniaProperty.Register<DialogHost, bool>(nameof(IsOpen));
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<DialogHost, string>(nameof(Title), "");
    public static readonly StyledProperty<string> SubtitleProperty = AvaloniaProperty.Register<DialogHost, string>(nameof(Subtitle), "");
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<DialogHost, string>(nameof(Icon), "");
    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<DialogHost, double>(nameof(IconSize), 30);
    public static readonly StyledProperty<IBrush?> IconBrushProperty = AvaloniaProperty.Register<DialogHost, IBrush?>(nameof(IconBrush));
    // With a plate the icon sits on a 58 px circle and the subtitle joins the header; without one the icon is drawn plain at 26 px.
    public static readonly StyledProperty<IBrush?> PlateBrushProperty = AvaloniaProperty.Register<DialogHost, IBrush?>(nameof(PlateBrush));
    public static readonly StyledProperty<double> DialogWidthProperty = AvaloniaProperty.Register<DialogHost, double>(nameof(DialogWidth), 520);
    public static readonly StyledProperty<ICommand?> CloseCommandProperty = AvaloniaProperty.Register<DialogHost, ICommand?>(nameof(CloseCommand));
    public static readonly StyledProperty<object?> FooterProperty = AvaloniaProperty.Register<DialogHost, object?>(nameof(Footer));

    public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double IconSize { get => GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public IBrush? IconBrush { get => GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public IBrush? PlateBrush { get => GetValue(PlateBrushProperty); set => SetValue(PlateBrushProperty, value); }
    public double DialogWidth { get => GetValue(DialogWidthProperty); set => SetValue(DialogWidthProperty, value); }
    public ICommand? CloseCommand { get => GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    public object? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }

    // Derived dialogs keep the one control theme.
    protected override Type StyleKeyOverride => typeof(DialogHost);

    // What had the keyboard when the dialog opened, as the button that opened it.
    private IInputElement? opener;

    static DialogHost()
    {
        IsOpenProperty.Changed.AddClassHandler<DialogHost>((host, _) => host.OpenChanged());
        IconProperty.Changed.AddClassHandler<DialogHost>((host, _) => host.UpdatePseudoClasses());
        PlateBrushProperty.Changed.AddClassHandler<DialogHost>((host, _) => host.UpdatePseudoClasses());
    }

    public DialogHost()
    {
        IsVisible = false;
        Focusable = true;
        // Escape closes from any focused control inside the dialog; the window's own Escape (cancel the running operation) stays behind it.
        var escape = new KeyBinding { Gesture = new KeyGesture(Key.Escape) };
        escape[!KeyBinding.CommandProperty] = this[!CloseCommandProperty];
        KeyBindings.Add(escape);
        // A screen reader meets the card as a window named by its title, as it meets a dialog window.
        this[!AutomationProperties.NameProperty] = this[!TitleProperty];
        AutomationProperties.SetControlTypeOverride(this, AutomationControlType.Window);
    }

    // Closing gives the keyboard back to what had it when the dialog opened, and the dialog left on top takes it when that
    // went away with it, so a dialog opened over another leaves the keys with that one instead of with nothing, which let
    // them reach the window behind both.
    private void OpenChanged()
    {
        IsVisible = IsOpen;
        if (IsOpen)
        {
            opener = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(KeepFocus, DispatcherPriority.Background);
            return;
        }
        (opener as InputElement)?.Focus();
        if (Topmost() is { } under) Dispatcher.UIThread.Post(under.KeepFocus, DispatcherPriority.Background);
    }

    // The open dialog drawn over the others of its window: the last open one in their XAML order.
    private DialogHost? Topmost() => (Parent as Panel)?.Children.OfType<DialogHost>().LastOrDefault(host => host.IsOpen);

    // A dialog that names no control to focus, or whose control is hidden for what it shows now, takes the focus itself,
    // so its keys reach it; FocusOnOpen's own focus runs first.
    private void KeepFocus()
    {
        if (IsOpen && !IsKeyboardFocusWithin) Focus();
    }

    // Tab from the dialog itself, which holds the keys when it names no control, goes into it: Avalonia passes a focused
    // container by to the window's next control behind the scrim.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.Source == this)
            e.Handled = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? FocusManager.FindLastFocusableElement(this) : FocusManager.FindFirstFocusableElement(this))?
                .Focus(NavigationMethod.Tab, e.KeyModifiers) == true;
        base.OnKeyDown(e);
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Set(":icon", Icon.Length != 0);
        PseudoClasses.Set(":plate", PlateBrush is not null);
    }
}
