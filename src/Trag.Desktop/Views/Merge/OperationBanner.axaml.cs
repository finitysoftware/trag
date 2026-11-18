using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Trag.Desktop.Views.Merge;

// The banner of an operation in progress. Its texts are properties the using view binds; the banner's own content binds
// to the control, so the view's data context stays the page.
public sealed partial class OperationBanner : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<OperationBanner, string>(nameof(Title), "");
    public static readonly StyledProperty<string> DirectionProperty = AvaloniaProperty.Register<OperationBanner, string>(nameof(Direction), "");
    public static readonly StyledProperty<string> RemainingProperty = AvaloniaProperty.Register<OperationBanner, string>(nameof(Remaining), "");
    public static readonly StyledProperty<double> ProgressProperty = AvaloniaProperty.Register<OperationBanner, double>(nameof(Progress));
    public static readonly StyledProperty<string> ProgressTextProperty = AvaloniaProperty.Register<OperationBanner, string>(nameof(ProgressText), "");
    // A staged file with markers turns the banner red: the operation cannot end that way.
    public static readonly StyledProperty<bool> IsAlarmProperty = AvaloniaProperty.Register<OperationBanner, bool>(nameof(IsAlarm));
    public static readonly StyledProperty<ICommand?> ReviewCommandProperty = AvaloniaProperty.Register<OperationBanner, ICommand?>(nameof(ReviewCommand));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Direction { get => GetValue(DirectionProperty); set => SetValue(DirectionProperty, value); }
    public string Remaining { get => GetValue(RemainingProperty); set => SetValue(RemainingProperty, value); }
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public string ProgressText { get => GetValue(ProgressTextProperty); set => SetValue(ProgressTextProperty, value); }
    public bool IsAlarm { get => GetValue(IsAlarmProperty); set => SetValue(IsAlarmProperty, value); }
    public ICommand? ReviewCommand { get => GetValue(ReviewCommandProperty); set => SetValue(ReviewCommandProperty, value); }

    public OperationBanner()
    {
        InitializeComponent();
        Banner.DataContext = this;
    }
}
