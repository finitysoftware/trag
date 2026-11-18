using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Trag.Desktop.Controls;

// A thin indeterminate bar that never flashes: it starts its animation from the first frame when activity begins,
// stays for at least one full cycle and disappears only on a cycle boundary. Its own control theme in App.axaml
// keeps the Fluent two-indicator sweep but runs it in 1 s instead of 2 s, and the default cycle matches that.
public sealed class BusyBar : ProgressBar
{
    public static readonly StyledProperty<bool> IsActiveProperty = AvaloniaProperty.Register<BusyBar, bool>(nameof(IsActive));
    public static readonly StyledProperty<TimeSpan> CycleProperty = AvaloniaProperty.Register<BusyBar, TimeSpan>(nameof(Cycle), TimeSpan.FromSeconds(1));
    public bool IsActive { get => GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public TimeSpan Cycle { get => GetValue(CycleProperty); set => SetValue(CycleProperty, value); }
    private DispatcherTimer? hide;
    private DateTime shownAt;

    public BusyBar() { IsVisible = false; IsIndeterminate = true; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsActiveProperty) return;
        if (change.GetNewValue<bool>()) Show(); else ScheduleHide();
    }
    private void Show()
    {
        hide?.Stop(); hide = null;
        if (IsVisible) return;
        // Toggling the indeterminate flag re-applies the :indeterminate style, so the animation restarts at its first frame.
        IsIndeterminate = false;
        IsVisible = true;
        IsIndeterminate = true;
        shownAt = DateTime.UtcNow;
    }
    private void ScheduleHide()
    {
        if (!IsVisible) return;
        var cycle = Cycle > TimeSpan.Zero ? Cycle : TimeSpan.FromMilliseconds(1);
        var elapsed = DateTime.UtcNow - shownAt;
        var cycles = Math.Max(1, (int)Math.Ceiling(elapsed / cycle));
        var remaining = cycle * cycles - elapsed;
        if (remaining <= TimeSpan.Zero) { IsVisible = false; return; }
        hide?.Stop();
        hide = new DispatcherTimer(remaining, DispatcherPriority.Normal, (_, _) =>
        {
            hide?.Stop(); hide = null;
            if (!IsActive) IsVisible = false;
        });
        hide.Start();
    }
}
