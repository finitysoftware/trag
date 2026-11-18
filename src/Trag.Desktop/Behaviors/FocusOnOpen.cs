using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Trag.Desktop.Behaviors;

// Moves keyboard focus into a dialog or a form when it becomes visible: once the owner is effectively visible, the attached
// Target gets focus on the next dispatcher pass after layout (and its text selected when SelectAll is set), the way a click
// would supply it. A dialog whose Target is hidden in one of its forms names a Fallback that has the keyboard then. The owner watches its own and its ancestors' IsVisible, so a dialog opening or a form switching in counts.
public static class FocusOnOpen
{
    public static readonly AttachedProperty<Control?> TargetProperty = AvaloniaProperty.RegisterAttached<Control, Control?>("Target", typeof(FocusOnOpen));
    public static readonly AttachedProperty<bool> SelectAllProperty = AvaloniaProperty.RegisterAttached<Control, bool>("SelectAll", typeof(FocusOnOpen));
    public static readonly AttachedProperty<Control?> FallbackProperty = AvaloniaProperty.RegisterAttached<Control, Control?>("Fallback", typeof(FocusOnOpen));
    private static readonly AttachedProperty<bool> WatchedProperty = AvaloniaProperty.RegisterAttached<Control, bool>("Watched", typeof(FocusOnOpen));

    public static Control? GetTarget(Control owner) => owner.GetValue(TargetProperty);
    public static void SetTarget(Control owner, Control? value) => owner.SetValue(TargetProperty, value);
    public static bool GetSelectAll(Control owner) => owner.GetValue(SelectAllProperty);
    public static void SetSelectAll(Control owner, bool value) => owner.SetValue(SelectAllProperty, value);
    public static Control? GetFallback(Control owner) => owner.GetValue(FallbackProperty);
    public static void SetFallback(Control owner, Control? value) => owner.SetValue(FallbackProperty, value);

    static FocusOnOpen() => TargetProperty.Changed.AddClassHandler<Control>((owner, _) => Watch(owner));

    private static void Watch(Control owner)
    {
        if (owner.GetValue(WatchedProperty)) return;
        owner.SetValue(WatchedProperty, true);
        var watched = new List<Visual>();
        void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty && owner.IsEffectivelyVisible) Post(owner);
        }
        owner.AttachedToVisualTree += (_, _) =>
        {
            foreach (var visual in owner.GetSelfAndVisualAncestors()) { visual.PropertyChanged += VisibilityChanged; watched.Add(visual); }
            if (owner.IsEffectivelyVisible) Post(owner);
        };
        owner.DetachedFromVisualTree += (_, _) =>
        {
            foreach (var visual in watched) visual.PropertyChanged -= VisibilityChanged;
            watched.Clear();
        };
    }

    private static void Post(Control owner) => Dispatcher.UIThread.Post(() =>
    {
        if ((GetTarget(owner) is { IsEffectivelyVisible: true } shown ? shown : GetFallback(owner)) is not { } target) return;
        target.Focus();
        if (GetSelectAll(owner) && target is TextBox editor) editor.SelectAll();
    }, DispatcherPriority.Loaded);
}
